using System.Threading;

namespace IssaPlugin.Items
{
    public static class PlaceableWallItem
    {
        private static int _nextUseIndex;

        public static int NextUseIndex() => Interlocked.Increment(ref _nextUseIndex);
    }
}
