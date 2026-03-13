namespace IssaPlugin.Items
{
    public static class LowGravityItem
    {
        public static readonly ItemType LowGravityItemType = (ItemType)105;

        /// <summary>
        /// True on all clients while low gravity is active. Set by LowGravityNetworkBridge via RPC.
        /// </summary>
        public static bool IsActive { get; set; }

        public static void GiveLowGravityToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(
                LowGravityItemType,
                (int)Configuration.LowGravityUses.Value,
                "LowGravity"
            );
        }
    }
}
