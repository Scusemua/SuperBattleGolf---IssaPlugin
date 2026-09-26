using System.Collections;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Heavy automatic gun. Spins up, then fires one bullet per fire-rate tick
    /// while left-click is held. The shot sound plays once per trigger pull.
    /// Each bullet consumes one use. The spin-up consumes none.
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
                driveUseAnimation: false
            );

        private static FirearmShellProfile ShellProfile() =>
            new FirearmShellProfile
            {
                ItemType = ItemRegistry.MinigunItemType,
                HitResponseType = ItemType.DuelingPistol,
                MaxAimingDistance = ModConfig.Minigun.MaxAimingDistance.Value,
                MaxShotDistance = ModConfig.Minigun.MaxShotDistance.Value,
                Inaccuracy = ModConfig.Minigun.Inaccuracy.Value,
                PelletCount = 1,
                ScreenShakeIntensity = ModConfig.Minigun.ScreenShakeIntensity.Value,
                ThrottleVfx = true,
                UseShotgunVisuals = true,
            };
    }
}
