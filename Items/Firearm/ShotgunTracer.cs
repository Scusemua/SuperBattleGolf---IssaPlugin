using System.Collections.Generic;
using IssaPlugin.Patches;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// One shotgun shell's pellet trails, plus the blood points and the bear ray
    /// that used to come from the elephant-gun bullet effects.
    /// </summary>
    public struct ShotgunTracerMessage : NetworkMessage
    {
        public Vector3 Origin;
        public Vector3[] Ends;
        public Vector3[] BloodPoints;
        public Vector3[] Impacts;
        public bool BearRay;
        public Vector3 BearDirection;
        public float BearRange;
    }

    public static class ShotgunTracerMessageSerialization
    {
        public static void WriteShotgunTracerMessage(NetworkWriter writer, ShotgunTracerMessage msg)
        {
            writer.WriteVector3(msg.Origin);
            WritePoints(writer, msg.Ends);
            WritePoints(writer, msg.BloodPoints);
            WritePoints(writer, msg.Impacts);
            writer.WriteBool(msg.BearRay);
            writer.WriteVector3(msg.BearDirection);
            writer.WriteFloat(msg.BearRange);
        }

        public static ShotgunTracerMessage ReadShotgunTracerMessage(NetworkReader reader) =>
            new ShotgunTracerMessage
            {
                Origin = reader.ReadVector3(),
                Ends = ReadPoints(reader),
                BloodPoints = ReadPoints(reader),
                Impacts = ReadPoints(reader),
                BearRay = reader.ReadBool(),
                BearDirection = reader.ReadVector3(),
                BearRange = reader.ReadFloat(),
            };

        private static void WritePoints(NetworkWriter writer, Vector3[] points)
        {
            int count = points == null ? 0 : Mathf.Min(points.Length, ShotgunTracer.MaxPellets);
            writer.WriteByte((byte)count);
            for (int i = 0; i < count; i++)
                writer.WriteVector3(points[i]);
        }

        private static Vector3[] ReadPoints(NetworkReader reader)
        {
            int count = Mathf.Min(reader.ReadByte(), ShotgunTracer.MaxPellets);
            var points = new Vector3[count];
            for (int i = 0; i < count; i++)
                points[i] = reader.ReadVector3();
            return points;
        }
    }

    public static class ShotgunTracer
    {
        internal const int MaxPellets = 32;
        private const float Speed = 90f;

        public static void Play(
            Vector3 origin,
            List<Vector3> ends,
            List<Vector3> bloodPoints,
            List<Vector3> impacts,
            bool bearRay,
            Vector3 bearDirection,
            float bearRange
        )
        {
            var msg = new ShotgunTracerMessage
            {
                Origin = origin,
                Ends = Copy(ends),
                BloodPoints = Copy(bloodPoints),
                Impacts = Copy(impacts),
                BearRay = bearRay,
                BearDirection = bearDirection,
                BearRange = bearRange,
            };
            if (
                msg.Ends.Length == 0
                && msg.BloodPoints.Length == 0
                && msg.Impacts.Length == 0
                && !msg.BearRay
            )
                return;

            Spawn(msg);

            if (msg.BearRay && NetworkServer.active)
                HitBear(GameManager.LocalPlayerInfo, msg);

            if (NetworkServer.active)
            {
                // The host does not receive its own SendToAll. Remotes do.
                NetworkServer.SendToAll(msg);
            }
            else if (NetworkClient.active)
            {
                NetworkClient.Send(msg);
            }
        }

        public static void ServerHandle(NetworkConnectionToClient sender, ShotgunTracerMessage msg)
        {
            if (sender != NetworkServer.localConnection)
            {
                Spawn(msg);
                if (msg.BearRay)
                    HitBear(sender?.identity?.GetComponent<PlayerInfo>(), msg);
            }

            foreach (var conn in NetworkServer.connections.Values)
            {
                if (conn == null || conn == sender || conn == NetworkServer.localConnection)
                    continue;
                conn.Send(msg);
            }
        }

        public static void ClientHandle(ShotgunTracerMessage msg) => Spawn(msg);

        private static Vector3[] Copy(List<Vector3> points)
        {
            if (points == null || points.Count == 0)
                return System.Array.Empty<Vector3>();

            int count = Mathf.Min(points.Count, MaxPellets);
            var copy = new Vector3[count];
            for (int i = 0; i < count; i++)
                copy[i] = points[i];
            return copy;
        }

        private static void HitBear(PlayerInfo attacker, ShotgunTracerMessage msg)
        {
            if (attacker == null || msg.BearDirection.sqrMagnitude < 0.0001f)
                return;

            float range = Mathf.Min(BearWeaponHitHelper.MaxGunRange, Mathf.Max(0f, msg.BearRange));
            BearWeaponHitHelper.TryHitBearAlongRay(
                attacker,
                msg.Origin,
                msg.BearDirection,
                ModConfig.Bear.DamageElephantGun.Value,
                range
            );
        }

        private static void Spawn(ShotgunTracerMessage msg)
        {
            if (AssetLoader.ShotgunBulletPrefab != null && msg.Ends != null)
            {
                int count = Mathf.Min(msg.Ends.Length, MaxPellets);
                for (int i = 0; i < count; i++)
                {
                    Vector3 end = msg.Ends[i];
                    Vector3 direction = end - msg.Origin;
                    if (direction.sqrMagnitude < 0.0001f || !IsFinite(msg.Origin) || !IsFinite(end))
                        continue;

                    BulletStream.Launch(msg.Origin, end);
                }
            }

            if (msg.Ends != null && msg.Ends.Length > 0)
                SpawnMuzzleFlash(msg);

            SpawnImpacts(msg);

            if (msg.BloodPoints == null)
                return;

            int bloodCount = Mathf.Min(msg.BloodPoints.Length, MaxPellets);
            for (int i = 0; i < bloodCount; i++)
            {
                if (!IsFinite(msg.BloodPoints[i]))
                    continue;
                BloodSplatterHelper.SpawnBloodSplatter(msg.BloodPoints[i], msg.Origin);
            }
        }

        private static void SpawnMuzzleFlash(ShotgunTracerMessage msg)
        {
            var prefab = AssetLoader.ShotgunMuzzleFlashPrefab;
            if (prefab == null || !IsFinite(msg.Origin))
                return;

            Vector3 forward = msg.BearDirection;
            if (forward.sqrMagnitude < 0.0001f && msg.Ends != null && msg.Ends.Length > 0)
                forward = msg.Ends[0] - msg.Origin;
            if (forward.sqrMagnitude < 0.0001f || !IsFinite(forward))
                return;

            forward.Normalize();

            // The loaded prefab stays alive and play-on-awake spends its one-particle
            // burst at the origin. Instances copied from that spent system emit nothing.
            if (prefab.activeSelf)
                prefab.SetActive(false);

            var go = Object.Instantiate(
                prefab,
                msg.Origin + forward * 0.2f,
                Quaternion.LookRotation(forward)
            );
            go.SetActive(true);
            foreach (var particles in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.Clear(true);
                particles.Play(true);
            }

            Object.Destroy(go, 2.1f);
        }

        private static void SpawnImpacts(ShotgunTracerMessage msg)
        {
            if (!ModConfig.Global.BulletImpactEnabled.Value)
                return;

            var prefab = AssetLoader.BulletImpactPrefab;
            if (prefab == null || msg.Impacts == null)
                return;

            if (prefab.activeSelf)
                prefab.SetActive(false);

            if (!ImpactStream.Ensure(prefab))
                return;

            int count = Mathf.Min(msg.Impacts.Length, MaxPellets);
            for (int i = 0; i < count; i++)
            {
                Vector3 point = msg.Impacts[i];
                if (!IsFinite(point))
                    continue;

                Vector3 forward = point - msg.Origin;
                if (forward.sqrMagnitude < 0.0001f || !IsFinite(forward))
                {
                    forward =
                        msg.BearDirection.sqrMagnitude > 0.0001f
                            ? msg.BearDirection
                            : Vector3.forward;
                }
                forward.Normalize();
                ImpactStream.Emit(point, Quaternion.LookRotation(forward));
            }
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        /// <summary>
        /// Manual world-space emitter. Bursts and rates are cleared so only
        /// explicit Emit calls spawn particles. Emitter velocity is zeroed
        /// because the impact bank is moved to each hit, and that jump would
        /// otherwise become the speed of the new particles.
        /// </summary>
        static void PrepareEmitter(ParticleSystem particles, int maxParticles)
        {
            var main = particles.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.prewarm = false;
            main.playOnAwake = false;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.ringBufferMode = ParticleSystemRingBufferMode.Disabled;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom;
            main.emitterVelocity = Vector3.zero;

            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());

            var inherit = particles.inheritVelocity;
            inherit.enabled = false;

            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.Clear(false);
            particles.Play(false);
        }

    /// <summary>
    /// One shared copy of bullet_impact. Each hit emits that prefab's bursts
    /// (sparks, the main puff, glow, dust) into those systems at the hit point.
    /// </summary>
    static class ImpactStream
    {
        static ParticleSystem[] _systems;
        static int[] _burstCounts;
        static Transform _bank;
        static bool _ready;
        static bool _failed;

        public static bool Ensure(GameObject prefab)
        {
            if (_ready)
                return true;
            if (_failed || prefab == null)
                return false;

            var bank = Object.Instantiate(prefab);
            bank.name = "ImpactStream";
            bank.SetActive(true);
            Object.DontDestroyOnLoad(bank);
            foreach (var col in bank.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            var systems = bank.GetComponentsInChildren<ParticleSystem>(true);
            if (systems.Length == 0)
            {
                _failed = true;
                Object.Destroy(bank);
                return false;
            }

            _systems = systems;
            _burstCounts = new int[systems.Length];
            for (int i = 0; i < systems.Length; i++)
            {
                var emission = systems[i].emission;
                int burst = 0;
                if (emission.burstCount > 0)
                    burst = Mathf.Max(0, Mathf.RoundToInt(emission.GetBurst(0).count.constant));
                _burstCounts[i] = burst;

                float life = systems[i].main.startLifetime.constantMax;
                int max = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(burst, 1) * Mathf.Max(life, 0.1f) * 1600f), 1024, 131072);
                PrepareEmitter(systems[i], max);
            }

            _bank = bank.transform;
            _ready = true;
            return true;
        }

        public static void Emit(Vector3 point, Quaternion rotation)
        {
            _bank.SetPositionAndRotation(point, rotation);
            for (int i = 0; i < _systems.Length; i++)
            {
                if (_burstCounts[i] <= 0)
                    continue;

                // Emit(count) reads a cached emitter pose, so a bank that just
                // moved drops the burst at the previous hit. Pass the position.
                var emit = new ParticleSystem.EmitParams
                {
                    position = _systems[i].transform.position,
                    applyShapeToPosition = true,
                };
                _systems[i].Emit(emit, _burstCounts[i]);
            }
        }
    }

    /// <summary>
    /// One shared copy of shotgun_bullet. Every pellet is a point moved from here.
    /// Glow and SparkTrail emit along that point at the prefab's own rate-over-distance,
    /// into world space, so every pellet still produces its particles and Unity draws
    /// each system once.
    /// </summary>
    static class BulletStream
    {
        struct Flight
        {
            public Vector3 Position;
            public Vector3 End;
            public float GlowDebt;
            public float SparkDebt;
        }

        static readonly List<Flight> Flights = new List<Flight>(256);
        static ParticleSystem _glow;
        static ParticleSystem _spark;
        static ParticleSystem _head;
        static float _glowPerMeter;
        static float _sparkPerMeter;
        static float _sparkInherit;
        static bool _ready;
        static bool _failed;

        public static void Launch(Vector3 origin, Vector3 end)
        {
            if (_failed || (!_ready && !Create()))
                return;

            Vector3 delta = end - origin;
            float distance = delta.magnitude;
            float life = distance / Speed;
            if (life <= 0f)
                return;

            var head = new ParticleSystem.EmitParams
            {
                position = origin,
                velocity = distance > 0.0001f ? delta / distance * Speed : Vector3.zero,
                applyShapeToPosition = false,
                startLifetime = life,
            };
            _head.Emit(head, 1);

            Flights.Add(
                new Flight
                {
                    Position = origin,
                    End = end,
                }
            );
        }

        public static void Tick(float dt)
        {
            if (!_ready || dt <= 0f)
                return;

            float step = Speed * dt;
            for (int i = Flights.Count - 1; i >= 0; i--)
            {
                Flight flight = Flights[i];
                Vector3 next = Vector3.MoveTowards(flight.Position, flight.End, step);
                float distance = Vector3.Distance(flight.Position, next);
                if (distance > 0f)
                {
                    Vector3 sparkVelocity = Vector3.zero;
                    Vector3 travel = flight.End - flight.Position;
                    if (_sparkInherit > 0f && travel.sqrMagnitude > 0.0001f)
                        sparkVelocity = travel.normalized * (Speed * _sparkInherit);

                    EmitAlong(_glow, flight.Position, next, ref flight.GlowDebt, _glowPerMeter, Vector3.zero);
                    EmitAlong(_spark, flight.Position, next, ref flight.SparkDebt, _sparkPerMeter, sparkVelocity);
                    flight.Position = next;
                }

                if ((next - flight.End).sqrMagnitude <= 0.0001f)
                    Flights.RemoveAt(i);
                else
                    Flights[i] = flight;
            }
        }

        static void EmitAlong(
            ParticleSystem particles,
            Vector3 from,
            Vector3 to,
            ref float debt,
            float perMeter,
            Vector3 velocity
        )
        {
            if (particles == null || perMeter <= 0f)
                return;

            debt += Vector3.Distance(from, to) * perMeter;
            int count = (int)debt;
            if (count <= 0)
                return;

            debt -= count;
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            if (velocity.sqrMagnitude > 0.0001f)
                emit.velocity = velocity;
            float span = count;
            for (int i = 1; i <= count; i++)
            {
                emit.position = Vector3.Lerp(from, to, i / span);
                particles.Emit(emit, 1);
            }
        }

        static bool Create()
        {
            var prefab = AssetLoader.ShotgunBulletPrefab;
            if (prefab == null)
                return false;

            // Same reason as the muzzle flash: the loaded prefab is a live object,
            // and play-on-awake would leave one mesh particle sitting on it.
            if (prefab.activeSelf)
                prefab.SetActive(false);

            var bank = Object.Instantiate(prefab);
            bank.name = "BulletStream";
            bank.SetActive(true);
            Object.DontDestroyOnLoad(bank);
            bank.AddComponent<Ticker>();

            _head = bank.GetComponent<ParticleSystem>();
            _glow = bank.transform.Find("Glow")?.GetComponent<ParticleSystem>();
            _spark = bank.transform.Find("SparkTrail")?.GetComponent<ParticleSystem>();
            if (_head == null || _glow == null || _spark == null)
            {
                _failed = true;
                Object.Destroy(bank);
                return false;
            }

            _glowPerMeter = _glow.emission.rateOverDistance.constant;
            _sparkPerMeter = _spark.emission.rateOverDistance.constant;
            // SparkTrail is a stretched billboard. It used to inherit the moving
            // bullet's speed so the streak pointed along the shot. The bank does
            // not move, so apply that fraction as the particle's own velocity.
            var sparkInherit = _spark.inheritVelocity;
            _sparkInherit = sparkInherit.enabled ? sparkInherit.curve.Evaluate(0f, 1f) : 0f;

            PrepareEmitter(_glow, 8192);
            PrepareEmitter(_spark, 16384);
            PrepareEmitter(_head, 16384);
            // The prefab clamps speed toward zero. The mesh used to move because it was
            // parented to the bullet object. It has to carry its own velocity now.
            var limit = _head.limitVelocityOverLifetime;
            limit.enabled = false;
            var headRenderer = _head.GetComponent<ParticleSystemRenderer>();
            if (headRenderer != null)
            {
                headRenderer.enableGPUInstancing = true;
                // The slug is long on its local forward axis, and the prefab aligns
                // that axis to this transform. The shared bank never turns, so looking
                // left or right lays the length across the screen. Follow the shot.
                headRenderer.alignment = ParticleSystemRenderSpace.Velocity;
                // Those scales turn the 90-unit shot speed into a ribbon.
                headRenderer.velocityScale = 0f;
                headRenderer.lengthScale = 1f;
            }

            _ready = true;
            return true;
        }

        sealed class Ticker : MonoBehaviour
        {
            void Update() => Tick(Time.deltaTime);
        }
    }
}
}
