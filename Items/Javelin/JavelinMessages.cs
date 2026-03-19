using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // ── Client → Server ──────────────────────────────────────────────────

    /// Sent by the owning client when the player presses the use button.
    /// Carries the world-space position that was locked onto so the server
    /// can drive the entire flight server-side without further client input.
    public struct JavelinFireMessage : NetworkMessage
    {
        public Vector3 TargetPosition;
    }

    public static class JavelinFireMessageSerialization
    {
        public static void WriteJavelinFireMessage(NetworkWriter writer, JavelinFireMessage msg)
        {
            writer.WriteVector3(msg.TargetPosition);
        }

        public static JavelinFireMessage ReadJavelinFireMessage(NetworkReader reader)
        {
            return new JavelinFireMessage { TargetPosition = reader.ReadVector3() };
        }
    }

    // ── Server → All Clients ─────────────────────────────────────────────

    /// Broadcast to every client when the Javelin rocket detonates so each
    /// client can spawn the explosion VFX locally.
    public struct JavelinExplosionMessage : NetworkMessage
    {
        public Vector3 Position;
    }

    public static class JavelinExplosionMessageSerialization
    {
        public static void WriteJavelinExplosionMessage(
            NetworkWriter w,
            JavelinExplosionMessage m
        ) => w.WriteVector3(m.Position);

        public static JavelinExplosionMessage ReadJavelinExplosionMessage(NetworkReader r) =>
            new JavelinExplosionMessage { Position = r.ReadVector3() };
    }

    // ── Server → Client ──────────────────────────────────────────────────

    /// Sent to the firing client once the server has spawned and launched
    /// the rocket, so the client can show a brief "launched" notification.
    public struct JavelinLaunchedMessage : NetworkMessage { }

    public static class JavelinLaunchedMessageSerialization
    {
        public static void WriteJavelinLaunchedMessage(
            NetworkWriter w,
            JavelinLaunchedMessage m
        ) { }

        public static JavelinLaunchedMessage ReadJavelinLaunchedMessage(NetworkReader r)
        {
            return new JavelinLaunchedMessage();
        }
    }

    /// Sent to the firing client when the rocket has detonated (or timed
    /// out) so that client-side lock-on state can be cleared.
    public struct JavelinDetonatedMessage : NetworkMessage { }

    public static class JavelinDetonatedMessageSerialization
    {
        public static void WriteJavelinDetonatedMessage(
            NetworkWriter w,
            JavelinDetonatedMessage m
        ) { }

        public static JavelinDetonatedMessage ReadJavelinDetonatedMessage(NetworkReader r)
        {
            return new JavelinDetonatedMessage();
        }
    }
}
