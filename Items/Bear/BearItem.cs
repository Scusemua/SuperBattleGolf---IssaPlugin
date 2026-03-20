using System.Threading;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Stateless utility class for the Bear item.
    ///
    /// ItemType 110 — one above StickyGrenade (109).
    ///
    /// Behaviour:
    ///   1. Player uses item → sends BearSummonMessage to server.
    ///   2. Server validates, consumes item, spawns BearCount networked bear GameObjects.
    ///   3. Each bear runs a server-authoritative AI state machine (BearBehaviour).
    ///   4. Clients receive BearStateMessages and drive their local Animators accordingly.
    ///   5. When a bear attacks a player, the server calls HitWithItem on the target.
    ///   6. Bears can be killed by rockets/explosions via BearHitReceiver.
    ///   7. All bears despawn when the session timeout expires or the hole ends.
    /// </summary>
    public static class BearItem
    {
        private static int _useIndex;

        public static int NextUseIndex() => Interlocked.Increment(ref _useIndex);
    }
}
