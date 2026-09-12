// SpawnConfigUI.cs
// An IMGUI overlay that lets the host edit all spawn configuration at runtime.
//
// ── Opening the panel ─────────────────────────────────────────────────────────
//   Press the hotkey bound to ModConfig.Global.SpawnConfigUIKey (default: M).
//   Alternatively, run the console command "spawnConfigUI".
//
// ── Host vs client ────────────────────────────────────────────────────────────
//   Host  → full editing controls. "Apply" persists to BepInEx cfg + broadcasts.
//   Client → read-only display of the most recently received values.
//
// ── Layout overview ───────────────────────────────────────────────────────────
//   ┌─ Item Spawn Config ──────────────────────────────────────────────────────┐
//   │  [×]  [ ] Custom items enabled   Global rate: [0.50]                     │
//   │  ──────────────────────────────────────────────────────────────────────  │
//   │  Item Name          On   Lead  Beh.50  Beh.125  Beh.200  Ahead  Mob.     │ 
//   │  Baseball Bat       [✓]  [15]  [15]    [15]     [15]     [15]   [15]     │
//   │  ...                                                                     │
//   │  ──────────────────────────────────────────────────────────────────────  │
//   │  [Cancel]   [Apply & Sync]                                               │
//   └──────────────────────────────────────────────────────────────────────────┘

