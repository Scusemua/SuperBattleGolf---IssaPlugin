using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IssaPlugin.Integrations.ModConfigUI
{
    /// <summary>
    /// Adds search, item filtering, and collapsible sections to the IssaPlugin page
    /// that ModConfig builds.
    ///
    /// This deliberately post-processes the GameObject hierarchy ModConfig already
    /// created rather than rebuilding any of it. ModConfig exposes no events or
    /// extension points, but it does name everything predictably
    /// (<c>SUBHEADING_{section}</c> / <c>CONTAINER_{section}</c>), so we locate those
    /// pairs and drive them with SetActive. If ModConfig changes its layout, we find
    /// nothing, log, and leave the stock UI untouched.
    /// </summary>
    internal class ConfigPageEnhancer : MonoBehaviour
    {
        /// <summary>One collapsible section: its heading, its rows, and its state.</summary>
        private class Section
        {
            public string Name;
            public string Group;
            public GameObject Heading;
            public GameObject Container;
            public TextMeshProUGUI HeadingText;

            /// <summary>Row object -> lowercased search haystack for that row.</summary>
            public List<KeyValuePair<GameObject, string>> Rows = new List<KeyValuePair<GameObject, string>>();

            public bool Expanded;

            /// <summary>True when the group filter alone would show this section.</summary>
            public bool MatchesGroup = true;
        }

        private readonly List<Section> _sections = new List<Section>();

        private RectTransform _contentRect;
        private string _search = string.Empty;
        private string _group = ConfigSectionCatalog.AllGroups;

        /// <summary>Collapse state to restore once a search is cleared.</summary>
        private readonly Dictionary<Section, bool> _preSearchState = new Dictionary<Section, bool>();
        private bool _searchActive;

        /// <summary>Shown when the current filter/search matches nothing.</summary>
        private TextMeshProUGUI _emptyState;

        /// <summary>
        /// Scans the page for ModConfig's section pairs. Returns false if the expected
        /// structure is missing, so the caller can bail out without altering anything.
        /// </summary>
        public bool Initialize(GameObject page)
        {
            Transform content = page.transform.Find("Viewport/Content");
            if (content == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[ModConfig] Could not find Viewport/Content on the IssaPlugin page; "
                        + "leaving the stock config UI untouched.");
                return false;
            }

            _contentRect = content as RectTransform;
            CollectSections(content);

            if (_sections.Count == 0)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[ModConfig] Found no SUBHEADING_/CONTAINER_ pairs on the IssaPlugin page; "
                        + "leaving the stock config UI untouched.");
                return false;
            }

            BuildControls(content);

            // Start fully collapsed: ~40 sections and 460+ entries is unusable expanded.
            foreach (var section in _sections) SetExpanded(section, false);
            ApplyFilters();

            IssaPluginPlugin.Log.LogInfo(
                $"[ModConfig] Enhanced IssaPlugin config page: {_sections.Count} sections, "
                    + $"{_sections.Sum(s => s.Rows.Count)} entries.");
            return true;
        }

        /// <summary>
        /// Walks the content children in order. ModConfig emits a SUBHEADING_ immediately
        /// followed by its CONTAINER_, so we pair them as we go.
        /// </summary>
        private void CollectSections(Transform content)
        {
            var containers = new Dictionary<string, GameObject>();
            foreach (Transform child in content)
            {
                if (child.name.StartsWith("CONTAINER_"))
                    containers[child.name.Substring("CONTAINER_".Length)] = child.gameObject;
            }

            foreach (Transform child in content)
            {
                if (!child.name.StartsWith("SUBHEADING_")) continue;

                string name = child.name.Substring("SUBHEADING_".Length);
                if (!containers.TryGetValue(name, out GameObject container)) continue;

                var section = new Section
                {
                    Name = name,
                    Group = ConfigSectionCatalog.ResolveGroup(name),
                    Heading = child.gameObject,
                    Container = container,
                    HeadingText = child.GetComponent<TextMeshProUGUI>(),
                };

                CollectRows(section);
                MakeHeadingClickable(section);
                _sections.Add(section);
            }
        }

        /// <summary>
        /// Indexes each row's searchable text. ModConfig names rows by widget type and
        /// config key (SLIDER_Foo, DROPDOWN_Bar, INPUT_Baz); we search the key plus the
        /// visible label, and include the section name so "bear" matches all of Bear.
        /// </summary>
        private void CollectRows(Section section)
        {
            foreach (Transform row in section.Container.transform)
            {
                string key = row.name;
                int underscore = key.IndexOf('_');
                if (underscore >= 0) key = key.Substring(underscore + 1);

                string label = string.Empty;
                Transform labelText = row.Find("Label Text");
                if (labelText != null)
                {
                    var tmp = labelText.GetComponent<TextMeshProUGUI>();
                    if (tmp != null) label = tmp.text;
                }

                string haystack = (key + " " + label + " " + section.Name + " " + section.Group)
                    .ToLowerInvariant();
                section.Rows.Add(new KeyValuePair<GameObject, string>(row.gameObject, haystack));
            }
        }

        /// <summary>Turns the section heading into a click target that toggles the section.</summary>
        private void MakeHeadingClickable(Section section)
        {
            if (section.HeadingText != null) section.HeadingText.raycastTarget = true;

            var trigger = section.Heading.GetComponent<EventTrigger>();
            if (trigger == null) trigger = section.Heading.AddComponent<EventTrigger>();

            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(_ =>
            {
                // While searching, the visible set is driven by the query; toggling
                // collapse there would fight the filter, so ignore clicks.
                if (_searchActive) return;

                SetExpanded(section, !section.Expanded);
                RebuildLayout();
            });
            trigger.triggers.Add(entry);
        }

        private void SetExpanded(Section section, bool expanded)
        {
            section.Expanded = expanded;
            section.Container.SetActive(expanded);
            UpdateHeadingText(section);
        }

        /// <summary>Prefixes the heading with a caret so collapse state is visible at a glance.</summary>
        private void UpdateHeadingText(Section section)
        {
            if (section.HeadingText == null) return;

            string arrow = section.Expanded ? "▼" : "▶";

            // Count the rows themselves rather than keying off the container: while a
            // search is filtering, a collapsed section must still report how many rows
            // matched, not its total.
            int shown = section.Rows.Count(r => r.Key.activeSelf);
            string count = shown == section.Rows.Count
                ? shown.ToString()
                : $"{shown}/{section.Rows.Count}";

            section.HeadingText.text = $"{arrow}  {section.Name}  ({count})";
        }

        /// <summary>Applies the current group filter and search query to every section.</summary>
        private void ApplyFilters()
        {
            string query = _search.Trim().ToLowerInvariant();
            bool searching = query.Length > 0;

            // Entering a search: remember collapse state so clearing restores it. The map
            // is cleared on the way out, so an empty map is exactly "not yet captured" —
            // this stays correct across repeated keystrokes without re-capturing the
            // search-driven state over the user's real one.
            if (searching && _preSearchState.Count == 0)
            {
                foreach (var section in _sections) _preSearchState[section] = section.Expanded;
            }

            foreach (var section in _sections)
            {
                section.MatchesGroup =
                    _group == ConfigSectionCatalog.AllGroups || section.Group == _group;

                if (!section.MatchesGroup)
                {
                    section.Heading.SetActive(false);
                    section.Container.SetActive(false);
                    continue;
                }

                if (!searching)
                {
                    // Restore every row, then the collapse state from before the search.
                    // section.Expanded is not trustworthy here: while searching it is
                    // driven by whether the section matched, so the remembered map is
                    // the only record of what the user had open.
                    foreach (var row in section.Rows) row.Key.SetActive(true);

                    bool expanded = _preSearchState.TryGetValue(section, out bool prev)
                        ? prev
                        : section.Expanded;

                    section.Heading.SetActive(true);
                    SetExpanded(section, expanded);
                    continue;
                }

                // Searching: a section-name hit shows the whole section, otherwise
                // only the matching rows, and matches auto-expand.
                bool sectionHit = section.Name.ToLowerInvariant().Contains(query)
                    || section.Group.ToLowerInvariant().Contains(query);

                int matches = 0;
                foreach (var row in section.Rows)
                {
                    bool hit = sectionHit || row.Value.Contains(query);
                    row.Key.SetActive(hit);
                    if (hit) matches++;
                }

                bool anyMatch = matches > 0;
                section.Heading.SetActive(anyMatch);
                section.Container.SetActive(anyMatch);
                section.Expanded = anyMatch;
                UpdateHeadingText(section);
            }

            // Once the remembered state has been restored, drop it so later manual
            // collapsing is not overridden by a stale entry on the next filter pass.
            if (!searching) _preSearchState.Clear();

            UpdateEmptyState(query);

            _searchActive = searching;
            RebuildLayout();
        }

        /// <summary>
        /// Shows an explanation when the current filter hides everything, so a blank
        /// page never looks like a broken one.
        /// </summary>
        private void UpdateEmptyState(string query)
        {
            if (_emptyState == null) return;

            bool anyVisible = _sections.Any(s => s.Heading.activeSelf);
            _emptyState.gameObject.SetActive(!anyVisible);
            if (anyVisible) return;

            bool filtered = _group != ConfigSectionCatalog.AllGroups;

            if (query.Length > 0 && filtered)
                _emptyState.text = $"No settings match \"{query}\" in {_group}.";
            else if (query.Length > 0)
                _emptyState.text = $"No settings match \"{query}\".";
            else
                _emptyState.text = $"No settings in {_group}.";
        }

        /// <summary>
        /// Forces a layout pass. VerticalLayoutGroup + ContentSizeFitter do not always
        /// recompute after mass SetActive toggles, which leaves the scroll height stale.
        /// </summary>
        private void RebuildLayout()
        {
            if (_contentRect != null) LayoutRebuilder.MarkLayoutForRebuild(_contentRect);
            Canvas.ForceUpdateCanvases();
        }

        // ── Control strip ────────────────────────────────────────────────────

        /// <summary>
        /// Builds the search box, filter dropdown, and expand/collapse buttons, pinned
        /// as the first children of the scroll content so they sit above the sections.
        /// </summary>
        private void BuildControls(Transform content)
        {
            GameObject strip = ConfigControlsFactory.CreateControlStrip(content);

            ConfigControlsFactory.CreateSearchField(
                strip.transform,
                "Search settings...",
                value =>
                {
                    _search = value ?? string.Empty;
                    ApplyFilters();
                });

            var options = ConfigSectionCatalog.BuildFilterOptions(_sections.Select(s => s.Name));
            ConfigControlsFactory.CreateFilterDropdown(
                strip.transform,
                options,
                index =>
                {
                    _group = index >= 0 && index < options.Count
                        ? options[index]
                        : ConfigSectionCatalog.AllGroups;
                    ApplyFilters();
                });

            ConfigControlsFactory.CreateButtonRow(
                strip.transform,
                onExpandAll: () => SetAllExpanded(true),
                onCollapseAll: () => SetAllExpanded(false));

            // Appended to the content root (not the strip) so it reads as page body
            // text. Created after CollectSections, so it is never indexed as a row.
            _emptyState = ConfigControlsFactory.CreateEmptyStateLabel(content);
        }

        private void SetAllExpanded(bool expanded)
        {
            if (_searchActive) return;

            foreach (var section in _sections)
            {
                if (section.MatchesGroup) SetExpanded(section, expanded);
            }
            RebuildLayout();
        }
    }
}
