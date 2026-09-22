namespace IssaPlugin.Items
{
    /// <summary>
    /// Client-local state. True only when this client should have the terrain
    /// prediction sections removed from the base game's swing power gauge.
    /// </summary>
    public static class PowerJammerItem
    {
        public static bool IsAffected { get; internal set; }
    }
}
