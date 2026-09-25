using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Shooter → server. The server forwards the shove to the client that simulates the victim.
    /// </summary>
    public struct FirearmKnockbackRequestMessage : NetworkMessage
    {
        public uint VictimNetId;
        public Vector3 Direction;
        public float Force;
    }

    /// <summary>Server → the victim's client. Applied only on that machine.</summary>
    public struct FirearmKnockbackMessage : NetworkMessage
    {
        public Vector3 Direction;
        public float Force;
    }

    public static class FirearmKnockbackRequestMessageSerialization
    {
        public static void WriteFirearmKnockbackRequestMessage(
            NetworkWriter writer,
            FirearmKnockbackRequestMessage msg
        )
        {
            writer.WriteUInt(msg.VictimNetId);
            writer.WriteVector3(msg.Direction);
            writer.WriteFloat(msg.Force);
        }

        public static FirearmKnockbackRequestMessage ReadFirearmKnockbackRequestMessage(
            NetworkReader reader
        ) =>
            new FirearmKnockbackRequestMessage
            {
                VictimNetId = reader.ReadUInt(),
                Direction = reader.ReadVector3(),
                Force = reader.ReadFloat(),
            };
    }

    public static class FirearmKnockbackMessageSerialization
    {
        public static void WriteFirearmKnockbackMessage(NetworkWriter writer, FirearmKnockbackMessage msg)
        {
            writer.WriteVector3(msg.Direction);
            writer.WriteFloat(msg.Force);
        }

        public static FirearmKnockbackMessage ReadFirearmKnockbackMessage(NetworkReader reader) =>
            new FirearmKnockbackMessage
            {
                Direction = reader.ReadVector3(),
                Force = reader.ReadFloat(),
            };
    }

    public static class FirearmKnockback
    {
        /// <summary>Config values sit around 8–10. Reject anything a client inflates past this.</summary>
        private const float MaxForce = 40f;

        /// <summary>Extra reach so the check uses the players' feet, not the barrel.</summary>
        private const float RangeSlack = 8f;

        private static readonly Dictionary<int, float> LastAcceptTime = new Dictionary<int, float>();

        public static void Send(Hittable hittable, Vector3 direction, float force)
        {
            if (force <= 0f || hittable?.AsEntity == null || !hittable.AsEntity.IsPlayer)
                return;

            var identity = hittable.AsEntity.PlayerInfo?.GetComponent<NetworkIdentity>();
            if (identity == null)
                return;

            force = Mathf.Min(force, MaxForce);
            if (NetworkServer.active)
                Deliver(identity, direction, force);
            else if (NetworkClient.active)
                NetworkClient.Send(
                    new FirearmKnockbackRequestMessage
                    {
                        VictimNetId = identity.netId,
                        Direction = direction,
                        Force = force,
                    }
                );
        }

        public static void ServerHandleRequest(
            NetworkConnectionToClient conn,
            FirearmKnockbackRequestMessage msg
        )
        {
            if (conn?.identity == null)
                return;
            if (msg.Force <= 0f || msg.Force > MaxForce)
                return;
            if (!NetworkServer.spawned.TryGetValue(msg.VictimNetId, out var victim) || victim == null)
                return;
            if (victim.GetComponent<PlayerInfo>() == null)
                return;

            float minInterval = Mathf.Max(0.05f, ModConfig.AA12.FireRate.Value * 0.75f);
            float now = Time.time;
            if (
                LastAcceptTime.TryGetValue(conn.connectionId, out float last)
                && now - last < minInterval
            )
                return;

            float maxRange =
                Mathf.Max(
                    ModConfig.AA12.MaxShotDistance.Value,
                    ModConfig.Remington870.MaxShotDistance.Value
                ) + RangeSlack;
            float dist = Vector3.Distance(conn.identity.transform.position, victim.transform.position);
            if (dist > maxRange)
                return;

            LastAcceptTime[conn.connectionId] = now;
            Deliver(victim, msg.Direction, msg.Force);
        }

        public static void ClientHandle(FirearmKnockbackMessage msg) =>
            ApplyLocal(msg.Direction, msg.Force);

        private static void Deliver(NetworkIdentity victim, Vector3 direction, float force)
        {
            if (victim.isLocalPlayer)
                ApplyLocal(direction, force);
            else
                victim.connectionToClient?.Send(
                    new FirearmKnockbackMessage { Direction = direction, Force = force }
                );
        }

        private static void ApplyLocal(Vector3 direction, float force)
        {
            if (force <= 0f || force > MaxForce)
                return;

            var rb = GameManager.LocalPlayerInfo?.Rigidbody;
            if (rb == null)
                return;

            direction.y = Mathf.Max(direction.y, 0.15f);
            if (direction.sqrMagnitude < 0.0001f)
                return;

            Vector3 impulse = direction.normalized * force;

            // The elephant-gun hit often locks the root body. Ragdoll bones stay dynamic.
            if (rb.isKinematic)
            {
                var bones = rb.GetComponentsInChildren<Rigidbody>();
                bool applied = false;
                for (int i = 0; i < bones.Length; i++)
                {
                    var bone = bones[i];
                    if (bone == null || bone == rb || bone.isKinematic)
                        continue;
                    bone.AddForce(impulse, ForceMode.VelocityChange);
                    applied = true;
                }
                if (applied)
                    return;

                rb.isKinematic = false;
                rb.useGravity = true;
            }

            rb.AddForce(impulse, ForceMode.VelocityChange);
        }
    }
}
