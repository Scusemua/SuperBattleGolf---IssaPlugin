using System.Collections.Generic;
using System.Linq;
using IssaPlugin.Items;
using NUnit.Framework;

namespace IssaPlugin.Tests
{
    /// <summary>
    /// Tests for the structural invariants of ItemRegistry.
    ///
    /// These tests deliberately avoid any property that touches Unity assets
    /// (Icon, HeldModelPrefab) or BepInEx configuration (MaxUses, SpawnWeight,
    /// GiveKey) — those require a live Unity/BepInEx runtime and are not
    /// reachable in a standalone test runner.
    ///
    /// What IS safe to test here:
    ///   • The ItemType IDs assigned in ItemRegistry are unique and in range.
    ///   • AllItems and CustomItemDefinitionMap agree on membership.
    ///   • GetDefinition returns the right object (or null for unknowns).
    ///   • IsCustomItem / GetMaxUses don't crash for custom vs. built-in types.
    ///   • Each definition reports its own ItemType correctly (no copy-paste bug).
    ///   • DisplayName and ConsoleAliases are non-empty on every definition.
    /// </summary>
    [TestFixture]
    public class ItemRegistryTests
    {
        // The custom item ID range used by this mod (100–117 as of this version).
        private const int CustomIdRangeStart = 100;
        private const int CustomIdRangeEnd = 200; // upper bound for sanity check

        // ── AllItems presence ─────────────────────────────────────────────────

        [Test]
        public void AllItems_IsNotEmpty()
        {
            Assert.That(
                ItemRegistry.AllItems,
                Is.Not.Empty,
                "AllItems must contain at least one custom item definition."
            );
        }

        [Test]
        public void AllItems_CountMatchesExpectedRegistrations()
        {
            // 18 items were registered at the time these tests were written.
            // If this fails, a definition was added or removed without updating
            // the tests — that is intentional: the test acts as a registry
            // change detector.
            Assert.That(
                ItemRegistry.AllItems.Count,
                Is.EqualTo(18),
                "Expected 18 custom item definitions. Update this constant if items were added/removed."
            );
        }

        // ── ItemType ID uniqueness ────────────────────────────────────────────

        [Test]
        public void AllItems_HaveUniqueItemTypeIds()
        {
            var ids = ItemRegistry.AllItems.Select(d => (int)d.ItemType).ToList();
            var distinct = ids.Distinct().ToList();

            Assert.That(
                distinct.Count,
                Is.EqualTo(ids.Count),
                "Two or more item definitions share the same ItemType ID. "
                    + "IDs in the registry: "
                    + string.Join(", ", ids)
            );
        }

        [Test]
        public void AllItems_ItemTypeIdsAreInExpectedRange()
        {
            foreach (var def in ItemRegistry.AllItems)
            {
                int id = (int)def.ItemType;
                Assert.That(
                    id,
                    Is.InRange(CustomIdRangeStart, CustomIdRangeEnd),
                    $"ItemType ID {id} for '{def.DisplayName}' is outside the expected "
                        + $"custom-item range [{CustomIdRangeStart}, {CustomIdRangeEnd}]."
                );
            }
        }

        // ── ItemType static constants ─────────────────────────────────────────

        [Test]
        public void StaticItemTypeConstants_AreUniqueAcrossEachOther()
        {
            // The static fields like AC130ItemType, BearItemType, etc. must all
            // be distinct — even before any definition objects are created.
            var constants = new[]
            {
                (int)ItemRegistry.BaseballBatItemType,
                (int)ItemRegistry.StealthBomberItemType,
                (int)ItemRegistry.PredatorMissileItemType,
                (int)ItemRegistry.AC130ItemType,
                (int)ItemRegistry.FreezeItemType,
                (int)ItemRegistry.LowGravityItemType,
                (int)ItemRegistry.SniperRifleItemType,
                (int)ItemRegistry.DonutItemType,
                (int)ItemRegistry.JavelinItemType,
                (int)ItemRegistry.StickyGrenadeItemType,
                (int)ItemRegistry.BearItemType,
                (int)ItemRegistry.NukeItemType,
                (int)ItemRegistry.BlackHoleGrenadeItemType,
                (int)ItemRegistry.PlaceableWallItemType,
                (int)ItemRegistry.AK47ItemType,
                (int)ItemRegistry.HarrierItemType,
                (int)ItemRegistry.PositionSwapItemType,
                (int)ItemRegistry.PoisonJarItemType,
            };

            Assert.That(
                constants.Distinct().Count(),
                Is.EqualTo(constants.Length),
                "Static ItemType constants in ItemRegistry must all be unique."
            );
        }

