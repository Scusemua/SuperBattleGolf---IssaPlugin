using HarmonyLib;
using IssaPlugin.Items;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Suppresses only the terrain/hazard sections of the base game's swing
    /// power bar. The numeric power fill, charge behavior, trajectory simulation,
    /// and actual shot physics continue to run normally.
    /// </summary>
    [HarmonyPatch(typeof(SwingPowerBarUi), nameof(SwingPowerBarUi.SetTerrainLayers))]
    static class PowerJammerTerrainPreviewPatch
    {
        static bool Prefix()
        {
            if (!PowerJammerItem.IsAffected)
                return true;

            SwingPowerBarUi.HideTerrainLayers();
            return false;
        }
    }
}
