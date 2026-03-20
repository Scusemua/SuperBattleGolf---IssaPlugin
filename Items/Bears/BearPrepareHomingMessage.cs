using Mirror;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Sent client→server each frame while the local player has a bear locked on
    /// and is holding the rocket launcher ready to fire.
    ///
    /// The server handler calls BearNetworkBridge.ServerPrepareBearRocket(), which
    /// sets PendingBearHoming = true. BearRocketHomingPatch.Postfix then consumes
    /// that flag the moment the next Rocket.ServerInitialize fires, attaching a
    /// GunshipHomingBehaviour that steers the rocket toward the nearest bear.
    ///
    /// Mirrors BomberPrepareHomingMessage and AC130PrepareHomingMessage exactly.
    /// </summary>
    public struct BearPrepareHomingMessage : NetworkMessage { }

    public static class BearPrepareHomingMessageSerialization
    {
        public static void WriteBearPrepareHomingMessage(
            NetworkWriter writer, BearPrepareHomingMessage msg) { }

        public static BearPrepareHomingMessage ReadBearPrepareHomingMessage(
            NetworkReader reader) => new BearPrepareHomingMessage();
    }
}
