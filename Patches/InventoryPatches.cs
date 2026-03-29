using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IssaPlugin.Patches
{
    [HarmonyPatch(typeof(GameManager))]
    public class GameManagerPatch
    {
        private static readonly FieldInfo _settingsField = AccessTools.Field(
            typeof(GameManager),
            "playerInventorySettings"
        );

        private static readonly MethodInfo _maxItemsSetter = AccessTools.PropertySetter(
            typeof(PlayerInventorySettings),
            "MaxItems"
        );

        [HarmonyPatch("Awake")]
        [HarmonyPostfix]
        private static void AwakePatch(GameManager __instance)
        {
            int newInventorySize = (int)Configuration.NumInventorySlots.Value;
            if (newInventorySize == 3)
                return; // Same as base game.

            var settings = (PlayerInventorySettings)_settingsField.GetValue(__instance);
            if (settings == null)
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[GameManager::PlayerInventorySettings] Failed to increase player inventory size."
                );
                return;
            }
            _maxItemsSetter.Invoke(settings, new object[] { newInventorySize });
        }
    }
}
