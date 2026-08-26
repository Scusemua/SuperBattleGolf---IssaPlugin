using System.Collections;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Flow:
    ///   1. Client presses use → sends HarrierRequestMessage.
    ///   2. Server receives it → ServerHandleRequest() starts ServerHarrierRoutine.
    ///   3. Server spawns the Harrier prefab and attaches HarrierBehaviour.
    ///   4. Harrier flies in autonomously, fires rockets at players, then flies out.
    ///   5. All clients receive HarrierBeginClientMessage / HarrierEndClientMessage.
    ///
    /// The Harrier is fully server-authoritative — no client input is ever sent after
    /// the initial use request. Per-player guard prevents stacking; there is no global
    /// one-at-a-time lock, so two players can each summon their own Harrier.
    /// </summary>
    public class HarrierNetworkBridge : NetworkBridgeBase
    {
        // ================================================================
        //  Server state
        // ================================================================

        /// <summary>
        /// Set by the server message handler for HarrierPrepareHomingMessage while
        /// the owning client has the Harrier locked on. Consumed by RocketHomingPatch
        /// when the next player-fired rocket spawns.
        /// </summary>
        public bool PendingHarrierHoming;

        private bool _serverRoutineActive;
        private bool _shotDown;
        private Coroutine _serverRoutine;
        private GameObject _serverHarrier;

        // ================================================================
        //  Client state (local client only)
        // ================================================================

        /// <summary>True on the local client while any Harrier session is active.</summary>
        public bool LocalSessionActive { get; private set; }

        // ================================================================
        //  Mirror lifecycle
        // ================================================================

        public override void OnStopServer()
        {
            if (_serverRoutineActive)
                ForceServerCleanup();
        }

        // ================================================================
        //  Server — entry point
        // ================================================================

        public void ServerHandleRequest()
        {
            if (_serverRoutineActive)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Harrier] Session already active for this player — ignoring."
                );
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogError("[Harrier] No PlayerInventory on bridge object.");
                return;
            }

            _serverRoutine = StartCoroutine(ServerHarrierRoutine(inventory));
        }

        // ================================================================
        //  Server coroutine
        // ================================================================

        private IEnumerator ServerHarrierRoutine(PlayerInventory inventory)
        {
            _serverRoutineActive = true;
            ItemHelper.ConsumeEquippedItem(inventory);

            // ── Choose hover position ────────────────────────────────────
            Vector3 mapCenter = ComputeMapCenter();
            float altitude = ModConfig.Harrier.Altitude.Value;
            Vector3 hoverPos = new Vector3(mapCenter.x, mapCenter.y + altitude, mapCenter.z);

            // Approach from a random horizontal direction.
            float approachAngle = Random.value * 360f * Mathf.Deg2Rad;
            Vector3 approachDir = new Vector3(
                Mathf.Cos(approachAngle),
                0f,
                Mathf.Sin(approachAngle)
            );
            float approachDist = ModConfig.Harrier.ApproachDistance.Value;
            Vector3 spawnPos = hoverPos + approachDir * approachDist;
            // Fly-out destination: continue beyond the hover point in the same direction.
            Vector3 flyOutPos = hoverPos + approachDir * (approachDist * 1.5f);

            // Raise the whole path if any terrain along it out-tops the hover altitude,
            // then re-derive the endpoints from the corrected hover height.
            hoverPos.y = TerrainHeight.ClearPath(spawnPos, hoverPos, flyOutPos, altitude);
            spawnPos = hoverPos + approachDir * approachDist;
            flyOutPos = hoverPos + approachDir * (approachDist * 1.5f);

            // ── Spawn harrier ────────────────────────────────────────────
            GameObject harrierGo = SpawnHarrierObject(spawnPos, hoverPos);
            if (harrierGo == null)
            {
                IssaPluginPlugin.Log.LogError("[Harrier] Failed to spawn harrier object.");
                _serverRoutineActive = false;
                yield break;
            }

            _serverHarrier = harrierGo;
            _shotDown = false;

            // Attach the movement driver (server-only; not replicated to clients).
            var behaviour = harrierGo.AddComponent<HarrierBehaviour>();
            behaviour.HoverTarget = hoverPos;
            behaviour.FlyOutTarget = flyOutPos;
            behaviour.ApproachSpeed = ModConfig.Harrier.ApproachSpeed.Value;
            behaviour.DriftSpeed = ModConfig.Harrier.DriftSpeed.Value;
            behaviour.HoverRadius = ModConfig.Harrier.HoverRadius.Value;

            // Attach the hit receiver so the jet can be shot down immediately
            // (even during fly-in). The OnHitsExceeded callback captures locals
            // from this coroutine so it can initiate the crash without polling.
            var hitReceiver = harrierGo.AddComponent<HarrierHitReceiver>();
            var harrierIdentity = harrierGo.GetComponent<NetworkIdentity>();
            ItemWarningBroadcaster.Broadcast(
                inventory.PlayerInfo.PlayerId.PlayerName,
                ItemRegistry.HarrierItemType,
                "Harrier Jet",
                trackedNetId: harrierIdentity.netId,
                senderNetId: netId
            );
            hitReceiver.OnHit += () =>
            {
                if (
                    hitReceiver.HitsRequired > 0
                    && hitReceiver.HitCount > 0
                    && hitReceiver.HitCount < hitReceiver.HitsRequired
                )
                {
                    IssaPluginPlugin.Log.LogInfo(
                        $"[Harrier] Damaged ({hitReceiver.HitCount}/{hitReceiver.HitsRequired}) — broadcasting smoke."
                    );
                    NetworkServer.SendToAll(
                        new HarrierDamagedMessage { HarrierNetId = harrierIdentity.netId }
                    );
                }
            };

            hitReceiver.OnHitsExceeded += () =>
            {
                if (_shotDown || harrierGo == null)
                    return;

                _shotDown = true;
                IssaPluginPlugin.Log.LogInfo("[Harrier] Shot down — initiating crash.");

                NetworkServer.SendToAll(
                    new HarrierShotDownMessage { HarrierNetId = harrierIdentity.netId }
                );

                var crash = harrierGo.AddComponent<HarrierCrashBehaviour>();
                crash.ThrowerInventory = inventory;
                crash.KillingRocketDir = (
                    harrierGo.transform.position - hitReceiver.LastHitWorldPos
                ).normalized;
                crash.ExplosionScale = ModConfig.Harrier.CrashExplosionScale.Value;
            };

            IssaPluginPlugin.Log.LogInfo(
                $"[Harrier] Jet spawned at {spawnPos:F0}, heading to {hoverPos:F0}."
            );

            NetworkServer.SendToAll(new HarrierBeginClientMessage { HoverCenter = mapCenter });

            // ── Wait for fly-in ──────────────────────────────────────────
            float flyInTimeout =
                approachDist / Mathf.Max(ModConfig.Harrier.ApproachSpeed.Value, 1f) + 8f;
            float elapsed = 0f;

            while (
                harrierGo != null
                && behaviour != null
                && !behaviour.HasArrived
                && !_shotDown
                && elapsed < flyInTimeout
            )
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (harrierGo == null || behaviour == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Harrier] Jet destroyed before arriving — aborting session."
                );
                ForceServerCleanup();
                yield break;
            }

            IssaPluginPlugin.Log.LogInfo("[Harrier] Jet arrived — beginning attack phase.");

            // ── Attack phase ─────────────────────────────────────────────
            float duration = ModConfig.Harrier.Duration.Value;
            float fireInterval = ModConfig.Harrier.FireInterval.Value;
            bool friendlyFire = ModConfig.Harrier.FriendlyFire.Value;

            float sessionElapsed = 0f;
            // Stagger the first shot by half an interval so the jet isn't
            // firing before it has fully settled into its hover position.
            float fireCooldown = fireInterval * 0.5f;

            while (sessionElapsed < duration && harrierGo != null && !_shotDown)
            {
                sessionElapsed += Time.deltaTime;
                fireCooldown -= Time.deltaTime;

                if (fireCooldown <= 0f)
                {
                    fireCooldown = fireInterval;
                    HarrierItem.FireAtRandomTarget(
                        inventory,
                        harrierGo.transform.position,
                        friendlyFire
                    );
                }

                yield return null;
            }

            // ── Exit phase: normal fly-out OR wait for crash to complete ───
            if (_shotDown)
            {
                // Jet was shot down — wait for HarrierCrashBehaviour to detect
                // ground impact before we NetworkServer.Destroy it.
                float crashTimeout = 20f;
                elapsed = 0f;
                var crashBehaviour = harrierGo?.GetComponent<HarrierCrashBehaviour>();
                while (
                    harrierGo != null
                    && crashBehaviour != null
                    && !crashBehaviour.IsComplete
                    && elapsed < crashTimeout
                )
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }
            else if (harrierGo != null && behaviour != null)
            {
                // Session expired normally — fly the jet off the map.
                behaviour.BeginFlyOut();
                IssaPluginPlugin.Log.LogInfo("[Harrier] Beginning fly-out.");

                float flyOutTimeout = 12f;
                elapsed = 0f;
                while (
                    harrierGo != null
                    && behaviour != null
                    && !behaviour.IsComplete
                    && elapsed < flyOutTimeout
                )
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }
            }

            // ── Cleanup ──────────────────────────────────────────────────
            if (harrierGo != null)
                NetworkServer.Destroy(harrierGo);

            NetworkServer.SendToAll(new HarrierEndClientMessage());

            _serverHarrier = null;
            _serverRoutineActive = false;
            _serverRoutine = null;

            IssaPluginPlugin.Log.LogInfo("[Harrier] Session complete.");
        }

        // ================================================================
        //  Helpers
        // ================================================================

        /// <summary>
        /// Returns the centroid of all connected players, with y set to the ground
        /// height beneath that centroid. Altitude is added separately by the caller.
        ///
        /// Ground height is sampled rather than assumed to be y = 0: on hilly maps (the
        /// ice maps especially) the terrain sits well above the world origin, so a fixed
        /// y = 0 base put the Harrier's hover point underground.
        ///
        /// Falls back to world origin if no players are found.
        /// </summary>
        private static Vector3 ComputeMapCenter()
        {
            var players = Object.FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None);
            if (players.Length == 0)
                return Vector3.zero;

            Vector3 sum = Vector3.zero;
            foreach (var p in players)
                sum += p.transform.position;

            Vector3 centroid = sum / players.Length;

            // If no ground is found under the centroid, the players' own average height
            // stands in — a far better estimate than the world origin.
            centroid.y = TerrainHeight.GroundHeightAt(centroid, centroid.y);
            return centroid;
        }

        /// <summary>
        /// Instantiates the Harrier prefab, ensures it has a Rigidbody, faces toward
        /// the hover point, and spawns it on the network.
        /// AssetLoader.Load() always sets HarrierPrefab (bundle asset or fallback primitive),
        /// so this should never encounter a null prefab after startup.
        /// </summary>
        private static GameObject SpawnHarrierObject(Vector3 spawnPos, Vector3 hoverPos)
        {
            if (AssetLoader.HarrierPrefab == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[Harrier] HarrierPrefab is null — was AssetLoader.Load() called?"
                );
                return null;
            }

            var harrierGo = Object.Instantiate(
                AssetLoader.HarrierPrefab,
                spawnPos,
                Quaternion.identity
            );

            if (harrierGo == null)
                return null;

            // Ensure the Rigidbody exists and is kinematic (HarrierBehaviour will use it).
            var rigidBody = harrierGo.GetComponent<Rigidbody>();
            if (rigidBody == null)
            {
                rigidBody = harrierGo.AddComponent<Rigidbody>();
                rigidBody.isKinematic = true;
                rigidBody.useGravity = false;
            }

            // Orient toward the hover point on spawn.
            Vector3 toHover = hoverPos - spawnPos;
            if (toHover.sqrMagnitude > 0.01f)
                harrierGo.transform.rotation = Quaternion.LookRotation(toHover.normalized);

            // Server AI drives this object's position; make sure the prefab's
            // NetworkTransform can't overwrite it from the local client.
            ServerAuthoritativeTransform.Apply(harrierGo, "Harrier");

            NetworkServer.Spawn(harrierGo);
            return harrierGo;
        }

        private void ForceServerCleanup()
        {
            if (_serverRoutine != null)
            {
                StopCoroutine(_serverRoutine);
                _serverRoutine = null;
            }

            if (_serverHarrier != null)
            {
                NetworkServer.Destroy(_serverHarrier);
                _serverHarrier = null;
            }

            _serverRoutineActive = false;
        }

        // ================================================================
        //  Client handlers
        // ================================================================

        public static void ClientHandleBegin(HarrierBeginClientMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo($"[Harrier] Inbound — hover center {msg.HoverCenter:F0}.");
            var local = NetworkClient.localPlayer?.GetComponent<HarrierNetworkBridge>();
            if (local != null)
                local.LocalSessionActive = true;
        }

        public static void ClientHandleEnd(HarrierEndClientMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo("[Harrier] Session ended.");
            var local = NetworkClient.localPlayer?.GetComponent<HarrierNetworkBridge>();
            if (local != null)
                local.LocalSessionActive = false;
        }

        public static void ClientHandleDamaged(HarrierDamagedMessage msg)
        {
            if (AssetLoader.MaydaySmokeTrailPrefab == null)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.HarrierNetId, out var ni) || ni == null)
                return;

            IssaPluginPlugin.Log.LogInfo("[Harrier] Spawning damage smoke trail.");
            var smoke = Object.Instantiate(
                AssetLoader.MaydaySmokeTrailPrefab,
                ni.transform.position,
                Quaternion.identity
            );
            smoke.transform.SetParent(ni.transform, worldPositionStays: true);
        }

        public static void ClientHandleShotDown(HarrierShotDownMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo("[Harrier] Shot down!");

            var localBridge = NetworkClient.localPlayer?.GetComponent<HarrierNetworkBridge>();
            if (localBridge != null && localBridge.LocalSessionActive)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.HarrierNetId, out var ni) || ni == null)
                return;

            var harrierGo = ni.gameObject;
            if (harrierGo.GetComponent<HarrierCrashBehaviour>() == null)
            {
                var crashBehaviour = harrierGo.AddComponent<HarrierCrashBehaviour>();
            }
        }

        // ================================================================
        //  Hole-transition cleanup
        // ================================================================

        public override void ServerHoleCleanup()
        {
            // Clear all in-flight rocket entries so they don't linger if rockets
            // are destroyed by the hole transition before ServerExplode fires.
            // Safe to call on every player's bridge — Clear() is idempotent.
            HarrierItem.ActiveHarrierRocketIds.Clear();

            if (_serverRoutineActive)
                ForceServerCleanup();
        }

        public override void ClientHoleCleanup()
        {
            LocalSessionActive = false;
        }
    }
}
