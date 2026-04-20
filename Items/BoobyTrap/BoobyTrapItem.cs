using System.Threading;

namespace IssaPlugin.Items
{
    public static class BoobyTrapItem
    {
        private static int _useIndex;

        /// Returns a monotonically increasing index used to generate unique
        /// ItemUseIds for the temporary detonation rockets.
        public static int NextUseIndex() => Interlocked.Increment(ref _useIndex);
    }
}
