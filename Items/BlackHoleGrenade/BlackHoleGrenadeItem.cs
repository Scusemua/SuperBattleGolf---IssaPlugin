using System.Threading;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Stateless utility class for the BlackHoleGrenade item.
    ///
    /// ItemType 112 — one above Nuke (111).
    ///
    /// Behaviour:
    ///   1. Player presses use → grenade is thrown in camera-forward direction.
    ///   2. Grenade flies as a physics projectile (server-authoritative Rigidbody).
    ///   3. On first contact with terrain/ground it freezes and becomes a black hole.
    ///   4. The black hole sucks in nearby players, golf balls, and golf carts for
    ///      a configurable duration.  Suction grows stronger closer to the center.
    ///   5. After the suction phase, the black hole spits everything out in random
    ///      directions (with upward bias) and destroys itself.
    /// </summary>
    public static class BlackHoleGrenadeItem
    {
        private static int _useIndex;

        public static int NextUseIndex() => Interlocked.Increment(ref _useIndex);
    }
}
