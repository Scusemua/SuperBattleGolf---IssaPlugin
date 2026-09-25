using System.Collections;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Sub-machine gun. One bullet per fire-rate tick while left-click is held.
    /// The shot sound plays once per trigger pull. Each bullet consumes one use.
    /// </summary>
    public static class AK47Item
    {
        public static IEnumerator FireLoop(PlayerInventory inventory) =>
            Firearm.HoldFire(
                inventory,
                ItemRegistry.AK47ItemType,
                ShellProfile,
                () => ModConfig.AK47.FireRate.Value
            );

        private static FirearmShellProfile ShellProfile() =>
            new FirearmShellProfile
            {
                ItemType = ItemRegistry.AK47ItemType,
                HitResponseType = ItemType.DuelingPistol,
                MaxAimingDistance = ModConfig.AK47.MaxAimingDistance.Value,
                MaxShotDistance = ModConfig.AK47.MaxShotDistance.Value,
                Inaccuracy = ModConfig.AK47.Inaccuracy.Value,
                PelletCount = 1,
                ScreenShakeIntensity = ModConfig.AK47.ScreenShakeIntensity.Value,
                ThrottleVfx = true,
            };
    }
}
