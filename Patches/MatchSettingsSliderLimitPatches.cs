using System.Reflection;
using HarmonyLib;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Expands the maximum values of the Player Speed, Cart Speed, Swing Power, and
    /// Countdown sliders in the match setup rules menu beyond the game's defaults
    /// (200% / 60 s). Runs after Initialize so the sliders already exist.
    /// </summary>
    [HarmonyPatch(typeof(MatchSetupRules), nameof(MatchSetupRules.Initialize))]
    static class MatchSettingsSliderLimitPatches
    {
        // Speed / power sliders store multipliers (1.0 = 100%). 1000% → 10.0.
        private const float SpeedMaxMultiplier = 25f;

        // Countdown slider is in seconds. 5 minutes → 300 s.
        private const float CountdownMaxSeconds = 500f;

        // SliderOption.slider is a private UnityEngine.UI.Slider field. We read minValue
        // through reflection so we don't need to add UnityEngine.UI.dll as a reference.
        private static readonly FieldInfo SliderField = typeof(SliderOption).GetField(
            "slider",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        private static readonly PropertyInfo MinValueProp = SliderField?.FieldType.GetProperty(
            "minValue"
        );

        private static float GetMin(SliderOption opt)
        {
            var slider = SliderField?.GetValue(opt);
            if (slider == null || MinValueProp == null)
                return 0f;
            return (float)MinValueProp.GetValue(slider);
        }

        static void Postfix(MatchSetupRules __instance)
        {
            __instance.playerSpeed.SetLimits(GetMin(__instance.playerSpeed), SpeedMaxMultiplier);
            __instance.cartSpeed.SetLimits(GetMin(__instance.cartSpeed), SpeedMaxMultiplier);
            __instance.swingPower.SetLimits(GetMin(__instance.swingPower), SpeedMaxMultiplier);
            __instance.countdown.SetLimits(GetMin(__instance.countdown), CountdownMaxSeconds);
        }
    }
}
