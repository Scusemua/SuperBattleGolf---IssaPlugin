using System.Collections;
using System.Reflection;
using UnityEngine;

namespace IssaPlugin.Items
{
    public class ScreenShakeHelper
    {
        // ── Reflected fields on ScreenshakeSettings (private auto-property backing fields) ───

        public static readonly FieldInfo ShakeDurationField = typeof(ScreenshakeSettings).GetField(
            "<Duration>k__BackingField",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        public static readonly FieldInfo ShakePositionCurveField =
            typeof(ScreenshakeSettings).GetField(
                "<PositionIntensityOverTime>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        public static readonly FieldInfo ShakeRotationCurveField =
            typeof(ScreenshakeSettings).GetField(
                "<RotationIntensityOverTime>k__BackingField",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

        public static void ApplyScreenShake(float intensity)
        {
            if (intensity <= 0f)
                return;

            var settings = ScriptableObject.CreateInstance<ScreenshakeSettings>();
            ScreenShakeHelper.ShakeDurationField?.SetValue(settings, 0.35f);
            ScreenShakeHelper.ShakePositionCurveField?.SetValue(
                settings,
                AnimationCurve.EaseInOut(0f, intensity, 1f, 0f)
            );
            ScreenShakeHelper.ShakeRotationCurveField?.SetValue(
                settings,
                AnimationCurve.EaseInOut(0f, intensity * 15f, 1f, 0f)
            );
            CameraModuleController.Shake(settings);
        }
    }
}
