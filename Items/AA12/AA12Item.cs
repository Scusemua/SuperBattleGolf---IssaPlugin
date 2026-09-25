using System.Collections;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Fully automatic shotgun. One shell per fire-rate tick while left-click is held.
    /// The shot sound plays once per trigger pull. Each shell is one use.
    /// </summary>
    public static class AA12Item
    {
        public static IEnumerator FireLoop(PlayerInventory inventory) =>
            Firearm.HoldFire(
                inventory,
                ItemRegistry.AA12ItemType,
                ShellProfile,
                () => ModConfig.AA12.FireRate.Value
            );

        private static FirearmShellProfile ShellProfile() =>
            new FirearmShellProfile
            {
                ItemType = ItemRegistry.AA12ItemType,
                HitResponseType = ItemType.ElephantGun,
                MaxAimingDistance = ModConfig.AA12.MaxAimingDistance.Value,
                MaxShotDistance = ModConfig.AA12.MaxShotDistance.Value,
                Inaccuracy = ModConfig.AA12.Inaccuracy.Value,
                PelletCount = ModConfig.AA12.PelletCount.Value,
                ScreenShakeIntensity = ModConfig.AA12.ScreenShakeIntensity.Value,
                BonusKnockback = ModConfig.AA12.BonusKnockback.Value,
                ThrottleVfx = true,
            };
    }
}
