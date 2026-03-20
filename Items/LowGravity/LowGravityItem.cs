namespace IssaPlugin.Items
{
    public static class LowGravityItem
    {
        /// <summary>
        /// True on all clients while low gravity is active. Set by LowGravityNetworkBridge via RPC.
        /// </summary>
        public static bool IsActive { get; set; }

        public static void GiveLowGravityToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(
                ItemRegistry.LowGravityItemType,
                (int)Configuration.LowGravityUses.Value,
                "LowGravity"
            );
        }
    }
}
