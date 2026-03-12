namespace IssaPlugin.Items
{
    public static class FreezeItem
    {
        public static readonly ItemType FreezeItemType = (ItemType)104;

        /// <summary>
        /// True on all clients while the world is frozen. Set by FreezeNetworkBridge via RPC.
        /// </summary>
        public static bool IsFrozen { get; set; }

        public static void GiveFreezeToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(FreezeItemType, (int)Configuration.FreezeUses.Value, "Freeze");
        }
    }
}
