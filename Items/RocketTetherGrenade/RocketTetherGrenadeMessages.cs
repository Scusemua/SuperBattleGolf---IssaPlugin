using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────────

    /// <summary>
    /// Sent when the local player uses the Rocket Tether Grenade.
    /// The server validates, spawns the grenade, and configures its Rigidbody.
    /// Landing detection is server-side (OnCollisionEnter on the behaviour).
    /// </summary>
    public struct RocketTetherGrenadeThrowMessage : NetworkMessage
    {
        /// World-space position from which the grenade is launched.
        public Vector3 ThrowOrigin;

        /// Initial world-space velocity of the grenade.
        public Vector3 ThrowVelocity;
    }

    public static class RocketTetherGrenadeThrowMessageSerialization
    {
        public static void WriteRocketTetherGrenadeThrowMessage(
            NetworkWriter writer,
            RocketTetherGrenadeThrowMessage msg
        )
        {
            writer.WriteVector3(msg.ThrowOrigin);
            writer.WriteVector3(msg.ThrowVelocity);
        }

        public static RocketTetherGrenadeThrowMessage ReadRocketTetherGrenadeThrowMessage(
            NetworkReader reader
        ) =>
            new RocketTetherGrenadeThrowMessage
            {
                ThrowOrigin   = reader.ReadVector3(),
                ThrowVelocity = reader.ReadVector3(),
            };
    }
}
