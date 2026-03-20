namespace IssaPlugin.Items
{
    public static class FreezeItem
    {
        /// <summary>
        /// True on all clients while the world is frozen. Set by FreezeNetworkBridge via RPC.
        /// </summary>
        public static bool IsFrozen { get; set; }

        public static void GiveFreezeToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(
                ItemRegistry.FreezeItemType,
                (int)Configuration.FreezeUses.Value,
                "Freeze"
            );
        }
    }
}
