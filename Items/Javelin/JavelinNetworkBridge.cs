using System.Collections;
using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Attached to every player object via <see cref="NetworkBridgePatches"/>.
    ///
    /// Flow:
    ///   1. Client aims at ground → stores TargetPosition locally.
    ///   2. Client presses use → sends JavelinFireMessage with TargetPosition.
    ///   3. Server receives it, runs ServerFireRoutine (spawns rocket, attaches
    ///      JavelinRocketBehaviour, waits for completion).
    ///   4. Server sends JavelinLaunchedMessage back to firing client (HUD hint).
    ///   5. When done, server sends JavelinDetonatedMessage so client clears state.
    public class JavelinNetworkBridge : NetworkBridgeBase
    {
        // ================================================================
        //  Client state  (owning client only)
        // ================================================================

        /// World-space ground position under the player's crosshair.
        /// Updated every frame on the local client while the item is equipped.
        public Vector3 ClientLockedTarget { get; private set; }

        /// True when a valid ground position has been locked (crosshair hits terrain).
        public bool HasLock { get; private set; }

        /// True from fire until detonation — prevents double-fire.
        public bool IsWaitingForDetonation { get; private set; }

        // ================================================================
        //  Server state  (server only, per-instance)
        // ================================================================

        private bool _serverRoutineActive;
        private Coroutine _serverRoutine;
        private Camera _camera;

        // ================================================================
        //  Mirror lifecycle
        // ================================================================

        public override void OnStopServer()
        {
            // Nothing extra needed — the rocket will clean itself up when
            // the player disconnects mid-flight.
        }

        // ================================================================
        //  Client-side lock-on update
        //  Called each frame by JavelinLockOnUpdater (client-only component).
        // ================================================================

        /// <summary>
        /// Called every frame on the local client to raycast and update the
        /// lock-on ground target.  Should only run when the Javelin is equipped.
        /// </summary>
        public void ClientUpdateLockOn()
        {
            // Raycast from camera through crosshair into terrain/ground.
            _camera ??= Camera.main;
            var cam = _camera;
            if (cam == null)
            {
                HasLock = false;
                return;
            }

            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, 2000f, ItemHelper.GroundLayerMask))
            {
                ClientLockedTarget = hit.point;
                HasLock = true;
            }
            else
            {
                // Fallback: project ray to Y=0 plane.
                if (Mathf.Abs(ray.direction.y) > 0.001f)
                {
                    float t = -ray.origin.y / ray.direction.y;
                    if (t > 0f)
                    {
                        ClientLockedTarget = ray.origin + ray.direction * t;
                        HasLock = true;
                        return;
                    }
                }
                HasLock = false;
            }
        }

        /// <summary>
        /// Clears the local lock-on state (e.g. when the item is unequipped
        /// or after the rocket fires).
        /// </summary>
        public void ClientClearLock()
        {
            HasLock = false;
            IsWaitingForDetonation = false;
        }

        // ================================================================
        //  Client → Server  (called from ItemUsagePatches)
        // ================================================================

        /// <summary>
        /// Called on the local client when the player activates the Javelin.
        /// Sends the locked target position to the server.
        /// </summary>
        public void ClientFire(Vector3 targetPosition)
        {
            if (IsWaitingForDetonation)
            {
                IssaPluginPlugin.Log.LogWarning("[Javelin] Already waiting for detonation.");
                return;
            }

            IsWaitingForDetonation = true;
            NetworkClient.Send(new JavelinFireMessage { TargetPosition = targetPosition });
        }

        // ================================================================
        //  Server-side handler (called from NetworkManagerPatches message handler)
        // ================================================================

        public void ServerHandleFire(Vector3 targetPosition)
        {
            if (_serverRoutineActive)
            {
                IssaPluginPlugin.Log.LogWarning("[Javelin] Server routine already active.");
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogError("[Javelin] No PlayerInventory on bridge object.");
                return;
            }

            _serverRoutineActive = true;
            _serverRoutine = StartCoroutine(ServerFireRoutine(inventory, targetPosition));
        }

        // ================================================================
        //  Server fire routine
        // ================================================================

        private IEnumerator ServerFireRoutine(PlayerInventory inventory, Vector3 targetPosition)
        {
            ItemHelper.ConsumeEquippedItem(inventory);

            var playerInfo = inventory.PlayerInfo;
            Vector3 spawnPos = playerInfo.transform.position + Vector3.up * 2f;
            Quaternion spawnRot = Quaternion.LookRotation(Vector3.up, Vector3.forward);

            var useIndex = JavelinItem.NextUseIndex();
            var itemUseId = new ItemUseId(
                playerInfo.PlayerId.Guid,
                useIndex,
                ItemType.RocketLauncher,
                false
            );

            var rocket = Object.Instantiate(
                GameManager.ItemSettings.RocketPrefab,
                spawnPos,
                spawnRot
            );

            if (rocket == null)
            {
                IssaPluginPlugin.Log.LogError("[Javelin] Rocket prefab failed to instantiate.");
                _serverRoutineActive = false;
                yield break;
            }

            // Prevent this rocket from being assigned homing behaviour.
            rocket.gameObject.AddComponent<CustomSpawnedRocket>();

            rocket.ServerInitialize(playerInfo, null, itemUseId);
            NetworkServer.Spawn(rocket.gameObject, (NetworkConnectionToClient)null);

            // Register for large explosion scaling.
            ExplosionScaler.Register(rocket, ModConfig.Javelin.ExplosionScale.Value);

            // Attach the flight behaviour.
            var flight = rocket.gameObject.AddComponent<JavelinRocketBehaviour>();
            flight.TargetPosition = targetPosition;
            flight.ApexHeightAboveLaunch = ModConfig.Javelin.ApexHeight.Value;
            flight.AscentSpeed = ModConfig.Javelin.AscentSpeed.Value;
            flight.DiveSpeed = ModConfig.Javelin.DiveSpeed.Value;
            flight.DiveAcceleration = ModConfig.Javelin.DiveAcceleration.Value;
            flight.ArrivalRadius = ModConfig.Javelin.ArrivalRadius.Value;

            IssaPluginPlugin.Log.LogInfo(
                $"[Javelin] Rocket spawned. Target={targetPosition}, "
                    + $"Apex+{ModConfig.Javelin.ApexHeight.Value} units."
            );

            // Give Mirror one frame to propagate the new NetworkIdentity to clients.
            yield return null;

            // Tell all clients to attach the trail VFX to the rocket.
            NetworkServer.SendToAll(new JavelinRocketTrailMessage { RocketNetId = rocket.netId });

            // Notify the firing client the rocket is in the air.
            connectionToClient.Send(new JavelinLaunchedMessage());

            // Wait for the rocket to die (naturally or via timeout).
            float elapsed = 0f;
            float timeout = ModConfig.Javelin.Timeout.Value;

            while (rocket != null && rocket.gameObject != null && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Force-detonate if still alive at timeout.
            if (rocket != null && rocket.gameObject != null)
            {
                var flightComp = rocket.gameObject.GetComponent<JavelinRocketBehaviour>();
                if (flightComp != null)
                    flightComp.Detonate();
                else
                    ForceServerExplode(rocket);
            }

            // Notify client the sequence is complete.
            if (connectionToClient != null)
                connectionToClient.Send(new JavelinDetonatedMessage());

            _serverRoutineActive = false;
            _serverRoutine = null;
            IssaPluginPlugin.Log.LogInfo("[Javelin] Server routine complete.");
        }

        // ================================================================
        //  Hole-transition cleanup
        // ================================================================

        /// <summary>
        /// Called by Plugin.OnMatchStateChanged on every hole transition.
        /// Stops the in-flight coroutine so _serverRoutineActive does not
        /// permanently block new Javelin uses after a hole ends mid-flight.
        /// </summary>
        public override void ServerHoleCleanup()
        {
            if (!_serverRoutineActive)
                return;

            if (_serverRoutine != null)
            {
                StopCoroutine(_serverRoutine);
                _serverRoutine = null;
            }

            _serverRoutineActive = false;
            IssaPluginPlugin.Log.LogInfo("[Javelin] Server state cleared on hole transition.");
        }

        public override void ClientHoleCleanup()
        {
            ClientClearLock();
        }

        // ================================================================
        //  Client-side server-message handlers
        // ================================================================

        /// Called when the server confirms the rocket is airborne.
        public void ClientHandleLaunched()
        {
            IssaPluginPlugin.Log.LogInfo("[Javelin] Rocket launched — watching for impact.");
        }

        /// Called when the server reports the rocket has detonated.
        public void ClientHandleDetonated()
        {
            IsWaitingForDetonation = false;
            HasLock = false;
            IssaPluginPlugin.Log.LogInfo("[Javelin] Detonation confirmed — lock cleared.");
        }

        // ================================================================
        //  NetworkMessage handlers (registered by NetworkManagerPatches)
        // ================================================================

        internal static void HandleRocketTrailVfx(JavelinRocketTrailMessage msg)
        {
            if (AssetLoader.JavelinTrailVfxPrefab == null)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.RocketNetId, out var ni) || ni == null)
                return;

            var trail = Instantiate(
                AssetLoader.JavelinTrailVfxPrefab,
                ni.transform.position,
                ni.transform.rotation
            );
            trail.transform.SetParent(ni.transform, worldPositionStays: false);
            trail.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            var detacher = ni.gameObject.AddComponent<JavelinRocketTrailDetacher>();
            detacher.TrailRoot = trail.transform;
        }

        internal static void HandleExplosionVfx(JavelinExplosionMessage msg)
        {
            if (AssetLoader.JavelinExplosionVfxPrefab == null)
                return;

            var vfx = Instantiate(
                AssetLoader.JavelinExplosionVfxPrefab,
                msg.Position,
                Quaternion.identity
            );
            Destroy(vfx, ModConfig.Javelin.ExplosionVfxDuration.Value);
        }

        // ================================================================
        //  Helpers
        // ================================================================

        private static readonly MethodInfo ServerExplodeMethod = typeof(Rocket).GetMethod(
            "ServerExplode",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        private static void ForceServerExplode(Rocket rocket)
        {
            ServerExplodeMethod?.Invoke(rocket, new object[] { rocket.transform.position });
        }
    }
}
