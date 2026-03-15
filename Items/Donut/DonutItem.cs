namespace IssaPlugin.Items
{
    /// <summary>
    /// Stateless utility class for the Donut item.
    /// All per-session state lives in DonutNetworkBridge instance fields.
    /// </summary>
    public static class DonutItem
    {
        public static readonly ItemType DonutItemType = (ItemType)107;

        public static void GiveDonutToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(DonutItemType, (int)Configuration.DonutUses.Value, "Donut");
        }
    }
}
