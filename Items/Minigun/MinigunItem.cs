using System.Collections;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Heavy automatic gun. Spins up while aimed in, then fires one shot per
    /// fire-rate tick while left-click and aim are held. The shot sound plays
    /// once per trigger pull. Each shot consumes one use. The spin-up consumes none.
    /// </summary>
    public static class MinigunItem
    {
        public static IEnumerator FireLoop(PlayerInventory inventory) =>
            Firearm.HoldFire(
                inventory,
                ItemRegistry.MinigunItemType,
                ShellProfile,
                () => ModConfig.Minigun.FireRate.Value,
                ModConfig.Minigun.SpinUp.Value,
                driveUseAnimation: false,
                requireAim: true
            );

        private static FirearmShellProfile ShellProfile() =>
            new FirearmShellProfile
            {
                ItemType = ItemRegistry.MinigunItemType,
                HitResponseType = ItemType.DuelingPistol,
                MaxAimingDistance = ModConfig.Minigun.MaxAimingDistance.Value,
                MaxShotDistance = ModConfig.Minigun.MaxShotDistance.Value,
                Inaccuracy = ModConfig.Minigun.Inaccuracy.Value,
                PelletCount = Mathf.Max(1, Mathf.RoundToInt(ModConfig.Minigun.PelletCount.Value)),
                ScreenShakeIntensity = ModConfig.Minigun.ScreenShakeIntensity.Value,
                UseShotgunVisuals = true,
            };
    }
}
