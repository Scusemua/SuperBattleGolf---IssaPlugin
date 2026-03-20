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
            (new[] { "ac130" }, AC130Item.AC130ItemType, (int)Configuration.AC130Uses.Value),
            (
                new[] { "stealthbomber", "bomber" },
                StealthBomberItem.BomberItemType,
                (int)Configuration.BomberUses.Value
            ),
            (
                new[] { "predatormissile", "missile" },
                PredatorMissileItem.MissileItemType,
                (int)Configuration.MissileUses.Value
            ),
            (
                new[] { "baseballbat", "bat" },
                BatItem.BatItemType,
                (int)Configuration.BaseballBatUses.Value
            ),
            (
                new[] { "freezeworld", "freeze" },
                FreezeItem.FreezeItemType,
                (int)Configuration.FreezeUses.Value
            ),
            (
                new[] { "lowgravity", "gravity" },
                LowGravityItem.LowGravityItemType,
                (int)Configuration.LowGravityUses.Value
            ),
            (
                new[] { "m200", "sniper", "sniper_rifle", "intervention" },
                SniperRifleItem.SniperRifleItemType,
                (int)Configuration.SniperRifleUses.Value
            ),
            (
                new[] { "donut", "Donut" },
                DonutItem.DonutItemType,
                (int)Configuration.DonutUses.Value
            ),
            (
                new[] { "javelin", "Javelin" },
                JavelinItem.JavelinItemType,
                (int)Configuration.JavelinUses.Value
            ),
            (
                new[] { "sticky_grenade", "sticky", "stickygrenade" },
                StickyGrenadeItem.StickyGrenadeItemType,
                (int)Configuration.StickyGrenadeUses.Value
            ),
            (new[] { "bear", "bears" }, BearItem.BearItemType, (int)Configuration.BearUses.Value),
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
                    + "Valid names: ac130, bomber, missile, bat, freeze, lowgravity, sniper, donut, javelin, stickygrenade (or an integer item-type ID)."
            );
        }
    }
}
