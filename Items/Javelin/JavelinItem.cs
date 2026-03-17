using System.Threading;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Stateless utility class for the Javelin rocket launcher item.
    ///
    /// ItemType 108 — one above the existing Donut (107).
    public static class JavelinItem
    {
        public static readonly ItemType JavelinItemType = (ItemType)108;

        private static int _useIndex;

        public static int NextUseIndex() => Interlocked.Increment(ref _useIndex);

        public static void GiveJavelinToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(
                JavelinItemType,
                (int)Configuration.JavelinUses.Value,
                "Javelin"
            );
        }
    }
}
