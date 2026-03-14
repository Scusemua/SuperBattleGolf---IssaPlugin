namespace IssaPlugin.Items
{
    /// <summary>
    /// Stateless utility class for the UFO item.
    /// All per-session state lives in UFONetworkBridge instance fields.
    /// </summary>
    public static class UFOItem
    {
        public static readonly ItemType UFOItemType = (ItemType)107;

        public static void GiveUFOToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(UFOItemType, (int)Configuration.UFOUses.Value, "UFO");
        }
    }
}
