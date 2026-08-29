extern alias ItemSpawnerMod;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace IssaPlugin.Integrations.SpawnerUI
{
    /// <summary>
    /// Optional interoperability with the ItemSpawner mod (<c>com.atomic.itemspawner</c>).
    ///
    /// Note this is interoperability, not a dependency: <see cref="SpawnerWindow"/> is
    /// entirely self-contained and works whether or not ItemSpawner is installed. All
    /// this does is suppress ItemSpawner's own window when both are present, so the
    /// player does not get two overlapping spawners bound to the same key.
    ///
    /// The suppression is a prefix on their OnGUI that skips drawing. We deliberately do
    /// not reflect into any of their private state — their window simply stops drawing,
    /// and everything ours needs comes from the base game or our own registry.
    ///
    /// As with the ModConfig integration, the patch class carries no [HarmonyPatch]
    /// attributes and is applied manually, because Plugin.cs calls PatchAll(assembly)
    /// and that eagerly resolves the target type of every attributed patch class it
    /// finds — which would force ItemSpawner to load on machines without it.
    /// </summary>
    internal static class ItemSpawnerIntegration
    {
        private const string ItemSpawnerGuid = "com.atomic.itemspawner";

        private static bool _applied;

        /// <summary>True when the ItemSpawner mod is loaded in this session.</summary>
        public static bool IsItemSpawnerPresent =>
            Chainloader.PluginInfos.ContainsKey(ItemSpawnerGuid);

        /// <summary>
        /// Suppresses ItemSpawner's window if that mod is installed. Safe to call
        /// unconditionally; a logged no-op otherwise.
        /// </summary>
        public static void TryApply(Harmony harmony)
        {
            if (_applied) return;
            _applied = true;

            if (!IsItemSpawnerPresent)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[Spawner] ItemSpawner is not installed; using our spawner window on its own.");
                return;
            }

            if (!ModConfig.Global.SpawnerSuppressItemSpawner.Value)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[Spawner] ItemSpawner is installed; leaving its window alone "
                        + "(SpawnerSuppressItemSpawner is false). Both windows will be available.");
                return;
            }

            try
            {
                ApplySuppression(harmony);
                IssaPluginPlugin.Log.LogInfo(
                    "[Spawner] ItemSpawner detected; its window is suppressed in favour of ours.");
            }
            catch (Exception ex)
            {
                // Non-fatal: our window still works, the user just sees theirs too.
                IssaPluginPlugin.Log.LogWarning(
                    $"[Spawner] Could not suppress the ItemSpawner window: {ex.Message}. "
                        + "Both spawner windows may appear.");
            }
        }

        /// <summary>
        /// Isolated behind NoInlining so the ItemSpawner type it references is only
        /// resolved once we know the assembly is loaded.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplySuppression(Harmony harmony)
        {
            harmony.Patch(
                ItemSpawnerWindowPatch.TargetMethod(),
                prefix: new HarmonyMethod(ItemSpawnerWindowPatch.PrefixMethod()));
        }
    }

    /// <summary>
    /// Skips ItemSpawner's OnGUI so only one spawner window draws. Carries no Harmony
    /// attributes on purpose — see <see cref="ItemSpawnerIntegration"/>.
    /// </summary>
    internal static class ItemSpawnerWindowPatch
    {
        public static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(ItemSpawnerMod::ItemSpawner.Plugin), "OnGUI");

        public static MethodInfo PrefixMethod() =>
            AccessTools.Method(typeof(ItemSpawnerWindowPatch), nameof(Prefix));

        /// <summary>Returning false skips their window entirely.</summary>
        private static bool Prefix() => false;
    }
}
