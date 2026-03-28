using System.Threading;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Utility class for the Electric Grapple item.
    /// Provides a thread-safe incrementing use-index counter used when
    /// constructing ItemUseId values for TryKnockOut attribution.
    /// </summary>
    public static class GrappleItem
    {
        private static int _useIndex;

        public static int NextUseIndex() => Interlocked.Increment(ref _useIndex);
    }
}
