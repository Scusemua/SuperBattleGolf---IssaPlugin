using System.Collections.Generic;
using System.Linq;
using IssaPlugin.Items;

namespace IssaPlugin.Integrations.SpawnerUI
{
    /// <summary>
    /// Classifies every spawnable item by its source and provides the search index the
    /// spawner window filters on.
    ///
    /// Classification is exact rather than heuristic: <see cref="ItemRegistry.IsCustomItem"/>
    /// already answers "is this ours", and the base game's ItemType enum tops out well
    /// below our range (ours start at 100), so anything that is neither ours nor a
    /// defined base-game value came from some other mod.
    /// </summary>
    internal static class SpawnerItemCatalog
    {
        public const string AllSources = "All Items";
        public const string OurSource = "IssaPlugin";
        public const string BaseGameSource = "Base Game";
        public const string OtherSource = "Other Mods";

        /// <summary>One spawnable item plus everything the window needs to draw it.</summary>
        internal class Entry
        {
            public ItemData Data;
            public string DisplayName;
            public string Source;

            /// <summary>Lowercased name, matched against the search query.</summary>
            public string Haystack;
        }

        /// <summary>
        /// Builds the catalog from the game's item collection. Called when the window
        /// opens rather than per frame — the item set only changes when the game
        /// reloads its collection, and localized name lookups are not free.
        /// </summary>
        public static List<Entry> Build(IEnumerable<ItemData> items)
        {
            var entries = new List<Entry>();
            if (items == null) return entries;

            foreach (var item in items)
            {
                if (item == null) continue;

                string name = ResolveName(item);
                entries.Add(new Entry
                {
                    Data = item,
                    DisplayName = name,
                    Source = ResolveSource(item),
                    Haystack = name.ToLowerInvariant(),
                });
            }

            return entries;
        }

        /// <summary>
        /// Prefers our own DisplayName for custom items: it is a plain string, whereas
        /// the localized lookup can return a key (or throw) for entries the game's
        /// localization tables do not know about.
        /// </summary>
        private static string ResolveName(ItemData item)
        {
            var definition = ItemRegistry.GetDefinition(item.Type);
            if (definition != null) return definition.DisplayName;

            try
            {
                string localized = item.LocalizedName?.GetLocalizedString();
                if (!string.IsNullOrEmpty(localized)) return localized;
            }
            catch
            {
                // Fall through to the enum name below.
            }

            return item.Type.ToString();
        }

        private static string ResolveSource(ItemData item)
        {
            if (ItemRegistry.IsCustomItem(item.Type)) return OurSource;
            return System.Enum.IsDefined(typeof(ItemType), item.Type)
                ? BaseGameSource
                : OtherSource;
        }

        /// <summary>
        /// The source options to offer, limited to sources actually present so the
        /// dropdown never lists an empty bucket.
        /// </summary>
        public static List<string> BuildSourceOptions(IEnumerable<Entry> entries)
        {
            var present = new HashSet<string>(entries.Select(e => e.Source));
            var ordered = new List<string> { AllSources };

            // Ours first: it is what this window mainly exists to make findable.
            foreach (string source in new[] { OurSource, BaseGameSource, OtherSource })
            {
                if (present.Contains(source)) ordered.Add(source);
            }

            return ordered;
        }

        /// <summary>Applies the active source filter and search query.</summary>
        public static List<Entry> Filter(List<Entry> entries, string source, string query)
        {
            IEnumerable<Entry> result = entries;

            if (!string.IsNullOrEmpty(source) && source != AllSources)
                result = result.Where(e => e.Source == source);

            query = (query ?? string.Empty).Trim().ToLowerInvariant();
            if (query.Length > 0)
                result = result.Where(e => e.Haystack.Contains(query));

            return result.ToList();
        }
    }
}
