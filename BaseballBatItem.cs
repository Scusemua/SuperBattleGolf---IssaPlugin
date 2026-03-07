using System.Collections;
using System.Reflection;
using IssaPlugin.Patches;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    public static class BatItem
    {
        public static readonly ItemType BatItemType = (ItemType)100;

        private static bool _isSwinging;
        private static MethodInfo _cmdAddItemMethod;

        public static void GiveBatToLocalPlayer()
        {
            var inventory = GameManager.LocalPlayerInventory;
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogWarning("[Bat] No local player inventory.");
                return;
            }

            if (NetworkServer.active)
            {
                bool added = InventoryPatches.DirectAddCustomItem(
                    inventory,
                    BatItemType,
                    Configuration.BaseballBatUses.Value
                );
                if (!added)
                    IssaPluginPlugin.Log.LogWarning("[Bat] Failed to add bat (inventory full?).");
            }
            else
            {
                if (_cmdAddItemMethod == null)
                {
                    _cmdAddItemMethod = typeof(PlayerInventory).GetMethod(
                        "CmdAddItem",
                        BindingFlags.NonPublic | BindingFlags.Instance
                    );
                }

                if (_cmdAddItemMethod != null)
                {
                    _cmdAddItemMethod.Invoke(inventory, new object[] { BatItemType });
                    IssaPluginPlugin.Log.LogInfo("[Bat] Requested bat via server command.");
                }
                else
                {
                    IssaPluginPlugin.Log.LogError("[Bat] Could not find CmdAddItem method.");
                }
            }
        }

        public static IEnumerator BatSwingRoutine(PlayerInventory inventory)
        {
            if (_isSwinging)
                yield break;
            _isSwinging = true;

            var playerInfo = inventory.PlayerInfo;
            var golfer = playerInfo.GetComponent<PlayerGolfer>();

            // Brief windup
            yield return new WaitForSeconds(Configuration.BaseballBatChargeTime.Value);

            Vector3 center = playerInfo.transform.TransformPoint(new Vector3(0f, 1f, 1.5f));
            Vector3 halfExtents = new Vector3(1.5f, 1f, 1.5f);
            int mask = GameManager.LayerSettings.SwingHittableMask;

            var hits = Physics.OverlapBox(
                center,
                halfExtents,
                playerInfo.transform.rotation,
                mask,
                QueryTriggerInteraction.Ignore
            );

            foreach (var col in hits)
            {
                var hittable = col.GetComponentInParent<Hittable>();
                if (hittable == null || hittable == playerInfo.AsEntity.AsHittable)
                    continue;

                Vector3 dir = playerInfo.transform.forward;
                Vector3 hitPos = col.ClosestPoint(center);
                Vector3 localHit = hittable.transform.InverseTransformPoint(hitPos);
                Vector3 localOrigin = hittable.transform.InverseTransformPoint(center);

                hittable.HitWithGolfSwing(
                    localHit,
                    localOrigin,
                    dir,
                    isPutt: false,
                    power: Configuration.BaseballBatPower.Value,
                    sideSpin: 0f,
                    hitter: golfer,
                    homingTargetHittable: null
                );
            }

            yield return new WaitForSeconds(Configuration.BaseballBatCooldown.Value);
            _isSwinging = false;
        }
    }
}
