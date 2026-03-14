using System;
using IssaPlugin.Items;

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
        // Accepted names for each item (all checked case-insensitively).
        private static readonly (string[] Names, ItemType Type, int Uses)[] _customItems =
        {
            (new[] { "ac130" }, AC130Item.AC130ItemType, 1),
            (new[] { "stealthbomber", "bomber" }, StealthBomberItem.BomberItemType, 1),
            (new[] { "predatormissile", "missile" }, PredatorMissileItem.MissileItemType, 1),
            (new[] { "baseballbat", "bat" }, BatItem.BatItemType, 1),
            (new[] { "freezeworld", "freeze" }, FreezeItem.FreezeItemType, 1),
            (new[] { "lowgravity", "gravity" }, LowGravityItem.LowGravityItemType, 1),
            (
                new[] { "m200", "sniper", "sniper_rifle", "intervention" },
                SniperRifleItem.SniperRifleItemType,
                1
            ),
            (new[] { "ufo", "UFO" }, UFOItem.UFOItemType, 1),
        };

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
                var numericType = (ItemType)id;
                if (!ItemRegistry.IsCustomItem(numericType))
                {
                    UnityEngine.Debug.LogWarning(
                        $"[giveCustomItem] Item ID {id} is not a recognised custom item."
                    );
                    return;
                }

                // Look up default uses from the table, default to 1.
                int uses = 1;
                foreach (var entry in _customItems)
                {
                    if (entry.Type == numericType)
                    {
                        uses = entry.Uses;
                        break;
                    }
                }

                ItemHelper.GiveItemToLocalPlayer(numericType, uses, "giveCustomItem");
                return;
            }

            // Named lookup.
            foreach (var (names, type, defaultUses) in _customItems)
            {
                foreach (var alias in names)
                {
                    if (string.Equals(itemName, alias, StringComparison.OrdinalIgnoreCase))
                    {
                        ItemHelper.GiveItemToLocalPlayer(type, defaultUses, "giveCustomItem");
                        return;
                    }
                }
            }

            UnityEngine.Debug.LogWarning(
                $"[giveCustomItem] Unknown item \"{itemName}\". "
                    + "Valid names: ac130, bomber, missile, bat, freeze, lowgravity, sniper, ufo (or an integer item-type ID)."
            );
        }
    }
}