using System.Collections.Generic;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Overlays
{
    public class SpawnConfigUI : MonoBehaviour
    {
        // ── Singleton ──────────────────────────────────────────────────────────
        public static SpawnConfigUI Instance { get; private set; }

        // ── Visibility ─────────────────────────────────────────────────────────
        private bool _visible;

        // ── Working copy of the config ─────────────────────────────────────────
        private bool _workingEnabled;
        private float _workingRate;
        private bool[] _workingItemEnabled; // [itemIndex] into ItemRegistry.AllItems
        private float[,] _workingPoolWeights; // [itemIndex, gamePoolIndex 0-5]

        // IMGUI text field buffers — keyed by "{itemIndex}_{gamePoolIndex}" for pool
        // weight fields, and "rate" for the global rate field.
        // Required because IMGUI TextField resets cursor position on every frame
        // without a per-field string buffer.
        private readonly Dictionary<string, string> _textBuffers = new();

        // ── UI state ──────────────────────────────────────────────────────────
        private Vector2 _scrollPos;

        // ── Resolution scaling ────────────────────────────────────────────────
        // Every dimension in this file is authored against a 1080p-tall screen and
        // multiplied by the current scale at use, so the panel keeps the same apparent
        // size on a 1440p or 4K display instead of shrinking to unreadable pixels.
        //
        // Scaled per-dimension rather than through a GUI.matrix transform because the
        // panel is interactive: GUI.Window's drag, the text-field carets, and the scroll
        // view all hit-test in unscaled screen space, and a matrix scale would draw the
        // controls away from their own hitboxes.
        private const float ReferenceHeight = 1080f;

        // Window size at the reference height.
        private const float WindowWidth = 1360f;
        private const float WindowHeight = 740f;

        // Column widths. The item rows and the tier headers have to line up, so these
        // are shared rather than repeated as literals at each call site.
        private const float ColIconWidth = 28f;
        private const float ColNameWidth = 128f;
        private const float ColToggleWidth = 20f;
        private const float ColWeightWidth = 50f;
        private const float ColHeaderWeightWidth = 58f;
        private const float ItemRowHeight = 36f;
        private const float TierRowHeight = 32f;
        private const float ControlHeight = 28f;
        private const float TierControlHeight = 26f;

        // Font sizes, also at the reference height.
        private const int HeaderFontSize = 15;
        private const int ColHeaderFontSize = 11;
        private const int TooltipFontSize = 12;

        /// <summary>
        /// Scale from the reference layout to this screen. Shares
        /// ModConfig.Global.SpawnerUiScale with the item spawner window so both panels
        /// size consistently: a configured value wins, 0 (the default) derives it from
        /// screen height.
        ///
        /// Only height is considered, on purpose. An ultra-wide monitor is wide but no
        /// taller than an ordinary one of the same vertical resolution, so scaling by
        /// width would inflate the panel past the screen height on a 32:9 display.
        /// Never scales below 1: the layout has hand-tuned column widths that stop
        /// lining up when shrunk.
        /// </summary>
        private static float CurrentScale
        {
            get
            {
                float configured = ModConfig.Global.SpawnerUiScale.Value;
                if (configured > 0f)
                    return configured;

                return Mathf.Max(1f, Screen.height / ReferenceHeight);
            }
        }

        /// <summary>
        /// The scale for the frame being drawn. Sampled once per OnGUI so every pass of
        /// an IMGUI frame (layout, repaint, input) uses an identical value -- if layout
        /// and repaint disagreed, controls would be drawn off their own hitboxes.
        /// </summary>
        private float _scale = 1f;

        /// <summary>Scale the styles were last built at; a change rebuilds them.</summary>
        private float _styleScale;

        // Window rect — draggable
        private Rect _windowRect = new Rect(40, 40, WindowWidth, WindowHeight);

        // Column display order: (gamePoolIndex, header label)
        // Matches the base game pause menu order (Lead, Beh50, Beh125, Beh200, Ahead, Mob).
        private static readonly (int pool, string label)[] PoolColumns =
        {
            (GlobalConfig.PoolLead, "Lead"),
            (GlobalConfig.PoolBehind50, "Beh.50"),
            (GlobalConfig.PoolBehind125, "Beh.125"),
            (GlobalConfig.PoolBehind200, "Beh.200"),
            (GlobalConfig.PoolAhead, "Ahead"),
            (GlobalConfig.PoolMobility, "Mob."),
        };

        // Tier groupings — purely a UI concept, no effect on stored config.
        // Items are bucketed by DefaultPoolWeight. The defaultWeight here
        // is also the initial value shown in the tier batch-set fields.
        private static readonly (string label, float defaultWeight)[] TierDefs =
        {
            ("Common", 15f),
            ("Uncommon", 10f),
            ("Rare", 5f),
            ("Epic", 3f),
            ("Legendary", 1f),
        };

        // [itemIndex] → TierDefs index.  Populated in InitWorkingCopy.
        private int[] _itemTierIndex;

        // [tierIndex] → whether that tier section is expanded in the scroll view.
        // Preserved across Open/Close so the user's layout preference sticks.
        private bool[] _tierExpanded;

        // Styles (initialised lazily on first OnGUI call)
        private GUIStyle _styleHeader;
        private GUIStyle _styleItemRow;
        private GUIStyle _styleTierHeader;
        private GUIStyle _styleIconBox;
        private GUIStyle _styleReadOnly;
        private GUIStyle _styleColHeader;
        private GUIStyle _styleTooltip;

        // Scaled copies of the bare skin styles, for the controls that previously used
        // GUI.skin.* implicitly. Held here rather than mutating the shared skin, which
        // every other IMGUI overlay in the mod also draws from.
        private GUIStyle _styleButton;
        private GUIStyle _styleTextField;
        private GUIStyle _styleLabel;
        private GUIStyle _styleToggle;
        private bool _stylesInitialised;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[ModConfig.Global.SpawnConfigUIKey.Value].wasPressedThisFrame)
                Toggle();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void Toggle()
        {
            if (_visible)
                Close();
            else
                Open();
        }

        public void Open()
        {
            _visible = true;
            InitWorkingCopy();
            IssaPluginPlugin.Log.LogInfo("[SpawnConfigUI] Opened.");
        }

        public void Close()
        {
            _visible = false;
            IssaPluginPlugin.Log.LogInfo("[SpawnConfigUI] Closed.");
        }

        // ── Working copy ──────────────────────────────────────────────────────

        private void InitWorkingCopy()
        {
            _workingEnabled = ModConfig.Global.CustomItemSpawnsEnabled.Value;
            _workingRate = ModConfig.Global.CustomItemSpawnRate.Value;
            var items = ItemRegistry.AllItems;
            _workingItemEnabled = new bool[items.Count];
            _workingPoolWeights = new float[items.Count, 6];
            _textBuffers.Clear();
            _textBuffers["rate"] = _workingRate.ToString("F2");
            for (int i = 0; i < items.Count; i++)
            {
                _workingItemEnabled[i] = items[i].Enabled;
                for (int p = 0; p < 6; p++)
                {
                    _workingPoolWeights[i, p] = ModConfig.GetItemPoolWeight(items[i].ItemType, p);
                    _textBuffers[$"{i}_{p}"] = _workingPoolWeights[i, p].ToString("F1");
                }
            }

            // Compute tier index for each item and seed tier batch-set text buffers.
            _itemTierIndex = new int[items.Count];
            for (int i = 0; i < items.Count; i++)
                _itemTierIndex[i] = GetTierIndex(items[i].DefaultPoolWeight);

            // Preserve expansion state across opens; default to all expanded.
            if (_tierExpanded == null || _tierExpanded.Length != TierDefs.Length)
            {
                _tierExpanded = new bool[TierDefs.Length];
                for (int t = 0; t < TierDefs.Length; t++)
                    _tierExpanded[t] = true;
            }

            // Seed tier batch-set buffers from the first item in each tier (so the
            // field shows a meaningful starting value rather than the bare default).
            for (int t = 0; t < TierDefs.Length; t++)
            {
                for (int p = 0; p < 6; p++)
                {
                    float seed = TierDefs[t].defaultWeight;
                    for (int i = 0; i < items.Count; i++)
                        if (_itemTierIndex[i] == t)
                        {
                            seed = _workingPoolWeights[i, p];
                            break;
                        }
                    _textBuffers[$"t{t}_{p}"] = seed.ToString("F1");
                }
            }
        }

        /// <summary>Maps a DefaultPoolWeight to a TierDefs index (0 = Common … 4 = Legendary).</summary>
        private static int GetTierIndex(float defaultWeight)
        {
            for (int t = 0; t < TierDefs.Length - 1; t++)
                if (defaultWeight >= TierDefs[t].defaultWeight)
                    return t;
            return TierDefs.Length - 1;
        }

        private void ResetToDefaults()
        {
            _workingEnabled = true;
            _workingRate = 0.5f;
            var items = ItemRegistry.AllItems;
            _textBuffers["rate"] = _workingRate.ToString("F2");
            for (int i = 0; i < items.Count; i++)
            {
                _workingItemEnabled[i] = true;
                for (int p = 0; p < 6; p++)
                {
                    _workingPoolWeights[i, p] = items[i].GetDefaultPoolWeight(p);
                    _textBuffers[$"{i}_{p}"] = _workingPoolWeights[i, p].ToString("F1");
                }
            }
            // Re-seed tier batch-set buffers from the new per-item values.
            for (int t = 0; t < TierDefs.Length; t++)
            for (int p = 0; p < 6; p++)
            {
                float seed = TierDefs[t].defaultWeight;
                for (int i = 0; i < items.Count; i++)
                    if (_itemTierIndex[i] == t)
                    {
                        seed = _workingPoolWeights[i, p];
                        break;
                    }
                _textBuffers[$"t{t}_{p}"] = seed.ToString("F1");
            }
        }

        private void ApplyAndSync()
        {
            ModConfig.Global.CustomItemSpawnsEnabled.Value = _workingEnabled;
            ModConfig.Global.CustomItemSpawnRate.Value = _workingRate;
            var items = ItemRegistry.AllItems;
            for (int i = 0; i < items.Count; i++)
            {
                ModConfig.SetItemEnabled(items[i].ItemType, _workingItemEnabled[i]);
                for (int p = 0; p < 6; p++)
                    ModConfig.SetItemPoolWeight(items[i].ItemType, p, _workingPoolWeights[i, p]);
            }
            SpawnWeightsSyncer.ForceServerSync();
            // Immediately push all ConfigEntry values (including the new per-pool weights and
            // CustomItemSpawnRate) to clients so they don't need to wait for the next
            // 5-second ItemConfigSyncer tick before their local config reflects the change.
            // ForceBroadcast bypasses the change guard so the edit always goes out now.
            ItemConfigSyncer.ForceBroadcast();
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_visible || _workingItemEnabled == null)
                return;

            // One sample per frame, used by every pass. Reading the scale separately in
            // layout and repaint would let a mid-frame resolution change split them.
            _scale = CurrentScale;

            if (!_stylesInitialised || !Mathf.Approximately(_styleScale, _scale))
                InitStyles();

            // Block game input while the panel is open.
            if (Event.current.type == EventType.KeyDown || Event.current.type == EventType.KeyUp)
                Event.current.Use();

            // Cap at the screen. This panel is already 1360x740 at 1x, so on a 1080p
            // display a 2x scale would run well off both edges; clamping keeps the
            // bottom action bar (Cancel / Reset / Apply) reachable.
            _windowRect.width = Mathf.Min(WindowWidth * _scale, Screen.width);
            _windowRect.height = Mathf.Min(WindowHeight * _scale, Screen.height);

            _windowRect = GUI.Window(0xCA7C0, _windowRect, DrawWindow, "");

            // Keep it on screen: it can otherwise be dragged almost entirely off.
            float edge = 80f * _scale;
            _windowRect.x = Mathf.Clamp(
                _windowRect.x, -_windowRect.width + edge, Screen.width - edge);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Screen.height - (40f * _scale));
        }

        private void DrawWindow(int id)
        {
            bool isHost = NetworkServer.active;

            // ── Window header ─────────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Label("⚙  Item Spawn Config", _styleHeader);
            GUILayout.FlexibleSpace();
            if (!isHost)
                GUILayout.Label("  ⚠ Read-only (host controls config)", _styleReadOnly);
            if (GUILayout.Button("✕ Close", _styleButton, GUILayout.Width(80f * _scale)))
                Close();
            GUILayout.EndHorizontal();

            GUILayout.Space(4f * _scale);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1f * _scale));
            GUILayout.Space(4f * _scale);

            // ── Global toggles row ────────────────────────────────────────────
            GUILayout.BeginHorizontal();

            GUI.enabled = isHost;
            bool newEnabled = GUILayout.Toggle(
                _workingEnabled,
                new GUIContent(
                    " Custom items enabled",
                    "Master switch for all custom items. When off, no custom items will appear in the item pool."
                ),
                _styleToggle,
                GUILayout.Width(200f * _scale)
            );
            if (isHost)
                _workingEnabled = newEnabled;

            GUILayout.Space(20f * _scale);
            GUILayout.Label(
                new GUIContent(
                    "Global rate multiplier:",
                    "Scales the spawn weight of ALL custom items. 1.0 = normal, 0.5 = half as frequent."
                ),
                _styleLabel,
                GUILayout.Width(160f * _scale)
            );
            _workingRate = DrawFloatField("rate", _workingRate, isHost, 0f, 10f, 70f * _scale);

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(8f * _scale);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1f * _scale));
            GUILayout.Space(4f * _scale);

            // ── Column header row ─────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Label("Item Name", _styleColHeader, GUILayout.Width(160f * _scale));
            GUILayout.Label("On", _styleColHeader, GUILayout.Width(30f * _scale));
            GUILayout.Space(4f * _scale);
            foreach (var (_, label) in PoolColumns)
                GUILayout.Label(
                    label, _styleColHeader, GUILayout.Width(ColHeaderWeightWidth * _scale));
            GUILayout.EndHorizontal();

            GUILayout.Space(2f * _scale);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1f * _scale));
            GUILayout.Space(2f * _scale);

            // ── Item rows grouped by tier (scrollable) ────────────────────────
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            var items = ItemRegistry.AllItems;
            for (int t = 0; t < TierDefs.Length; t++)
            {
                // Skip tiers with no items.
                bool hasTierItems = false;
                for (int i = 0; i < items.Count; i++)
                    if (_itemTierIndex[i] == t)
                    {
                        hasTierItems = true;
                        break;
                    }
                if (!hasTierItems)
                    continue;

                DrawTierHeader(t, items, isHost);

                if (_tierExpanded[t])
                    for (int i = 0; i < items.Count; i++)
                        if (_itemTierIndex[i] == t)
                            DrawItemRow(i, items[i], isHost);
            }

            GUILayout.EndScrollView();

            // ── Bottom action bar ─────────────────────────────────────────────
            GUILayout.Space(4f * _scale);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1f * _scale));
            GUILayout.Space(4f * _scale);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (
                GUILayout.Button(
                    new GUIContent("Cancel", "Discard unsaved changes and close."),
                    _styleButton,
                    GUILayout.Width(100f * _scale),
                    GUILayout.Height(ControlHeight * _scale)
                )
            )
                Close();

            GUILayout.Space(10f * _scale);

            GUI.enabled = isHost;
            GUI.backgroundColor = isHost ? new Color(0.9f, 0.6f, 0.2f) : Color.white;
            if (
                GUILayout.Button(
                    new GUIContent(
                        "↺  Reset to Defaults",
                        isHost
                            ? "Reset all weights, enabled flags, and global rate to their coded defaults. Does not save until you click Apply & Sync."
                            : "Only the host can reset values."
                    ),
                    _styleButton,
                    GUILayout.Width(160f * _scale),
                    GUILayout.Height(ControlHeight * _scale)
                )
            )
                ResetToDefaults();
            GUI.backgroundColor = Color.white;

            GUILayout.Space(10f * _scale);

            GUI.enabled = isHost;
            GUI.backgroundColor = isHost ? new Color(0.3f, 0.9f, 0.3f) : Color.white;
            if (
                GUILayout.Button(
                    new GUIContent(
                        "✔  Apply & Sync",
                        isHost
                            ? "Save changes and broadcast to all clients."
                            : "Only the host can apply changes."
                    ),
                    _styleButton,
                    GUILayout.Width(140f * _scale),
                    GUILayout.Height(ControlHeight * _scale)
                )
            )
            {
                ApplyAndSync();
                Close();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            GUILayout.EndHorizontal();

            // Declared after every control on purpose: IMGUI hands an event to controls
            // in declaration order, so a drag region declared earlier would consume
            // clicks before the controls under it ever run. Bounded to the header strip
            // rather than the whole window so a drag started on empty space below cannot
            // fight the scroll view.
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, HeaderFontSize * 2f * _scale));
            DrawTooltipInWindow();
        }

        // ── Item row ──────────────────────────────────────────────────────────

        private void DrawItemRow(int i, CustomItemDefinition def, bool isHost)
        {
            GUILayout.BeginHorizontal(_styleItemRow, GUILayout.Height(ItemRowHeight * _scale));

            // Icon
            if (def.Icon != null)
                GUILayout.Box(
                    new GUIContent(def.Icon.texture, def.DisplayName),
                    _styleIconBox,
                    GUILayout.Width(ColIconWidth * _scale),
                    GUILayout.Height(ControlHeight * _scale)
                );
            else
                GUILayout.Box(
                    new GUIContent("?", def.DisplayName),
                    _styleIconBox,
                    GUILayout.Width(ColIconWidth * _scale),
                    GUILayout.Height(ControlHeight * _scale)
                );

            GUILayout.Space(4f * _scale);

            // Name
            GUILayout.Label(
                new GUIContent(def.DisplayName, $"ItemType {(int)def.ItemType}"),
                _styleLabel,
                GUILayout.Width(ColNameWidth * _scale),
                GUILayout.Height(ControlHeight * _scale)
            );

            GUILayout.Space(4f * _scale);

            // Enabled toggle
            GUI.enabled = isHost;
            bool newEn = GUILayout.Toggle(
                _workingItemEnabled[i],
                new GUIContent(
                    "",
                    $"When off, {def.DisplayName} never spawns regardless of pool weights."
                ),
                _styleToggle,
                GUILayout.Width(ColToggleWidth * _scale),
                GUILayout.Height(ControlHeight * _scale)
            );
            if (isHost)
                _workingItemEnabled[i] = newEn;
            GUI.enabled = true;

            GUILayout.Space(10f * _scale);

            // Pool weight fields in display order
            foreach (var (p, _) in PoolColumns)
            {
                _workingPoolWeights[i, p] = DrawFloatField(
                    $"{i}_{p}",
                    _workingPoolWeights[i, p],
                    isHost,
                    0f,
                    999f,
                    ColWeightWidth * _scale
                );
                GUILayout.Space(4f * _scale);
            }

            GUILayout.EndHorizontal();
        }

        // ── Tier header row ───────────────────────────────────────────────────

        private void DrawTierHeader(
            int t,
            System.Collections.Generic.IReadOnlyList<CustomItemDefinition> items,
            bool isHost
        )
        {
            GUILayout.BeginHorizontal(_styleTierHeader, GUILayout.Height(TierRowHeight * _scale));

            // ── Expand/collapse ───────────────────────────────────────────────
            string arrow = _tierExpanded[t] ? "▼" : "▶";
            if (
                GUILayout.Button(
                    arrow,
                    _styleButton,
                    GUILayout.Width(ColIconWidth * _scale),
                    GUILayout.Height(TierControlHeight * _scale)
                )
            )
                _tierExpanded[t] = !_tierExpanded[t];

            GUILayout.Space(4f * _scale);

            // ── Tier name — matches column widths used in item rows ───────────
            // Item row: [28 icon][4 sp][128 name] = 160 before "On" column.
            // Header:   [24 btn][4 sp][128 label] = 156, then 4 sp to reach 160.
            GUILayout.Label(TierDefs[t].label, _styleHeader, GUILayout.Width(ColNameWidth * _scale));
            GUILayout.Space(4f * _scale);

            // ── Batch enabled toggle ──────────────────────────────────────────
            GUI.enabled = isHost;
            bool allOn = true;
            for (int i = 0; i < items.Count; i++)
                if (_itemTierIndex[i] == t && !_workingItemEnabled[i])
                {
                    allOn = false;
                    break;
                }

            bool newAll = GUILayout.Toggle(
                allOn,
                new GUIContent("", $"Enable / disable all {TierDefs[t].label} items at once."),
                _styleToggle,
                GUILayout.Width(ColToggleWidth * _scale),
                GUILayout.Height(TierControlHeight * _scale)
            );
            if (isHost && newAll != allOn)
                for (int i = 0; i < items.Count; i++)
                    if (_itemTierIndex[i] == t)
                        _workingItemEnabled[i] = newAll;
            GUI.enabled = true;

            GUILayout.Space(10f * _scale);

            // ── Batch pool-weight fields ──────────────────────────────────────
            // Editing any field immediately updates every item in this tier
            // for that pool column, and refreshes all their text buffers.
            foreach (var (p, _) in PoolColumns)
            {
                string key = $"t{t}_{p}";
                if (!_textBuffers.ContainsKey(key))
                    _textBuffers[key] = TierDefs[t].defaultWeight.ToString("F1");

                if (isHost)
                {
                    string prev = _textBuffers[key];
                    string next = GUILayout.TextField(
                        prev, _styleTextField, GUILayout.Width(ColWeightWidth * _scale));
                    if (next != prev)
                    {
                        _textBuffers[key] = next;
                        if (float.TryParse(next, out float parsed))
                        {
                            float v = Mathf.Clamp(parsed, 0f, 999f);
                            for (int i = 0; i < items.Count; i++)
                            {
                                if (_itemTierIndex[i] != t)
                                    continue;
                                _workingPoolWeights[i, p] = v;
                                _textBuffers[$"{i}_{p}"] = v.ToString("F1");
                            }
                        }
                    }
                }
                else
                {
                    GUILayout.Label(
                        _textBuffers[key],
                        _styleReadOnly,
                        GUILayout.Width(ColWeightWidth * _scale)
                    );
                }

                GUILayout.Space(4f * _scale);
            }

            GUILayout.EndHorizontal();
        }

        // ── Tooltip rendering ─────────────────────────────────────────────────

        private void DrawTooltipInWindow()
        {
            string tip = GUI.tooltip;
            if (string.IsNullOrEmpty(tip))
                return;

            Vector2 mouse = Event.current.mousePosition;
            float maxWidth = 320f * _scale;
            GUIContent content = new GUIContent(tip);
            float height = _styleTooltip.CalcHeight(content, maxWidth) + (10f * _scale);
            float x = Mathf.Min(mouse.x + (14f * _scale), _windowRect.width - maxWidth - (8f * _scale));
            float y = Mathf.Min(mouse.y + (18f * _scale), _windowRect.height - height - (8f * _scale));
            GUI.Box(new Rect(x, y, maxWidth, height), tip, _styleTooltip);
        }

        // ── IMGUI field helper ────────────────────────────────────────────────

        private float DrawFloatField(
            string key,
            float value,
            bool editable,
            float min,
            float max,
            float width
        )
        {
            if (!_textBuffers.ContainsKey(key))
                _textBuffers[key] = value.ToString("0.##");

            if (editable)
            {
                string newText = GUILayout.TextField(
                    _textBuffers[key], _styleTextField, GUILayout.Width(width));
                if (newText != _textBuffers[key])
                {
                    _textBuffers[key] = newText;
                    if (float.TryParse(newText, out float parsed))
                        value = Mathf.Clamp(parsed, min, max);
                }
            }
            else
            {
                GUILayout.Label(value.ToString("0.##"), _styleReadOnly, GUILayout.Width(width));
            }

            return value;
        }

        // ── Style init ────────────────────────────────────────────────────────

        private void InitStyles()
        {
            // Font sizes and padding bake the scale in, so a resolution change or a
            // config edit has to rebuild them -- otherwise every box grows while the
            // text inside it stays at its original size.
            int pad2 = Mathf.RoundToInt(2f * _scale);
            int pad4 = Mathf.RoundToInt(4f * _scale);
            int pad6 = Mathf.RoundToInt(6f * _scale);
            int pad8 = Mathf.RoundToInt(8f * _scale);

            // Controls that were previously drawn with the bare skin (buttons, text
            // fields, plain labels) need scaled copies of their own. Mutating
            // GUI.skin.* directly would be simpler but writes to the skin every other
            // IMGUI overlay in the mod shares, so the change would leak out of this
            // panel and resize unrelated HUD elements.
            int baseFont = GUI.skin.label.fontSize > 0 ? GUI.skin.label.fontSize : 12;
            int scaledFont = Mathf.RoundToInt(baseFont * _scale);

            _styleButton = new GUIStyle(GUI.skin.button) { fontSize = scaledFont };
            _styleTextField = new GUIStyle(GUI.skin.textField) { fontSize = scaledFont };
            _styleLabel = new GUIStyle(GUI.skin.label) { fontSize = scaledFont };
            _styleToggle = new GUIStyle(GUI.skin.toggle);

            _styleHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(HeaderFontSize * _scale),
                fontStyle = FontStyle.Bold,
            };

            _styleColHeader = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = Mathf.RoundToInt(ColHeaderFontSize * _scale),
                alignment = TextAnchor.MiddleCenter,
            };

            _styleItemRow = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(pad4, pad4, pad2, pad2),
                margin = new RectOffset(0, 0, Mathf.RoundToInt(1f * _scale), Mathf.RoundToInt(1f * _scale)),
            };

            _styleTierHeader = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(pad4, pad4, pad2, pad2),
                margin = new RectOffset(0, 0, Mathf.RoundToInt(3f * _scale), Mathf.RoundToInt(1f * _scale)),
                normal = { background = MakeTex(2, 2, new Color(0.22f, 0.22f, 0.28f, 1f)) },
            };

            _styleIconBox = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(pad2, pad2, pad2, pad2),
            };

            _styleReadOnly = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.65f, 0.65f, 0.65f) },
            };

            _styleTooltip = new GUIStyle(GUI.skin.box)
            {
                wordWrap = true,
                padding = new RectOffset(pad8, pad8, pad6, pad6),
                fontSize = Mathf.RoundToInt(TooltipFontSize * _scale),
                alignment = TextAnchor.UpperLeft,
                normal =
                {
                    background = MakeTex(2, 2, new Color(0.08f, 0.08f, 0.08f, 0.92f)),
                    textColor = new Color(0.95f, 0.95f, 0.85f),
                },
            };

            _styleScale = _scale;
            _stylesInitialised = true;
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = col;
            var tex = new Texture2D(w, h);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
