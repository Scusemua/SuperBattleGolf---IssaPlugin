using System.Threading;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Stateless utility class for the StickyGrenade sticky grenade item.
    ///
    /// ItemType 109 — one above Javelin (108).
    ///
    /// Behaviour:
    ///   1. Player presses use → grenade is thrown in camera-forward direction.
    ///   2. Grenade flies as a physics projectile (server-authoritative Rigidbody).
    ///   3. On first contact with anything — player, vehicle, terrain, golf cart —
    ///      it sticks: Rigidbody is disabled, and the grenade tracks its target's
    ///      transform each FixedUpdate.
    ///   4. After FuseTime seconds it explodes with a large scaled explosion.
    ///   5. While the fuse burns, the prefab's particle system / light blink at
    ///      increasing frequency — driven client-side inside StickyGrenadeClientSetup.
    /// </summary>
    public static class StickyGrenadeItem
    {
        public static readonly ItemType StickyGrenadeItemType = (ItemType)109;

        private static int _useIndex;

        public static int NextUseIndex() => Interlocked.Increment(ref _useIndex);
    }
}
