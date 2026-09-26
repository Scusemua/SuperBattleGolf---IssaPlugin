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
    ///   2. Server receives it → ServerHandleRequest() starts an AutonomousVehicleSession.
    ///   3. The session spawns the Harrier prefab and HarrierLogic arms it.
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

        private AutonomousVehicleSession _session;

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
            if (_session != null && _session.IsActive)
                _session.Abort();
        }

        // ================================================================
        //  Server — entry point
        // ================================================================

        public void ServerHandleRequest()
        {
            if (_session != null && _session.IsActive)
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

            _session ??= new AutonomousVehicleSession(this, new HarrierLogic(this));
            _session.Begin(inventory);
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
                harrierGo.AddComponent<HarrierCrashBehaviour>();
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

            // No end message: ClientHoleCleanup clears LocalSessionActive on each client.
            _session?.EndForHoleChange();
        }

        public override void ClientHoleCleanup()
        {
            LocalSessionActive = false;
        }

        /// <summary>
        /// Harrier steps for <see cref="AutonomousVehicleSession"/>. One instance per
        /// player bridge; each summon replaces <see cref="_craft"/> so a late hit on an
        /// old jet cannot mark the new run shot down.
        /// </summary>
        private sealed class HarrierLogic : IAutonomousVehicleLogic
        {
            private readonly HarrierNetworkBridge _bridge;
            private Craft _craft;

            public HarrierLogic(HarrierNetworkBridge bridge)
            {
                _bridge = bridge;
            }

            public string LogName => "Harrier";

            public GameObject Body => _craft == null ? null : _craft.Body;

            public bool IsLost =>
                _craft == null || _craft.Body == null || _craft.Behaviour == null;

            public bool HasArrived =>
                _craft != null && _craft.Behaviour != null && _craft.Behaviour.HasArrived;

            public bool IsNeutralized => _craft != null && _craft.IsShotDown;

            public bool HasDeparted =>
                _craft != null && _craft.Behaviour != null && _craft.Behaviour.IsComplete;

            public float ArrivalTimeout =>
                _craft.Plan.ApproachDistance / Mathf.Max(ModConfig.Harrier.ApproachSpeed.Value, 1f)
                + 8f;

            public float EngagementDuration => ModConfig.Harrier.Duration.Value;

            public float FireInterval => ModConfig.Harrier.FireInterval.Value;

            public float OpeningFireDelay => FireInterval * 0.5f;

            public float DepartureTimeout => 12f;

            public float NeutralizedTimeout => 20f;

            public bool IsNeutralizedComplete
            {
                get
                {
                    if (_craft == null || _craft.Body == null)
                        return true;

                    // The component Arm added, not a fresh lookup. A missing one means
                    // the crash cannot run, so the session should not wait out the timeout.
                    HarrierCrashBehaviour crash = _craft.Crash;
                    return crash == null || crash.IsComplete;
                }
            }

            public bool TrySpawn(PlayerInventory inventory)
            {
                var plan = PlanFlight();
                GameObject body = SpawnHarrierObject(plan.SpawnPosition, plan.HoverPosition);
                if (body == null)
                    return false;

                _craft = new Craft
                {
                    Inventory = inventory,
                    Plan = plan,
                    Body = body,
                };
                return true;
            }

            public void Arm(PlayerInventory inventory)
            {
                var craft = _craft;
                var harrierGo = craft.Body;

                var behaviour = harrierGo.AddComponent<HarrierBehaviour>();
                behaviour.HoverTarget = craft.Plan.HoverPosition;
                behaviour.FlyOutTarget = craft.Plan.FlyOutPosition;
                behaviour.ApproachSpeed = ModConfig.Harrier.ApproachSpeed.Value;
                behaviour.DriftSpeed = ModConfig.Harrier.DriftSpeed.Value;
                behaviour.HoverRadius = ModConfig.Harrier.HoverRadius.Value;
                craft.Behaviour = behaviour;

                var hitReceiver = harrierGo.AddComponent<HarrierHitReceiver>();
                var harrierIdentity = harrierGo.GetComponent<NetworkIdentity>();
                ItemWarningBroadcaster.Broadcast(
                    inventory.PlayerInfo.PlayerId.PlayerName,
                    ItemRegistry.HarrierItemType,
                    "Harrier Jet",
                    trackedNetId: harrierIdentity.netId,
                    senderNetId: _bridge.netId
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
                    if (craft.IsShotDown || craft.Body == null)
                        return;

                    craft.IsShotDown = true;
                    IssaPluginPlugin.Log.LogInfo("[Harrier] Shot down — initiating crash.");

                    NetworkServer.SendToAll(
                        new HarrierShotDownMessage { HarrierNetId = harrierIdentity.netId }
                    );

                    var crash = harrierGo.AddComponent<HarrierCrashBehaviour>();
                    craft.Crash = crash;
                    crash.ThrowerInventory = craft.Inventory;
                    crash.KillingRocketDir = (
                        harrierGo.transform.position - hitReceiver.LastHitWorldPos
                    ).normalized;
                    crash.ExplosionScale = ModConfig.Harrier.CrashExplosionScale.Value;
                };
            }

            public void Announce()
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[Harrier] Jet spawned at {_craft.Plan.SpawnPosition:F0}, heading to {_craft.Plan.HoverPosition:F0}."
                );

                NetworkServer.SendToAll(
                    new HarrierBeginClientMessage { HoverCenter = _craft.Plan.MapCenter }
                );
            }

            public void BeginDeparture()
            {
                _craft.Behaviour.BeginFlyOut();
                IssaPluginPlugin.Log.LogInfo("[Harrier] Beginning fly-out.");
            }

            public void BeginEngagement()
            {
                _craft.FriendlyFire = ModConfig.Harrier.FriendlyFire.Value;
            }

            public void Fire(PlayerInventory inventory)
            {
                if (_craft == null || _craft.Body == null)
                    return;

                HarrierItem.FireAtRandomTarget(
                    inventory,
                    _craft.Body.transform.position,
                    _craft.FriendlyFire
                );
            }

            public void NotifyClientsSessionEnded()
            {
                NetworkServer.SendToAll(new HarrierEndClientMessage());
            }

            public void OnSessionFinished()
            {
                _craft = null;
            }

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
                Vector3 flyOutPos =
                    hoverPos + approachDir * (approachDist * flyOutDistanceMultiplier);

                // Raise the whole path if any terrain along it out-tops the hover altitude,
                // then re-derive the endpoints from the corrected hover height.
                hoverPos.y = TerrainHeight.ClearPath(spawnPos, hoverPos, flyOutPos, altitude);
                spawnPos = hoverPos + approachDir * approachDist;
                flyOutPos = hoverPos + approachDir * (approachDist * flyOutDistanceMultiplier);

                return new FlightPlan(mapCenter, hoverPos, spawnPos, flyOutPos, approachDist);
            }

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

            private sealed class Craft
            {
                public PlayerInventory Inventory;
                public FlightPlan Plan;
                public GameObject Body;
                public HarrierBehaviour Behaviour;
                public bool IsShotDown;
                public bool FriendlyFire;
                public HarrierCrashBehaviour Crash;
            }
        }
    }
}
