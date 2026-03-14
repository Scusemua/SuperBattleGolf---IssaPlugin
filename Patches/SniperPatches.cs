using System.Collections.Generic;
using System.Reflection;
using System.Security.Authentication.ExtendedProtection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    [HarmonyPatch(typeof(PlayerInventory), "UpdateIsAimingItem")]
    static class PlayerInventoryUpdateIsAimingItemPatch
    {
        private static readonly PropertyInfo IsAimingItemField =
            typeof(PlayerInventory).GetProperty(
                "IsAimingItem",
                BindingFlags.Public | BindingFlags.Instance
            );

        private static MethodInfo UpdateIsUpdateLoopRunningMethod =
            typeof(PlayerInventory).GetMethod(
                "UpdateIsUpdateLoopRunning",
                BindingFlags.Instance | BindingFlags.NonPublic
            );

        private static MethodInfo UpdateAimingReticleMethod = typeof(PlayerInventory).GetMethod(
            "UpdateAimingReticle",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        private static bool ShouldAim(PlayerInventory __instance)
        {
            return (
                    !__instance.IsUsingItemAtAll
                    || (
                        BMath.GetTimeSince(__instance.ItemUseTimestamp)
                        >= GameManager.ItemSettings.ElephantGunShotDuration
                    )
                )
                && __instance.PlayerInfo.Input.IsHoldingAimSwing
                && !__instance.PlayerInfo.AsGolfer.IsMatchResolved
                && !__instance.PlayerInfo.AsSpectator.IsSpectating
                && !__instance.PlayerInfo.Movement.IsRespawning
                && !__instance.PlayerInfo.Movement.IsKnockedOutOrRecovering
                && !RadialMenu.IsVisible;
        }

        static bool Prefix(PlayerInventory __instance)
        {
            var equipped = __instance.GetEffectivelyEquippedItem(true);

            if (equipped != SniperRifleItem.SniperRifleItemType)
            {
                return true;
            }

            bool isAimingItem = __instance.IsAimingItem;
            IsAimingItemField.SetValue(__instance, ShouldAim(__instance));

            if (__instance.IsAimingItem == isAimingItem)
            {
                return false;
            }

            IssaPluginPlugin.Log.LogInfo($"[AimingPatch] IsAimingItem={__instance.IsAimingItem}");

            if (__instance.IsAimingItem)
            {
                GameplayCameraManager.EnterSwingAimCamera();
                __instance.PlayerInfo.PlayerAudio.PlayGunAimForAllClients(ItemType.ElephantGun);
                __instance.PlayerInfo.CancelEmote(false);
                __instance.CancelItemFlourish();
            }
            else
            {
                GameplayCameraManager.ExitSwingAimCamera();
            }

            UpdateIsUpdateLoopRunningMethod.Invoke(__instance, []);
            UpdateAimingReticleMethod.Invoke(__instance, []);

            __instance.PlayerInfo.SetIsAimingItem(__instance.IsAimingItem);
            __instance.PlayerInfo.Movement.InformIsAimingItemChanged();
            __instance.PlayerInfo.AnimatorIo.SetIsAimingItem(__instance.IsAimingItem);

            return false;
        }
    }
}
