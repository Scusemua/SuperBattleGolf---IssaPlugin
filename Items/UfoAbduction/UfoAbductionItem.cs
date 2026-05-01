using System.Threading;

namespace IssaPlugin.Items
{
    public static class UfoAbductionItem
    {
        private static int _useIndex;

        public static int NextUseIndex() => Interlocked.Increment(ref _useIndex);
    }
}
