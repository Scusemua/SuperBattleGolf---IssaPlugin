using System.Threading;

namespace IssaPlugin.Items
{
    public static class ExplosiveGolfBallsItem
    {
        private static int _useIndex;

        public static int NextUseIndex() => Interlocked.Increment(ref _useIndex);
    }
}
