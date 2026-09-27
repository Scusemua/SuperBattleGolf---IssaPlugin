using System.Collections;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Sniper rifle. One hitscan shot per use, then a recovery lock.
    /// Right-click zooms via SniperScopeOverlay. Does not call InformShotElephantGun.
    /// </summary>
    public static class SniperRifleItem
    {
        /// <summary>True on the local client while the scope is being held (right-click).</summary>
        public static bool IsScoped { get; set; }

        public static IEnumerator ShootRoutine(PlayerInventory inventory) =>
            Firearm.SingleShot(
                inventory,
                ItemRegistry.SniperRifleItemType,
                ShellProfile,
                () => ModConfig.SniperRifle.ShotDuration.Value
            );

        private static FirearmShellProfile ShellProfile()
        {
            float inaccuracy = IsScoped
                ? ModConfig.SniperRifle.ScopedInaccuracy.Value
                : ModConfig.SniperRifle.HipFireInaccuracy.Value;

            return new FirearmShellProfile
            {
                ItemType = ItemRegistry.SniperRifleItemType,
                HitResponseType = ItemType.ElephantGun,
                MaxAimingDistance = ModConfig.SniperRifle.MaxAimingDistance.Value,
                MaxShotDistance = ModConfig.SniperRifle.MaxShotDistance.Value,
                Inaccuracy = inaccuracy,
                PelletCount = 1,
                ScreenShakeIntensity = ModConfig.SniperRifle.ScreenShakeIntensity.Value,
                VfxInterval = ModConfig.SniperRifle.VfxInterval.Value,
            };
        }
    }
}