        [Test]
        public void StaticItemTypeConstants_MatchDefinitionItemTypes()
        {
            // Each definition's ItemType must equal the corresponding registry constant.
            // Detects copy-paste bugs like a new definition accidentally returning the
            // wrong constant's value.
            var constantMap = new Dictionary<string, int>
            {
                ["Baseball Bat"] = (int)ItemRegistry.BaseballBatItemType,
                ["Stealth Bomber"] = (int)ItemRegistry.StealthBomberItemType,
                ["Predator Missile"] = (int)ItemRegistry.PredatorMissileItemType,
                ["AC130 Gunship"] = (int)ItemRegistry.AC130ItemType,
                ["Freeze"] = (int)ItemRegistry.FreezeItemType,
                ["Low Gravity"] = (int)ItemRegistry.LowGravityItemType,
                ["Sniper Rifle"] = (int)ItemRegistry.SniperRifleItemType,
                ["Donut"] = (int)ItemRegistry.DonutItemType,
                ["Javelin"] = (int)ItemRegistry.JavelinItemType,
                ["Sticky Grenade"] = (int)ItemRegistry.StickyGrenadeItemType,
                ["Bear"] = (int)ItemRegistry.BearItemType,
                ["Nuke"] = (int)ItemRegistry.NukeItemType,
                ["Black Hole Grenade"] = (int)ItemRegistry.BlackHoleGrenadeItemType,
                ["Placeable Wall"] = (int)ItemRegistry.PlaceableWallItemType,
                ["AK47"] = (int)ItemRegistry.AK47ItemType,
                ["Harrier"] = (int)ItemRegistry.HarrierItemType,
                ["Position Swap"] = (int)ItemRegistry.PositionSwapItemType,
                ["Poison Jar"] = (int)ItemRegistry.PoisonJarItemType,
            };

            foreach (var def in ItemRegistry.AllItems)
            {
                if (!constantMap.TryGetValue(def.DisplayName, out int expectedId))
                    continue; // New item not in the map — handled by the uniqueness test.

                Assert.That(
                    (int)def.ItemType,
                    Is.EqualTo(expectedId),
                    $"'{def.DisplayName}' reports ItemType {(int)def.ItemType} but "
                        + $"the registry constant says {expectedId}."
                );
            }
        }

        // ── CustomItemDefinitionMap ───────────────────────────────────────────

        [Test]
        public void CustomItemDefinitionMap_HasSameCountAsAllItems()
        {
            Assert.That(
                ItemRegistry.CustomItemDefinitionMap.Count,
                Is.EqualTo(ItemRegistry.AllItems.Count),
                "The lazy dictionary must contain exactly one entry per AllItems element."
            );
        }

        [Test]
        public void CustomItemDefinitionMap_ContainsAllItemTypeKeys()
        {
            foreach (var def in ItemRegistry.AllItems)
            {
                Assert.That(
                    ItemRegistry.CustomItemDefinitionMap.ContainsKey((int)def.ItemType),
                    Is.True,
                    $"CustomItemDefinitionMap must contain a key for ItemType {(int)def.ItemType} ({def.DisplayName})."
                );
            }
        }

        // ── GetDefinition ─────────────────────────────────────────────────────

        [Test]
        public void GetDefinition_KnownType_ReturnsCorrectDefinition()
        {
            var def = ItemRegistry.GetDefinition(ItemRegistry.AC130ItemType);

            Assert.That(
                def,
                Is.Not.Null,
                "GetDefinition must return a non-null result for a registered type."
            );
            Assert.That(
                def!.ItemType,
                Is.EqualTo(ItemRegistry.AC130ItemType),
                "The returned definition must have the same ItemType that was requested."
            );
        }

