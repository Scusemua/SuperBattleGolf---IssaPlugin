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
//   │  Item Name          On   Lead  Beh.50  Beh.125  Beh.200  Ahead  Mob.    │
//   │  Baseball Bat       [✓]  [15]  [15]    [15]     [15]     [15]   [15]    │
//   │  ...                                                                      │
//   │  ──────────────────────────────────────────────────────────────────────  │
//   │  [Cancel]   [Apply & Sync]                                                │
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
        private bool[] _workingItemEnabled;       // [itemIndex] into ItemRegistry.AllItems
        private float[,] _workingPoolWeights;     // [itemIndex, gamePoolIndex 0-5]

        // IMGUI text field buffers — keyed by "{itemIndex}_{gamePoolIndex}" for pool
        // weight fields, and "rate" for the global rate field.
        // Required because IMGUI TextField resets cursor position on every frame
        // without a per-field string buffer.
        private readonly Dictionary<string, string> _textBuffers = new();

        // ── UI state ──────────────────────────────────────────────────────────
        private Vector2 _scrollPos;

        // Window rect — draggable
        private Rect _windowRect = new Rect(40, 40, 1360, 740);

        // Column display order: (gamePoolIndex, header label)
        // Matches the base game pause menu order (Lead, Beh50, Beh125, Beh200, Ahead, Mob).
        private static readonly (int pool, string label)[] PoolColumns =
        {
            (GlobalConfig.PoolLead,      "Lead"),
            (GlobalConfig.PoolBehind50,  "Beh.50"),
            (GlobalConfig.PoolBehind125, "Beh.125"),
            (GlobalConfig.PoolBehind200, "Beh.200"),
            (GlobalConfig.PoolAhead,     "Ahead"),
            (GlobalConfig.PoolMobility,  "Mob."),
        };

        // Styles (initialised lazily on first OnGUI call)
        private GUIStyle _styleHeader;
        private GUIStyle _styleItemRow;
        private GUIStyle _styleIconBox;
        private GUIStyle _styleReadOnly;
        private GUIStyle _styleColHeader;
        private GUIStyle _styleTooltip;
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
            if (_visible) Close();
            else Open();
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
            _workingRate    = ModConfig.Global.CustomItemSpawnRate.Value;
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
        }

        private void ApplyAndSync()
        {
            ModConfig.Global.CustomItemSpawnsEnabled.Value = _workingEnabled;
            ModConfig.Global.CustomItemSpawnRate.Value     = _workingRate;
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
            ItemConfigSyncer.Broadcast();
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_visible || _workingItemEnabled == null)
                return;

            if (!_stylesInitialised)
                InitStyles();

            // Block game input while the panel is open.
            if (Event.current.type == EventType.KeyDown || Event.current.type == EventType.KeyUp)
                Event.current.Use();

            _windowRect = GUI.Window(0xCA7C0, _windowRect, DrawWindow, "");
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
            if (GUILayout.Button("✕ Close", GUILayout.Width(80)))
                Close();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
            GUILayout.Space(4);

            // ── Global toggles row ────────────────────────────────────────────
            GUILayout.BeginHorizontal();

            GUI.enabled = isHost;
            bool newEnabled = GUILayout.Toggle(
                _workingEnabled,
                new GUIContent(" Custom items enabled",
                    "Master switch for all custom items. When off, no custom items will appear in the item pool."),
                GUILayout.Width(200)
            );
            if (isHost) _workingEnabled = newEnabled;

            GUILayout.Space(20);
            GUILayout.Label(
                new GUIContent("Global rate multiplier:",
                    "Scales the spawn weight of ALL custom items. 1.0 = normal, 0.5 = half as frequent."),
                GUILayout.Width(160)
            );
            _workingRate = DrawFloatField("rate", _workingRate, isHost, 0f, 10f, 70);

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
            GUILayout.Space(4);

            // ── Column header row ─────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Label("Item Name",   _styleColHeader, GUILayout.Width(160));
            GUILayout.Label("On",          _styleColHeader, GUILayout.Width(30));
            GUILayout.Space(4);
            foreach (var (_, label) in PoolColumns)
                GUILayout.Label(label, _styleColHeader, GUILayout.Width(58));
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
            GUILayout.Space(2);

            // ── Item rows (scrollable) ────────────────────────────────────────
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            var items = ItemRegistry.AllItems;
            for (int i = 0; i < items.Count; i++)
                DrawItemRow(i, items[i], isHost);

            GUILayout.EndScrollView();

            // ── Bottom action bar ─────────────────────────────────────────────
            GUILayout.Space(4);
            GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(
                    new GUIContent("Cancel", "Discard unsaved changes and close."),
                    GUILayout.Width(100), GUILayout.Height(28)))
                Close();

            GUILayout.Space(10);

            GUI.enabled = isHost;
            GUI.backgroundColor = isHost ? new Color(0.3f, 0.9f, 0.3f) : Color.white;
            if (GUILayout.Button(
                    new GUIContent("✔  Apply & Sync",
                        isHost
                            ? "Save changes and broadcast to all clients."
                            : "Only the host can apply changes."),
                    GUILayout.Width(140), GUILayout.Height(28)))
            {
                ApplyAndSync();
                Close();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            GUILayout.EndHorizontal();

            GUI.DragWindow();
            DrawTooltipInWindow();
        }

        // ── Item row ──────────────────────────────────────────────────────────

        private void DrawItemRow(int i, CustomItemDefinition def, bool isHost)
        {
            GUILayout.BeginHorizontal(_styleItemRow, GUILayout.Height(36));

            // Icon
            if (def.Icon != null)
                GUILayout.Box(new GUIContent(def.Icon.texture, def.DisplayName),
                    _styleIconBox, GUILayout.Width(28), GUILayout.Height(28));
            else
                GUILayout.Box(new GUIContent("?", def.DisplayName),
                    _styleIconBox, GUILayout.Width(28), GUILayout.Height(28));

            GUILayout.Space(4);

            // Name
            GUILayout.Label(
                new GUIContent(def.DisplayName, $"ItemType {(int)def.ItemType}"),
                GUILayout.Width(128), GUILayout.Height(28));

            GUILayout.Space(4);

            // Enabled toggle
            GUI.enabled = isHost;
            bool newEn = GUILayout.Toggle(
                _workingItemEnabled[i],
                new GUIContent("",
                    $"When off, {def.DisplayName} never spawns regardless of pool weights."),
                GUILayout.Width(20), GUILayout.Height(28));
            if (isHost) _workingItemEnabled[i] = newEn;
            GUI.enabled = true;

            GUILayout.Space(10);

            // Pool weight fields in display order
            foreach (var (p, _) in PoolColumns)
            {
                _workingPoolWeights[i, p] = DrawFloatField(
                    $"{i}_{p}",
                    _workingPoolWeights[i, p],
                    isHost,
                    0f, 999f, 50);
                GUILayout.Space(4);
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
            float maxWidth = 320f;
            GUIContent content = new GUIContent(tip);
            float height = _styleTooltip.CalcHeight(content, maxWidth) + 10f;
            float x = Mathf.Min(mouse.x + 14f, _windowRect.width - maxWidth - 8f);
            float y = Mathf.Min(mouse.y + 18f, _windowRect.height - height - 8f);
            GUI.Box(new Rect(x, y, maxWidth, height), tip, _styleTooltip);
        }

        // ── IMGUI field helper ────────────────────────────────────────────────

        private float DrawFloatField(string key, float value, bool editable,
            float min, float max, int width)
        {
            if (!_textBuffers.ContainsKey(key))
                _textBuffers[key] = value.ToString("0.##");

            if (editable)
            {
                string newText = GUILayout.TextField(_textBuffers[key], GUILayout.Width(width));
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
            _styleHeader = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
            };

            _styleColHeader = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
            };

            _styleItemRow = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(4, 4, 2, 2),
                margin  = new RectOffset(0, 0, 1, 1),
            };

            _styleIconBox = new GUIStyle(GUI.skin.box) { padding = new RectOffset(2, 2, 2, 2) };

            _styleReadOnly = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.65f, 0.65f, 0.65f) },
            };

            _styleTooltip = new GUIStyle(GUI.skin.box)
            {
                wordWrap = true,
                padding  = new RectOffset(8, 8, 6, 6),
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                normal =
                {
                    background = MakeTex(2, 2, new Color(0.08f, 0.08f, 0.08f, 0.92f)),
                    textColor  = new Color(0.95f, 0.95f, 0.85f),
                },
            };

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
