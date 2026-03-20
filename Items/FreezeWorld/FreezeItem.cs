namespace IssaPlugin.Items
{
    public static class FreezeItem
    {
        /// <summary>
        /// True on all clients while the world is frozen. Set by FreezeNetworkBridge via RPC.
        /// </summary>
        public static bool IsFrozen { get; set; }
    }
}
