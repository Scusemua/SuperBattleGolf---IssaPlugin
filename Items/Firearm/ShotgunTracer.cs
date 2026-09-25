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
                BearRay = bearRay,
                BearDirection = bearDirection,
                BearRange = bearRange,
            };
            if (msg.Ends.Length == 0 && msg.BloodPoints.Length == 0 && !msg.BearRay)
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
            GameObject prefab;

            if (ModConfig.AA12.BulletPrefab.Value < 2)
            {
                prefab = AssetLoader.ShotgunBulletPrefab;
                IssaPluginPlugin.Log.LogInfo($"[Shotgun] Using shotgun bullet prefab #1.");
            }
            else
            {
                prefab = AssetLoader.ShotgunBulletPrefab2;
                IssaPluginPlugin.Log.LogInfo($"[Shotgun] Using shotgun bullet prefab #2.");
            }

            if (prefab != null && msg.Ends != null)
            {
                int count = Mathf.Min(msg.Ends.Length, MaxPellets);
                for (int i = 0; i < count; i++)
                {
                    Vector3 end = msg.Ends[i];
                    Vector3 direction = end - msg.Origin;
                    if (direction.sqrMagnitude < 0.0001f || !IsFinite(msg.Origin) || !IsFinite(end))
                        continue;

                    var go = Object.Instantiate(
                        prefab,
                        msg.Origin,
                        Quaternion.LookRotation(direction)
                    );
                    go.SetActive(true);
                    foreach (var col in go.GetComponentsInChildren<Collider>())
                        col.enabled = false;

                    Vector3 velocity = direction.normalized * Speed;
                    foreach (var rb in go.GetComponentsInChildren<Rigidbody>())
                    {
                        rb.detectCollisions = false;
                        rb.useGravity = false;
                        rb.isKinematic = false;
                        rb.linearVelocity = velocity;
                    }

                    foreach (var particles in go.GetComponentsInChildren<ParticleSystem>())
                        particles.Play(true);

                    var bullet = go.AddComponent<ShotgunBulletVisual>();
                    bullet.Launch(end, Speed);
                }
            }

            if (msg.Ends != null && msg.Ends.Length > 0)
                SpawnMuzzleFlash(msg);

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

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }

    /// Flies one bullet so its particle trail has movement to render.
    public class ShotgunBulletVisual : MonoBehaviour
    {
        private Vector3 _end;
        private float _speed;
        private bool _done;

        public void Launch(Vector3 end, float speed)
        {
            _end = end;
            _speed = speed;
        }

        private void Update()
        {
            if (_done)
                return;

            Vector3 next = Vector3.MoveTowards(transform.position, _end, _speed * Time.deltaTime);
            Vector3 step = next - transform.position;
            transform.position = next;

            if (step.sqrMagnitude > 0.0000001f && Time.deltaTime > 0f)
            {
                Vector3 velocity = step / Time.deltaTime;
                foreach (var rb in GetComponentsInChildren<Rigidbody>())
                    rb.linearVelocity = velocity;
            }

            if ((next - _end).sqrMagnitude > 0.0001f)
                return;

            _done = true;
            foreach (var rb in GetComponentsInChildren<Rigidbody>())
                rb.linearVelocity = Vector3.zero;
            foreach (var particles in GetComponentsInChildren<ParticleSystem>())
                particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(gameObject, 0.5f);
        }
    }
}
