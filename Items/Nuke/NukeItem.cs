using System.Threading;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Stateless utility class for the Nuke item.
    ///
    /// ItemType 111 — one above Bear (110).
    /// </summary>
    public static class NukeItem
    {
        private static int _useIndex;

        /// Returns a monotonically increasing index used to generate unique
        /// ItemUseIds for the temporary detonation rockets spawned at impact.
        public static int NextUseIndex() => Interlocked.Increment(ref _useIndex);
    }
}
