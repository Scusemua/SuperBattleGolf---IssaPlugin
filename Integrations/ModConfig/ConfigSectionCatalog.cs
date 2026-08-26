using System.Collections.Generic;
using System.Linq;

namespace IssaPlugin.Integrations.ModConfigUI
{
    /// <summary>
    /// Maps friendly, human-readable group names (mostly item names) onto the raw
    /// config section strings used in <c>Config/*.cs</c>.
    ///
    /// Most items own exactly one section, but a few spread their settings over
    /// several (AC-130 has a separate Mayday section, Poison Jar has its overlay,
    /// Shape Shifter has its shape list), so this is deliberately one-to-many.
    /// Anything not listed here is still reachable — see <see cref="ResolveGroup"/>,
    /// which falls back to treating an unknown section as its own group so newly
    /// added items show up in the filter without needing an edit here.
    /// </summary>
    internal static class ConfigSectionCatalog
    {
        /// <summary>Group label shown when no filter is applied.</summary>
        public const string AllGroups = "All Settings";

        /// <summary>Ordered so non-item groups sort to the top of the dropdown.</summary>
        private static readonly List<KeyValuePair<string, string[]>> Groups =
            new List<KeyValuePair<string, string[]>>
            {
                // Cross-cutting groups first: these span every item rather than
                // belonging to one, so they are the most useful filter targets.
                Group("General", "IssaPlugin", "UI"),
                Group("Item Toggles", "ItemEnabled"),
                Group("Item Box Spawn Weights", "ItemBoxSpawns"),
                Group("Warnings", "Warnings", "ItemWarnings"),
                Group("Explosions", "Explosions"),
                Group("Diagnostics", "Diagnostics"),
                Group("Explosive Golf Balls", "ExplosiveGolfBalls"),

                Group("AC-130 Gunship", "AC130", "AC130Mayday"),
                Group("AK-47", "AK47"),
                Group("Baseball Bat", "BaseballBat"),
                Group("Bear", "Bear"),
                Group("Black Hole Grenade", "BlackHoleGrenade"),
                Group("Donut", "Donut"),
                Group("Drone Swarm", "DroneSwarm"),
                Group("Flamethrower", "Flamethrower"),
                Group("Freeze World", "FreezeWorld"),
                Group("Gravity Gun", "GravityGun"),
                Group("Harrier", "HarrierJet"),
                Group("Hunter Drone", "HunterDrone"),
                Group("Javelin", "Javelin"),
                Group("Jetpack", "Jetpack"),
                Group("Low Gravity", "LowGravity"),
                Group("Moon", "Moon"),
                Group("Nuke", "Nuke"),
                Group("Placeable Wall", "PlaceableWall"),
                Group("Poison Jar", "PoisonJar", "PoisonOverlay"),
                Group("Position Swap", "PositionSwap"),
                Group("Predator Missile", "PredatorMissile"),
                Group("Red Bull", "RedBull"),
                Group("Rocket Tether", "RocketTether"),
                Group("Rocket Tether Grenade", "RocketTetherGrenade"),
                Group("Shape Shifter", "ShapeShifter", "ShapeShifter.Shapes"),
                Group("Sniper Rifle", "SniperRifle"),
                Group("Spinach", "Spinach"),
                Group("Stealth Bomber", "StealthBomber"),
                Group("Sticky Grenade", "StickyGrenade"),
                Group("Super Donut", "SuperDonut"),
                Group("Super Shape Shifter", "SuperShapeShifter"),
                Group("Teleporter", "Teleporter"),
                Group("UFO Abduction", "UfoAbduction"),
                Group("Wind Storm", "WindStorm"),
            };

        private static KeyValuePair<string, string[]> Group(string label, params string[] sections) =>
            new KeyValuePair<string, string[]>(label, sections);

        /// <summary>Section name -> group label, built once from <see cref="Groups"/>.</summary>
        private static readonly Dictionary<string, string> SectionToGroup = BuildSectionLookup();

        private static Dictionary<string, string> BuildSectionLookup()
        {
            var map = new Dictionary<string, string>();
            foreach (var group in Groups)
            {
                foreach (var section in group.Value) map[section] = group.Key;
            }
            return map;
        }

        /// <summary>
        /// Returns the group a section belongs to. Unknown sections (a newly added
        /// item whose entry has not been added above yet) become their own group so
        /// they are never silently unreachable from the filter.
        /// </summary>
        public static string ResolveGroup(string section)
        {
            if (string.IsNullOrEmpty(section)) return "Other";
            return SectionToGroup.TryGetValue(section, out string group) ? group : section;
        }

        /// <summary>
        /// Builds the dropdown option list for the sections actually present in the
        /// live UI. Driven by what was rendered rather than by <see cref="Groups"/>
        /// alone, so the dropdown never offers a group with nothing behind it.
        /// </summary>
        public static List<string> BuildFilterOptions(IEnumerable<string> presentSections)
        {
            var present = new HashSet<string>(presentSections.Select(ResolveGroup));

            // Preserve the curated order above, then append any unrecognised groups.
            var ordered = Groups.Select(g => g.Key).Where(present.Contains).ToList();
            ordered.AddRange(present.Where(g => !ordered.Contains(g)).OrderBy(g => g));

            ordered.Insert(0, AllGroups);
            return ordered;
        }
    }
}
