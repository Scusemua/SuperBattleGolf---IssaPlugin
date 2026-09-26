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
        private Coroutine _serverRoutine;
        private GameObject _serverHarrier;

        /// <summary>
        /// Straight-line inbound path: spawn uprange of the station point, hover there,
        /// then continue past it to leave. Endpoints are raised together so the whole
        /// path clears terrain.
        /// </summary>
        private readonly struct FlightPlan
        {
            public readonly Vector3 MapCenter;
            public readonly Vector3 HoverPosition;
            public readonly Vector3 SpawnPosition;
            public readonly Vector3 FlyOutPosition;
            public readonly float ApproachDistance;

            public FlightPlan(
                Vector3 mapCenter,
                Vector3 hoverPosition,
                Vector3 spawnPosition,
                Vector3 flyOutPosition,
                float approachDistance
            )
            {
                MapCenter = mapCenter;
                HoverPosition = hoverPosition;
                SpawnPosition = spawnPosition;
                FlyOutPosition = flyOutPosition;
                ApproachDistance = approachDistance;
            }
        }

        /// <summary>
        /// State for one server-side run, shared by the phase methods and the hit
        /// callbacks. The shot-down flag lives here so a hit during fly-in and the
        /// coroutine agree without a field on the player bridge.
        /// </summary>
        private sealed class ServerSession
        {
            public readonly PlayerInventory Inventory;
            public readonly FlightPlan Plan;

            public GameObject Vehicle;
            public HarrierBehaviour Behaviour;
            public bool IsShotDown;

            public ServerSession(PlayerInventory inventory, FlightPlan plan)
            {
                Inventory = inventory;
                Plan = plan;
            }
        }

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

            var session = new ServerSession(inventory, PlanFlight());

            if (!TrySpawnHarrier(session))
            {
                IssaPluginPlugin.Log.LogError("[Harrier] Failed to spawn harrier object.");
                _serverRoutineActive = false;
                yield break;
            }

            ArmHarrier(session);
            AnnounceInbound(session);

            foreach (object step in EachStep(WaitForArrival(session)))
                yield return step;

            if (session.Vehicle == null || session.Behaviour == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Harrier] Jet destroyed before arriving — aborting session."
                );
                ForceServerCleanup();
                yield break;
            }

            IssaPluginPlugin.Log.LogInfo("[Harrier] Jet arrived — beginning attack phase.");

            if (!session.IsShotDown)
            {
                foreach (object step in EachStep(EngageTargets(session)))
                    yield return step;
            }

            foreach (object step in EachStep(ExitPhase(session)))
                yield return step;

            FinishSession(session);
        }

        // ================================================================
        //  Session phases
        // ================================================================

        private static FlightPlan PlanFlight()
        {
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
            const float flyOutDistanceMultiplier = 1.5f;
            Vector3 flyOutPos = hoverPos + approachDir * (approachDist * flyOutDistanceMultiplier);

            // Raise the whole path if any terrain along it out-tops the hover altitude,
            // then re-derive the endpoints from the corrected hover height.
            hoverPos.y = TerrainHeight.ClearPath(spawnPos, hoverPos, flyOutPos, altitude);
            spawnPos = hoverPos + approachDir * approachDist;
            flyOutPos = hoverPos + approachDir * (approachDist * flyOutDistanceMultiplier);

            return new FlightPlan(mapCenter, hoverPos, spawnPos, flyOutPos, approachDist);
        }

        private bool TrySpawnHarrier(ServerSession session)
        {
            GameObject harrierGo = SpawnHarrierObject(
                session.Plan.SpawnPosition,
                session.Plan.HoverPosition
            );
            if (harrierGo == null)
                return false;

            _serverHarrier = harrierGo;
            session.Vehicle = harrierGo;
            session.IsShotDown = false;
            return true;
        }

        /// <summary>
        /// Attaches the server-only flight driver and the hit receiver. The jet can be
        /// shot down as soon as this returns, including during fly-in. The destruction
        /// callback starts the crash itself; the session only watches
        /// <see cref="ServerSession.IsShotDown"/> and waits for impact.
        /// </summary>
        private void ArmHarrier(ServerSession session)
        {
            var harrierGo = session.Vehicle;

            var behaviour = harrierGo.AddComponent<HarrierBehaviour>();
            behaviour.HoverTarget = session.Plan.HoverPosition;
            behaviour.FlyOutTarget = session.Plan.FlyOutPosition;
            behaviour.ApproachSpeed = ModConfig.Harrier.ApproachSpeed.Value;
            behaviour.DriftSpeed = ModConfig.Harrier.DriftSpeed.Value;
            behaviour.HoverRadius = ModConfig.Harrier.HoverRadius.Value;
            session.Behaviour = behaviour;

            var hitReceiver = harrierGo.AddComponent<HarrierHitReceiver>();
            var harrierIdentity = harrierGo.GetComponent<NetworkIdentity>();
            ItemWarningBroadcaster.Broadcast(
                session.Inventory.PlayerInfo.PlayerId.PlayerName,
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
                if (session.IsShotDown || harrierGo == null)
                    return;

                session.IsShotDown = true;
                IssaPluginPlugin.Log.LogInfo("[Harrier] Shot down — initiating crash.");

                NetworkServer.SendToAll(
                    new HarrierShotDownMessage { HarrierNetId = harrierIdentity.netId }
                );

                var crash = harrierGo.AddComponent<HarrierCrashBehaviour>();
                crash.ThrowerInventory = session.Inventory;
                crash.KillingRocketDir = (
                    harrierGo.transform.position - hitReceiver.LastHitWorldPos
                ).normalized;
                crash.ExplosionScale = ModConfig.Harrier.CrashExplosionScale.Value;
            };
        }

        private static void AnnounceInbound(ServerSession session)
        {
            IssaPluginPlugin.Log.LogInfo(
                $"[Harrier] Jet spawned at {session.Plan.SpawnPosition:F0}, heading to {session.Plan.HoverPosition:F0}."
            );

            NetworkServer.SendToAll(
                new HarrierBeginClientMessage { HoverCenter = session.Plan.MapCenter }
            );
        }

        private static IEnumerator WaitForArrival(ServerSession session)
        {
            float flyInTimeout =
                session.Plan.ApproachDistance / Mathf.Max(ModConfig.Harrier.ApproachSpeed.Value, 1f)
                + 8f;

            return WaitWhile(
                flyInTimeout,
                () =>
                    session.Vehicle != null
                    && session.Behaviour != null
                    && !session.Behaviour.HasArrived
                    && !session.IsShotDown
            );
        }

        private static IEnumerator EngageTargets(ServerSession session)
        {
            float fireInterval = ModConfig.Harrier.FireInterval.Value;
            bool friendlyFire = ModConfig.Harrier.FriendlyFire.Value;
            // Stagger the first shot by half an interval so the jet isn't
            // firing before it has fully settled into its hover position.
            float openingDelay = fireInterval * 0.5f;

            return EngageFor(
                ModConfig.Harrier.Duration.Value,
                fireInterval,
                openingDelay,
                () => session.Vehicle != null,
                () => session.IsShotDown,
                () =>
                    HarrierItem.FireAtRandomTarget(
                        session.Inventory,
                        session.Vehicle.transform.position,
                        friendlyFire
                    )
            );
        }

        /// <summary>
        /// Shot down: wait until the crash behaviour reports impact.
        /// Otherwise fly the jet off the map. If the vehicle is already gone, finishes immediately.
        /// </summary>
        private static IEnumerator ExitPhase(ServerSession session)
        {
            if (session.IsShotDown)
                return WaitForCrash(session);

            if (session.Vehicle != null && session.Behaviour != null)
                return FlyOut(session);

            return Finished();
        }

        private static IEnumerator WaitForCrash(ServerSession session)
        {
            // Wait for HarrierCrashBehaviour to detect ground impact before
            // FinishSession calls NetworkServer.Destroy.
            const float crashTimeout = 20f;
            var crashBehaviour =
                session.Vehicle != null
                    ? session.Vehicle.GetComponent<HarrierCrashBehaviour>()
                    : null;

            return WaitWhile(
                crashTimeout,
                () =>
                    session.Vehicle != null && crashBehaviour != null && !crashBehaviour.IsComplete
            );
        }

        private static IEnumerator FlyOut(ServerSession session)
        {
            session.Behaviour.BeginFlyOut();
            IssaPluginPlugin.Log.LogInfo("[Harrier] Beginning fly-out.");

            const float flyOutTimeout = 12f;
            return WaitWhile(
                flyOutTimeout,
                () =>
                    session.Vehicle != null
                    && session.Behaviour != null
                    && !session.Behaviour.IsComplete
            );
        }

        private void FinishSession(ServerSession session)
        {
            if (session.Vehicle != null)
                NetworkServer.Destroy(session.Vehicle);

            NetworkServer.SendToAll(new HarrierEndClientMessage());

            _serverHarrier = null;
            _serverRoutineActive = false;
            _serverRoutine = null;

            IssaPluginPlugin.Log.LogInfo("[Harrier] Session complete.");
        }

        // ================================================================
        //  Phase primitives
        //
        //  These are Harrier-agnostic. An autonomous gunship can reuse the wait
        //  and the timed firing loop, and supply its own flight plan and weapons.
        // ================================================================

        /// <summary>
        /// Frames of <paramref name="phase"/>, yielded by <see cref="ServerHarrierRoutine"/>
        /// so the whole session stays one coroutine. <see cref="ForceServerCleanup"/> stops
        /// that coroutine; a nested <c>StartCoroutine</c> would keep running after it.
        /// </summary>
        private static IEnumerable EachStep(IEnumerator phase)
        {
            while (phase.MoveNext())
                yield return phase.Current;
        }

        /// <summary>An already-finished phase, for an exit that has nothing to wait on.</summary>
        private static IEnumerator Finished()
        {
            yield break;
        }

        /// <summary>
        /// Yields one frame at a time until <paramref name="shouldContinue"/> is false
        /// or <paramref name="timeoutSeconds"/> elapses.
        /// </summary>
        private static IEnumerator WaitWhile(float timeoutSeconds, System.Func<bool> shouldContinue)
        {
            float elapsed = 0f;
            while (shouldContinue() && elapsed < timeoutSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        /// <summary>
        /// Timed firing loop. The first shot waits <paramref name="openingDelay"/>;
        /// later shots wait <paramref name="fireInterval"/>. Stops when the duration
        /// elapses, the vehicle is gone, or the engagement is aborted.
        /// </summary>
        private static IEnumerator EngageFor(
            float duration,
            float fireInterval,
            float openingDelay,
            System.Func<bool> isVehiclePresent,
            System.Func<bool> isAborted,
            System.Action fire
        )
        {
            float elapsed = 0f;
            float cooldown = openingDelay;

            while (elapsed < duration && isVehiclePresent() && !isAborted())
            {
                elapsed += Time.deltaTime;
                cooldown -= Time.deltaTime;

                if (cooldown <= 0f)
                {
                    cooldown = fireInterval;
                    fire();
                }

                yield return null;
            }
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
