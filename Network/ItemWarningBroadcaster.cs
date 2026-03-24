using Mirror;

namespace IssaPlugin.Network
{
    /// <summary>
    /// Server-side helper that sends an <see cref="ItemWarningMessage"/> to all clients
    /// when an item is activated, if that item's warning flag is enabled in config.
    ///
    /// To add a warning for a new item:
    ///   1. Call <c>ItemWarningBroadcaster.Broadcast()</c> in the item's server
    ///      activation method (right after item consumption).
    ///   2. Register a per-item entry in <c>Configuration.Initialize()</c> using
    ///      the <c>RegWarn</c> helper alongside the existing entries.
    /// </summary>
    public static class ItemWarningBroadcaster
    {
        /// <param name="playerName">Display name of the activating player.</param>
        /// <param name="itemType">The custom ItemType enum value.</param>
        /// <param name="displayName">Human-readable item name shown in the banner.</param>
        /// <param name="trackedNetId">
        ///   netId of the networked object to track in the PiP camera, or 0 for none.
        /// </param>
        /// <param name="senderNetId">
        ///   netId of the activating player's NetworkIdentity. Pass <c>netId</c>
        ///   (the <see cref="Mirror.NetworkBehaviour"/> property) from the bridge.
        /// </param>
        public static void Broadcast(
            string playerName,
            ItemType itemType,
            string displayName,
            uint trackedNetId = 0,
            uint senderNetId = 0
        )
        {
            if (!Configuration.GetItemWarningEnabled(itemType))
                return;

            NetworkServer.SendToAll(
                new ItemWarningMessage
                {
                    PlayerName = playerName,
                    ItemDisplayName = displayName,
                    ItemTypeValue = (int)itemType,
                    TrackedNetId = trackedNetId,
                    SenderNetId = senderNetId,
                }
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[Warning] Broadcast: {playerName} used {displayName} "
                    + $"(trackedNetId={trackedNetId}, senderNetId={senderNetId})"
            );
        }
    }
}
