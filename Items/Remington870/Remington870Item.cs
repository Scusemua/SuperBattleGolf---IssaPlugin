using System.Collections;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Pump-action shotgun. One shell per use, then a pump delay.
    /// The shot sound plays once per shell. Each shell is one use.
    /// </summary>
    public static class Remington870Item
    {
        public static IEnumerator ShootRoutine(PlayerInventory inventory) =>
            Firearm.SingleShot(
                inventory,
                ItemRegistry.Remington870ItemType,
                ShellProfile,
                () => ModConfig.Remington870.PumpDuration.Value
            );

        private static FirearmShellProfile ShellProfile() =>
            new FirearmShellProfile
            {
                ItemType = ItemRegistry.Remington870ItemType,
                HitResponseType = ItemType.ElephantGun,
                MaxAimingDistance = ModConfig.Remington870.MaxAimingDistance.Value,
                MaxShotDistance = ModConfig.Remington870.MaxShotDistance.Value,
                Inaccuracy = ModConfig.Remington870.Inaccuracy.Value,
                PelletCount = ModConfig.Remington870.PelletCount.Value,
                ScreenShakeIntensity = ModConfig.Remington870.ScreenShakeIntensity.Value,
                BonusKnockback = ModConfig.Remington870.BonusKnockback.Value,
                ThrottleVfx = false,
            };
    }
}