        [Test]
        public void GetDefinition_UnknownType_ReturnsNull()
        {
            // A vanilla game ItemType (e.g. 1) should not be in the custom map.
            var vanillaType = (ItemType)1;

            Assert.That(
                ItemRegistry.GetDefinition(vanillaType),
                Is.Null,
                "GetDefinition must return null for an unregistered (vanilla) ItemType."
            );
        }

        [Test]
        public void GetDefinition_ForEachRegisteredType_ReturnsNonNull()
        {
            foreach (var def in ItemRegistry.AllItems)
            {
                var fetched = ItemRegistry.GetDefinition(def.ItemType);
                Assert.That(
                    fetched,
                    Is.Not.Null,
                    $"GetDefinition returned null for registered type {(int)def.ItemType} ({def.DisplayName})."
                );
            }
        }

        // ── IsCustomItem ──────────────────────────────────────────────────────

        [Test]
        public void IsCustomItem_RegisteredType_ReturnsTrue()
        {
            foreach (var def in ItemRegistry.AllItems)
            {
                Assert.That(
                    ItemRegistry.IsCustomItem(def.ItemType),
                    Is.True,
                    $"IsCustomItem must return true for registered type {(int)def.ItemType}."
                );
            }
        }

        [Test]
        public void IsCustomItem_VanillaType_ReturnsFalse()
        {
            var vanillaType = (ItemType)1;
            Assert.That(
                ItemRegistry.IsCustomItem(vanillaType),
                Is.False,
                "IsCustomItem must return false for a non-custom ItemType."
            );
        }

        // ── DisplayName / ConsoleAliases sanity ───────────────────────────────

        [Test]
        public void AllItems_DisplayName_IsNotNullOrWhitespace()
        {
            foreach (var def in ItemRegistry.AllItems)
            {
                Assert.That(
                    string.IsNullOrWhiteSpace(def.DisplayName),
                    Is.False,
                    $"DisplayName must not be null or empty (ItemType {(int)def.ItemType})."
                );
            }
        }

        [Test]
        public void AllItems_ConsoleAliases_IsNotNullOrEmpty()
        {
            foreach (var def in ItemRegistry.AllItems)
            {
                Assert.That(
                    def.ConsoleAliases,
                    Is.Not.Null,
                    $"ConsoleAliases must not be null (ItemType {(int)def.ItemType})."
                );
                Assert.That(
                    def.ConsoleAliases.Length,
                    Is.GreaterThan(0),
                    $"ConsoleAliases must have at least one entry ({def.DisplayName})."
                );
            }
        }

        [Test]
        public void AllItems_ConsoleAliases_ContainNoNullOrEmptyEntries()
        {
            foreach (var def in ItemRegistry.AllItems)
            {
                foreach (var alias in def.ConsoleAliases)
                {
                    Assert.That(
                        string.IsNullOrWhiteSpace(alias),
                        Is.False,
                        $"Alias entry in '{def.DisplayName}' is null or whitespace."
                    );
                }
            }
        }

        [Test]
        public void AllItems_DisplayNames_AreUnique()
        {
            var names = ItemRegistry.AllItems.Select(d => d.DisplayName).ToList();
            var distinct = names.Distinct().ToList();

            Assert.That(
                distinct.Count,
                Is.EqualTo(names.Count),
                "Two or more item definitions share the same DisplayName: "
                    + string.Join(
                        ", ",
                        names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key)
                    )
            );
        }

        // ── UseRocketIconFallback default ─────────────────────────────────────

        [Test]
        public void UseRocketIconFallback_DefaultIsTrue_ForMostItems()
        {
            // Items that are known to use the pistol fallback (UseRocketIconFallback = false).
            var pistolFallbackNames = new HashSet<string>
            {
                "Baseball Bat",
                "M200 Intervention",
                "AK47",
            };

            foreach (var def in ItemRegistry.AllItems)
            {
                if (pistolFallbackNames.Contains(def.DisplayName))
                    Assert.That(
                        def.UseRocketIconFallback,
                        Is.False,
                        $"'{def.DisplayName}' is expected to use the pistol icon fallback (UseRocketIconFallback=false)."
                    );
                else
                    Assert.That(
                        def.UseRocketIconFallback,
                        Is.True,
                        $"'{def.DisplayName}' is expected to use the rocket icon fallback (UseRocketIconFallback=true)."
                    );
            }
        }
    }
}
