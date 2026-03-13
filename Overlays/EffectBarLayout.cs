using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Shared layout constants for the HUD effect-duration bars drawn by overlay classes.
    ///
    /// Each bar occupies a "slot" counted upward from the bottom of the screen.
    /// Slot 0 = bottommost bar, slot 1 = one bar above it, etc.
    /// Overlays should query their slot each frame so stacking adjusts automatically
    /// as effects start and end.
    /// </summary>
    internal static class EffectBarLayout
    {
        public const float BarHeight       = 24f;
        public const float BarWidthFraction = 0.5f; // fraction of screen width
        private const float BarGap          = 6f;
        private const float BottomMargin    = 52f;

        public static float GetBarWidth()  => Screen.width * BarWidthFraction;
        public static float GetBarX()      => (Screen.width - GetBarWidth()) * 0.5f;
        public static float GetBarY(int slot) =>
            Screen.height - BottomMargin - slot * (BarHeight + BarGap);
    }
}
