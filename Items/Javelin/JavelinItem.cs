using System.Collections.Generic;
using System.Threading;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Stateless utility class for the Javelin rocket launcher item.
    ///
    /// ItemType 108 — one above the existing Donut (107).
    /// </summary>
    public static class JavelinItem
    {
        private static int _useIndex;

        public static int NextUseIndex() => Interlocked.Increment(ref _useIndex);

        /// Rockets whose Rocket.OnFixedBUpdate must be fully suppressed so that
        /// JavelinRocketBehaviour.FixedUpdate has exclusive control over velocity.
        /// Mirrors the ActiveMissileRockets pattern in PredatorMissileItem.
        internal static readonly HashSet<Rocket> ActiveJavelinRockets = new();
    }
}
