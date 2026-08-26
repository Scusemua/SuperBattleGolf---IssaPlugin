using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ModConfig.Core;

namespace IssaPlugin.Integrations.ModConfigUI
{
    /// <summary>
    /// Hooks ModConfig once it has finished building its pages, then attaches
    /// <see cref="ConfigPageEnhancer"/> to the IssaPlugin page.
    ///
    /// A postfix on InitialiseConfigs is the right seam: it runs after every mod page
    /// exists, so the section hierarchy we post-process is fully populated.
    ///
    /// Note this class carries NO [HarmonyPatch] attributes and is patched manually by
    /// <see cref="ModConfigIntegration"/>. That is deliberate: Plugin.cs calls
    /// PatchAll(assembly), which eagerly resolves the target type of every attributed
    /// patch class it discovers. An attribute here would therefore force ModConfig's
    /// ConfigGetter to load on machines that do not have ModConfig installed, throwing
    /// TypeLoadException during startup.
    /// </summary>
    internal static class ModConfigPagePatch
    {
        /// <summary>Page name ModConfig derives from our BepInEx plugin metadata name.</summary>
        private static readonly string PageName = $"PAGE_{PluginInfo.PLUGIN_NAME}";

        /// <summary>The ModConfig method we postfix.</summary>
        public static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(ConfigGetter), nameof(ConfigGetter.InitialiseConfigs));

        public static MethodInfo PostfixMethod() =>
            AccessTools.Method(typeof(ModConfigPagePatch), nameof(Postfix));

        /// <summary>
        /// Postfix on <c>ConfigGetter.InitialiseConfigs(GameObject modsPage, GameObject topBar)</c>.
        /// The parameter name must stay <c>modsPage</c> so Harmony binds it positionally.
        /// </summary>
        private static void Postfix(GameObject modsPage)
        {
            try
            {
                if (modsPage == null) return;

                Transform page = modsPage.transform.Find(PageName);
                if (page == null)
                {
                    IssaPluginPlugin.Log.LogWarning(
                        $"[ModConfig] Could not find '{PageName}'; leaving the stock config UI untouched.");
                    return;
                }

                // Guard against ModConfig rebuilding its pages (e.g. menu reopened).
                if (page.GetComponent<ConfigPageEnhancer>() != null) return;

                var enhancer = page.gameObject.AddComponent<ConfigPageEnhancer>();
                if (!enhancer.Initialize(page.gameObject))
                {
                    UnityEngine.Object.DestroyImmediate(enhancer);
                }
            }
            catch (Exception ex)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[ModConfig] Error enhancing the config page: {ex}. "
                        + "The stock ModConfig UI will be used instead.");
            }
        }
    }
}
