using System.Collections.Generic;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server steps for an AC130 that flies itself and shoots players.
    /// The piloted gunship stays on <see cref="AC130NetworkBridge"/> and is not started here.
    /// This class does not open a cockpit, and it does not call the piloted teardown.
    /// </summary>
    internal sealed class AC130AutonomousLogic : IAutonomousVehicleLogic
    {
        /// <summary>
        /// Gap between a regular rocket and a heavy rocket fired on the same tick.
        /// Applied perpendicular to the shot. The gunship's right points at the map
        /// centre while it orbits, which is the direction it shoots, so an offset
        /// along that axis pulls the heavy rocket back into the fuselage.
        /// </summary>
        private const float HeavyMuzzleSeparation = 4f;

        private readonly AC130NetworkBridge _bridge;
        private Craft _craft;

        public AC130AutonomousLogic(AC130NetworkBridge bridge)
        {
            _bridge = bridge;
        }

        public string LogName => "AC130 Autonomous";

        public GameObject Body => _craft != null ? _craft.Body : null;

        public bool IsLost => _craft == null || _craft.Body == null || _craft.Fly == null;

        public bool HasArrived => _craft != null && _craft.Fly != null && _craft.Fly.HasArrived;

        public bool IsNeutralized => _craft != null && _craft.IsShotDown;

        /// <summary>
        /// Fly-out destroys the gunship itself. <see cref="IsLost"/> ends that wait.
        /// </summary>
        public bool HasDeparted => false;

        public float ArrivalTimeout => ApproachDistance / ApproachSpeed + 8f;

        public float EngagementDuration => ModConfig.AC130Autonomous.Duration.Value;

        /// <summary>
        /// How often the attack checks the weapon clocks. Those clocks are the
        /// configured cooldowns. Checking only as often as the faster gun made
        /// the slower gun wait until the next check.
        /// </summary>
        private const float FirePollInterval = 0.05f;

        public float FireInterval => FirePollInterval;

        public float OpeningFireDelay => Mathf.Min(RegularCooldown, HeavyCooldown) * 0.5f;

        public float DepartureTimeout => FlyOutTime + 10f;

        public float NeutralizedTimeout =>
            Mathf.Max(1f, ModConfig.AC130Autonomous.CrashTimeout.Value);

        public bool IsNeutralizedComplete
        {
            get
            {
                if (_craft == null || _craft.Body == null || _craft.Mayday == null)
                    return true;

                return _craft.CrashComplete;
            }
        }

        public bool TrySpawn(PlayerInventory inventory)
        {
            var autonomous = ModConfig.AC130Autonomous;
            float orbitRadius = autonomous.OrbitRadius.Value;
            Vector3 orbitCenter = AC130NetworkBridge.ComputeRaisedOrbitCenter(
                inventory.PlayerInfo.transform.position,
                orbitRadius
            );
            GameObject gunship = AC130NetworkBridge.SpawnGunship(
                orbitCenter,
                autonomous.Altitude.Value,
                orbitRadius,
                autonomous.OrbitSpeed.Value,
                ApproachDistance,
                ApproachSpeed,
                Mathf.RoundToInt(autonomous.HitsToMayday.Value)
            );
            if (gunship == null)
                return false;

            _craft = new Craft
            {
                Inventory = inventory,
                Body = gunship,
                Fly = gunship.GetComponent<AC130FlyBehaviour>(),
                OrbitCenter = orbitCenter,
                SummonerId = inventory.PlayerInfo.PlayerId.Guid,
            };
            return true;
        }

        public void Arm(PlayerInventory inventory)
        {
            Craft craft = _craft;
            if (craft == null || craft.Body == null)
                return;

            var hitReceiver = craft.Body.GetComponent<AC130HitReceiver>();
            if (hitReceiver == null)
                return;

            var identity = craft.Body.GetComponent<NetworkIdentity>();
            hitReceiver.OnHit += () =>
            {
                if (craft.Closed || craft.Body == null || identity == null)
                    return;

                if (
                    hitReceiver.HitsRequired > 0
                    && hitReceiver.HitCount > 0
                    && hitReceiver.HitCount < hitReceiver.HitsRequired
                )
                {
                    NetworkServer.SendToAll(
                        new AC130DamagedMessage { GunshipNetId = identity.netId }
                    );
                }
            };

            hitReceiver.OnHitsExceeded = () => BeginCrash(craft, identity);
        }

        public void Announce()
        {
            if (_craft == null || _craft.Body == null || _craft.Inventory == null)
                return;

            _bridge.ServerBroadcastShooterState(
                true,
                ModConfig.AC130Autonomous.RocketSpeedMultiplier.Value
            );
            NetworkServer.SendToAll(new AC130SoundMessage());

            var identity = _craft.Body.GetComponent<NetworkIdentity>();
            ItemWarningBroadcaster.Broadcast(
                _craft.Inventory.PlayerInfo.PlayerId.PlayerName,
                ItemRegistry.AC130ItemType,
                "AC-130 Gunship",
                trackedNetId: identity != null ? identity.netId : 0u,
                senderNetId: _bridge.netId
            );

            IssaPluginPlugin.Log.LogInfo($"[{LogName}] Gunship deployed.");
        }

        public void BeginDeparture()
        {
            if (_craft == null)
                return;

            // A hit after this stays on the fly-out. The session does not switch to a crash.
            _craft.Departing = true;
            if (_craft.Fly == null)
                return;

            float flyOutTime = FlyOutTime;
            _craft.Fly.BeginFlyOut(ApproachSpeed, ApproachSpeed * flyOutTime);
            IssaPluginPlugin.Log.LogInfo($"[{LogName}] Beginning fly-out.");
        }

        public void BeginEngagement()
        {
            if (_craft == null)
                return;

            var autonomous = ModConfig.AC130Autonomous;
            // Copied once for this attack. The session only uses FireInterval as
            // how often it asks to shoot; these clocks decide when a gun is ready.
            _craft.RegularCooldown = RegularCooldown;
            _craft.HeavyCooldown = HeavyCooldown;
            _craft.TargetSelf = autonomous.TargetsUser.Value;
            _craft.HeavyMagazine = Mathf.Max(
                1,
                Mathf.RoundToInt(autonomous.HeavyShotsBeforeReload.Value)
            );
            _craft.HeavyShotsLeft = _craft.HeavyMagazine;
            _craft.HeavyReloading = false;
            _craft.NextRegularTime = Time.time;
            _craft.NextHeavyTime = Time.time;

            if (CollectTargets().Count == 0)
            {
                string hint = _craft.TargetSelf
                    ? "There are no players in the match."
                    : "Enable AC130 Autonomous → TargetsUser to let it shoot the player who called it.";
                IssaPluginPlugin.Log.LogInfo($"[{LogName}] No players to shoot. {hint}");
            }
        }

        public void Fire(PlayerInventory inventory)
        {
            if (_craft == null || _craft.Body == null || inventory == null)
                return;

            bool regularReady = Time.time >= _craft.NextRegularTime;
            bool heavyReady = TryReadyHeavy();
            if (!regularReady && !heavyReady)
                return;

            List<PlayerInventory> targets = CollectTargets();
            if (targets.Count == 0)
                return;

            if (regularReady && TryLaunch(inventory, targets, heavy: false))
                _craft.NextRegularTime = Time.time + _craft.RegularCooldown;

            if (!heavyReady || !TryLaunch(inventory, targets, heavy: true))
                return;

            _craft.NextHeavyTime = Time.time + _craft.HeavyCooldown;
            _craft.HeavyShotsLeft--;
            if (_craft.HeavyShotsLeft > 0)
                return;

            _craft.HeavyReloading = true;
            _craft.HeavyReloadReadyTime = Time.time + ModConfig.AC130Autonomous.HeavyReloadTime.Value;
            IssaPluginPlugin.Log.LogInfo($"[{LogName}] Heavy rocket reloading.");
        }

        public void NotifyClientsSessionEnded()
        {
            // Nobody entered a cockpit, so there is no client session to close.
            // Shooter state and the global lock are released from OnSessionFinished,
            // including when a hole change skips this method.
        }

        public void OnSessionFinished()
        {
            if (_craft != null)
                _craft.Closed = true;

            _craft = null;
            _bridge.EndAutonomousSideState();
        }

        private void BeginCrash(Craft craft, NetworkIdentity identity)
        {
            if (craft.Closed || craft.Departing || craft.IsShotDown || craft.Body == null)
                return;

            // Mark this before disabling the flight driver. If the driver is gone first,
            // the session treats the gunship as lost and destroys it instead of waiting.
            craft.IsShotDown = true;

            if (craft.Fly != null)
            {
                craft.Fly.OnExternallyDestroyed = null;
                craft.Fly.enabled = false;
            }

            var mayday = craft.Body.AddComponent<AC130MaydayBehaviour>();
            craft.Mayday = mayday;
            mayday.IsLocalPlayer = false;
            mayday.OrbitCenter = craft.OrbitCenter;
            mayday.OnImpact = () =>
            {
                if (craft.Closed || craft.CrashComplete || craft.Body == null)
                    return;

                craft.CrashComplete = true;
                _bridge.ServerAutonomousImpact(craft.Body.transform.position);
            };

            if (identity != null)
            {
                NetworkServer.SendToAll(
                    new AC130MaydayVfxMessage { GunshipNetId = identity.netId }
                );
            }

            IssaPluginPlugin.Log.LogInfo($"[{LogName}] Shot down — beginning crash.");
        }

        /// <summary>
        /// Speed written onto the gunship and used for both flight timeouts.
        /// Zero or negative never reaches the orbit, so the arrival wait would not end.
        /// </summary>
        private static float ApproachSpeed
        {
            get
            {
                float speed = ModConfig.AC130Autonomous.ApproachSpeed.Value;
                return speed > 0f ? speed : 1f;
            }
        }

        private static float ApproachDistance =>
            Mathf.Max(0f, ModConfig.AC130Autonomous.ApproachDistance.Value);

        private static float FlyOutTime =>
            Mathf.Max(0f, ModConfig.AC130Autonomous.FlyOutTime.Value);

        private static float RegularCooldown =>
            Mathf.Max(0.05f, ModConfig.AC130Autonomous.FireCooldown.Value);

        private static float HeavyCooldown =>
            Mathf.Max(0.05f, ModConfig.AC130Autonomous.HeavyFireCooldown.Value);

        /// <summary>
        /// True when a heavy rocket may fire. A finished reload refills the magazine first.
        /// </summary>
        private bool TryReadyHeavy()
        {
            if (_craft.HeavyReloading)
            {
                if (Time.time < _craft.HeavyReloadReadyTime)
                    return false;

                _craft.HeavyReloading = false;
                _craft.HeavyShotsLeft = _craft.HeavyMagazine;
            }

            return _craft.HeavyShotsLeft > 0 && Time.time >= _craft.NextHeavyTime;
        }

        private bool TryLaunch(PlayerInventory inventory, List<PlayerInventory> targets, bool heavy)
        {
            if (_craft == null || _craft.Body == null || targets.Count == 0)
                return false;

            PlayerInventory target = targets[Random.Range(0, targets.Count)];
            Vector3 origin = _craft.Body.transform.position;
            Vector3 aimPoint =
                target.transform.position
                + Vector3.up * ModConfig.AC130Autonomous.AimHeightOffset.Value;
            Vector3 aim = aimPoint - origin;
            if (aim.sqrMagnitude < 0.01f)
                return false;

            aim.Normalize();
            float jitterDeg = ModConfig.AC130Autonomous.RocketAngularJitter.Value;
            Quaternion jitter = Quaternion.Euler(
                Random.Range(-jitterDeg, jitterDeg),
                Random.Range(-jitterDeg, jitterDeg),
                0f
            );
            Vector3 muzzle = origin + aim * AC130NetworkBridge.RocketMuzzleOffset;
            if (heavy)
                muzzle += ShotLateral(aim) * HeavyMuzzleSeparation;

            float explosionScale = heavy
                ? ModConfig.AC130Autonomous.HeavyRocketExplosionScale.Value
                : ModConfig.AC130Autonomous.ExplosionScale.Value;

            AC130Item.SpawnRocketInDirection(
                inventory,
                muzzle,
                jitter * Quaternion.LookRotation(aim, Vector3.up),
                explosionScale
            );
            return true;
        }

        /// <summary>
        /// Unit vector perpendicular to <paramref name="aim"/>, kept level so a
        /// shot at the map centre is pushed sideways rather than back into the plane.
        /// </summary>
        private static Vector3 ShotLateral(Vector3 aim)
        {
            Vector3 lateral = Vector3.Cross(aim, Vector3.up);
            if (lateral.sqrMagnitude < 0.0001f)
                lateral = Vector3.Cross(aim, Vector3.forward);
            return lateral.normalized;
        }

        private List<PlayerInventory> CollectTargets()
        {
            var result = new List<PlayerInventory>();
            if (_craft == null)
                return result;

            foreach (PlayerInventory inv in Object.FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None))
            {
                if (inv == null || inv.PlayerInfo == null)
                    continue;

                if (!_craft.TargetSelf && inv.PlayerInfo.PlayerId.Guid == _craft.SummonerId)
                    continue;

                result.Add(inv);
            }

            return result;
        }

        private sealed class Craft
        {
            public PlayerInventory Inventory;
            public GameObject Body;
            public AC130FlyBehaviour Fly;
            public AC130MaydayBehaviour Mayday;
            public Vector3 OrbitCenter;
            public ulong SummonerId;
            public bool IsShotDown;
            public bool Departing;
            public bool CrashComplete;
            public bool Closed;
            public bool TargetSelf;
            public float RegularCooldown;
            public float HeavyCooldown;
            public int HeavyMagazine;
            public int HeavyShotsLeft;
            public bool HeavyReloading;
            public float NextRegularTime;
            public float NextHeavyTime;
            public float HeavyReloadReadyTime;
        }
    }
}
