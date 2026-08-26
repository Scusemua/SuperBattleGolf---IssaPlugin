using System;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace IssaPlugin.Integrations.ModConfigUI
{
    /// <summary>
    /// Optional integration with the ModConfig in-game settings mod
    /// (<c>com.atomic.modconfig</c>).
    ///
    /// ModConfig is a soft dependency: we reference its assembly at compile time but
    /// never ship it, and the game must run fine without it. To make that safe, every
    /// member that touches a ModConfig or Harmony-patched type lives behind
    /// <see cref="NoInlining"/> in a type that is only ever reached after the
    /// <see cref="IsModConfigPresent"/> check passes. If those types were referenced
    /// from a method in an already-loaded class, the JIT would raise a
    /// TypeLoadException at load time on machines without ModConfig installed.
    /// </summary>
    internal static class ModConfigIntegration
    {
        private const string ModConfigGuid = "com.atomic.modconfig";

        private static bool _applied;

        /// <summary>
        /// Applies the config UI enhancements if ModConfig is installed. Safe to call
        /// unconditionally; a no-op (with a debug log) when ModConfig is absent.
        /// </summary>
        public static void TryApply(Harmony harmony)
        {
            if (_applied) return;
            _applied = true;

            if (!IsModConfigPresent())
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[ModConfig] ModConfig is not installed; skipping config UI enhancements.");
                return;
            }

            try
            {
                ApplyPatches(harmony);
                IssaPluginPlugin.Log.LogInfo(
                    "[ModConfig] ModConfig detected; config UI enhancements enabled.");
            }
            catch (Exception ex)
            {
                // Never let an optional cosmetic integration take down plugin startup.
                IssaPluginPlugin.Log.LogWarning(
                    $"[ModConfig] Failed to enable config UI enhancements: {ex.Message}. "
                        + "The stock ModConfig UI will be used instead.");
            }
        }

        private static bool IsModConfigPresent() =>
            Chainloader.PluginInfos.ContainsKey(ModConfigGuid);

        /// <summary>
        /// Isolated so the ModConfig types it references are only resolved once we know
        /// the assembly is loaded.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ApplyPatches(Harmony harmony)
        {
            // Patched manually rather than via attributes — see ModConfigPagePatch for why.
            harmony.Patch(
                ModConfigPagePatch.TargetMethod(),
                postfix: new HarmonyMethod(ModConfigPagePatch.PostfixMethod()));
        }
    }
}
