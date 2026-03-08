using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Patches
{
    // ================================================================
    //  1. Temporarily scale RocketExplosionRange during ServerExplode
    // ================================================================

    [HarmonyPatch]
    static class ServerExplodeScalePatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(Rocket), "ServerExplode");

        static readonly MethodInfo _rangeSetter =
            AccessTools.PropertySetter(typeof(ItemSettings), "RocketExplosionRange");

        static void Prefix(Rocket __instance, out float __state)
        {
            float scale = ExplosionScaler.GetScale(__instance);
            ExplosionScaler.ActiveScale = scale;

            if (Mathf.Approximately(scale, 1f))
            {
                __state = -1f;
                return;
            }

            float original = GameManager.ItemSettings.RocketExplosionRange;
            __state = original;

            if (_rangeSetter != null)
            {
                _rangeSetter.Invoke(GameManager.ItemSettings, new object[] { original * scale });
            }
            else
            {
                var backingField = AccessTools.Field(
                    typeof(ItemSettings),
                    "<RocketExplosionRange>k__BackingField"
                );
                backingField?.SetValue(GameManager.ItemSettings, original * scale);
            }
        }

        static void Postfix(Rocket __instance, float __state)
        {
            if (__state >= 0f)
            {
                if (_rangeSetter != null)
                    _rangeSetter.Invoke(GameManager.ItemSettings, new object[] { __state });
                else
                {
                    var backingField = AccessTools.Field(
                        typeof(ItemSettings),
                        "<RocketExplosionRange>k__BackingField"
                    );
                    backingField?.SetValue(GameManager.ItemSettings, __state);
                }
            }

            ExplosionScaler.ActiveScale = 1f;
            ExplosionScaler.Unregister(__instance);
        }
    }

    // ================================================================
    //  2. Scale VFX size for custom rocket explosions
    //     Patches the static PlayPooledVfxLocalOnly overload that
    //     OnExploded calls (8-parameter variant).
    // ================================================================

    [HarmonyPatch]
    static class VfxExplosionScalePatch
    {
        static MethodBase TargetMethod()
        {
            return typeof(VfxManager)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                    m.Name == "PlayPooledVfxLocalOnly"
                    && m.GetParameters().Length == 8
                    && m.GetParameters()[0].ParameterType == typeof(VfxType)
                    && m.GetParameters()[3].ParameterType == typeof(Vector3)
                );
        }

        static void Prefix(VfxType vfxType, ref Vector3 localScale)
        {
            if (vfxType != VfxType.RocketLauncherRocketExplosion)
                return;

            float scale = ExplosionScaler.ActiveScale;
            if (Mathf.Approximately(scale, 1f))
                return;

            localScale = Vector3.one * scale;
        }
    }

    // ================================================================
    //  3. Scale knockback by adjusting the distance parameter
    //     in Hittable.HitWithItem.  Closer distance = more knockback.
    // ================================================================

    [HarmonyPatch(typeof(Hittable), nameof(Hittable.HitWithItem))]
    static class HitWithItemScalePatch
    {
        static void Prefix(ItemType itemType, ref float distance)
        {
            if (itemType != ItemType.RocketLauncher)
                return;

            float scale = ExplosionScaler.ActiveScale;
            if (scale <= 0f || Mathf.Approximately(scale, 1f))
                return;

            distance /= scale;
        }
    }
}
