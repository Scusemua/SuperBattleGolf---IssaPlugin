using System.Reflection;
using HarmonyLib;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Expands the maximum values of the Player Speed, Cart Speed, Swing Power, and
    /// Countdown sliders in the match setup rules menu beyond the game's defaults
    /// (200% / 60 s).
    ///
    /// IMPORTANT: We need both a Prefix AND a Postfix here.
    ///
    /// The Prefix runs before Initialize so that when InitSlider calls
    /// sliderOption.SetValue(storedValue), Unity's Slider.value setter does not clamp
    /// the stored value (e.g. 200 s) down to the old maximum (60 s). If it were clamped,
    /// the slider's onChanged callback would fire with the clamped value and overwrite
    /// rules[Rule.Countdown] = 60, permanently losing the user's setting.
    ///
    /// The Postfix re-applies limits as a safety net for the first-ever call, when the
    /// SliderOption fields may not yet have been assigned before Initialize ran.
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

        private static void ExpandLimits(MatchSetupRules instance)
        {
            instance.playerSpeed?.SetLimits(GetMin(instance.playerSpeed), SpeedMaxMultiplier);
            instance.cartSpeed?.SetLimits(GetMin(instance.cartSpeed), SpeedMaxMultiplier);
            instance.swingPower?.SetLimits(GetMin(instance.swingPower), SpeedMaxMultiplier);
            instance.countdown?.SetLimits(GetMin(instance.countdown), CountdownMaxSeconds);
        }

        // Expand limits BEFORE InitSlider runs so that slider.value = storedValue
        // is not clamped to the old maximum, which would corrupt rules[Rule.Countdown].
        static void Prefix(MatchSetupRules __instance) => ExpandLimits(__instance);

        // Re-apply as a safety net (handles the first-ever call if any field was null above).
        static void Postfix(MatchSetupRules __instance) => ExpandLimits(__instance);
    }
}
