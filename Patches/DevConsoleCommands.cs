using System;
using System.Linq;
using IssaPlugin.Items;
using IssaPlugin.Network;
using IssaPlugin.Overlays;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Registers custom dev-console commands for the mod.
    ///
    /// DevConsole.LoadStaticAssemblies() scans AppDomain.CurrentDomain.GetAssemblies()
    /// for static methods tagged [CCommand]. Because BepInEx loads our mod assembly
    /// before the game's manager scene initialises DevConsole, our commands are
    /// discovered automatically — no extra registration step is required.
    /// </summary>
    internal static class DevConsoleCommands
    {
        /// <summary>
        /// Console command: giveCustomItem <name>
        /// Gives the named custom item to the local player.
        ///
        /// Also accepts an integer item-type ID, e.g. giveCustomItem 100.
        /// </summary>
        [CCommand("giveCustomItem", "Give a custom mod item. Usage: giveCustomItem <name|id>")]
        private static void GiveCustomItem(string itemName)
        {
            // Integer fallback: giveCustomItem 100
            if (int.TryParse(itemName, out int id))
            {
                var def = ItemRegistry.GetDefinition((ItemType)id);
                if (def == null)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[giveCustomItem] Item ID {id} is not a recognised custom item."
                    );
                    return;
                }
                ItemHelper.GiveItemToLocalPlayer(def.ItemType, def.MaxUses, "giveCustomItem");
                return;
            }

            // Named lookup — all aliases are lowercase; comparison is OrdinalIgnoreCase.
            foreach (var def in ItemRegistry.AllItems)
            foreach (var alias in def.ConsoleAliases)
                if (string.Equals(itemName, alias, StringComparison.OrdinalIgnoreCase))
                {
                    ItemHelper.GiveItemToLocalPlayer(def.ItemType, def.MaxUses, "giveCustomItem");
                    return;
                }

            // Build valid-name hint from the registry (stays in sync automatically).
            var validNames = string.Join(
                ", ",
                ItemRegistry.AllItems.SelectMany(d => d.ConsoleAliases)
            );
            UnityEngine.Debug.LogWarning(
                $"[giveCustomItem] Unknown item \"{itemName}\". Valid names: {validNames} (or an integer item-type ID)."
            );
        }

        /// <summary>
        /// Console command: vote [timeout]
        /// Starts an enable/disable vote for all custom items. Host only.
        /// Timeout defaults to 30 seconds if omitted.
        /// </summary>
        [CCommand("vote", "Start a custom-item enable/disable vote. Usage: vote <timeoutSeconds>")]
        private static void StartVote(string timeoutStr)
        {
            if (!float.TryParse(timeoutStr, out float timeout) || timeout <= 0f)
            {
                UnityEngine.Debug.LogWarning(
                    $"[vote] Invalid timeout \"{timeoutStr}\". Must be a positive number."
                );
                return;
            }

            if (VoteManager.Instance == null)
            {
                UnityEngine.Debug.LogWarning("[vote] VoteManager is not initialised.");
                return;
            }

            VoteManager.Instance.StartVote(timeout);
        }

        /// <summary>
        /// Console command: spawnConfigUI
        /// Toggles the spawn configuration GUI panel open/closed.
        /// </summary>
        [CCommand("spawnConfigUI", "Open or close the Spawn Config GUI panel.")]
        private static void ToggleSpawnConfigUI()
        {
            if (SpawnConfigUI.Instance == null)
            {
                UnityEngine.Debug.LogWarning(
                    "[spawnConfigUI] SpawnConfigUI is not initialised yet."
                );
                return;
            }
            SpawnConfigUI.Instance.Toggle();
        }
    }
}
