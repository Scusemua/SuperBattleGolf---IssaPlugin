using Mirror;

namespace IssaPlugin.Items
{
    /// <summary>Client → Server: local player ate a Super Jumbo Burger.</summary>
    public struct SuperJumboBurgerActivateMessage : NetworkMessage { }

    /// <summary>
    /// Server → all clients: play the eat VFX/audio on a specific player.
    ///
    /// The giant scale itself is NOT sent here — characterScale is a ClientToServer
    /// SyncVar on PlayerMovement that Mirror already replicates to every client. This
    /// message only covers the one-shot effects, which are not part of any SyncVar.
    /// </summary>
    public struct SuperJumboBurgerEffectsMessage : NetworkMessage
    {
        public uint PlayerNetId;
    }

    public static class SuperJumboBurgerMessageSerialization
    {
        public static void WriteActivate(NetworkWriter w, SuperJumboBurgerActivateMessage msg) { }

        public static SuperJumboBurgerActivateMessage ReadActivate(NetworkReader r) =>
            new SuperJumboBurgerActivateMessage();

        public static void WriteEffects(NetworkWriter w, SuperJumboBurgerEffectsMessage msg) =>
            w.WriteUInt(msg.PlayerNetId);

        public static SuperJumboBurgerEffectsMessage ReadEffects(NetworkReader r) =>
            new SuperJumboBurgerEffectsMessage { PlayerNetId = r.ReadUInt() };
    }
}
