// SpawnConfigUI.cs
// An IMGUI overlay that lets the host edit all spawn configuration at runtime.
//
// ── Opening the panel ─────────────────────────────────────────────────────────
//   Press the hotkey bound to ModConfig.Global.SpawnConfigUIKey (default: F8).
//   Alternatively, run the console command "spawnConfigUI".
//
// ── Host vs client ────────────────────────────────────────────────────────────
//   Host  → full editing controls. "Apply" persists to BepInEx cfg + broadcasts.
//   Client → read-only display of the most recent snapshot received from the host.
//
// ── Layout overview ───────────────────────────────────────────────────────────
//   ┌─ Spawn Config ────────────────────────────────────────────────────────────┐
//   │  [×] Close  ──  [ ] Custom items enabled  ─  Global rate: [___] ─ Catchup: [___]  │
//   ├─── Tier tabs: [Tier 1] [Tier 2] [Tier 3] ─────────────────────────────────┤
//   │  Selected tier panel:                                                       │
//   │    [ ] Tier enabled                                                         │
//   │    Spawn weight: [____]                                                     │
//   │    Min dist behind leader: [____] units  (0 = disabled)                    │
//   │    Min place to trigger: [____]           (0 = disabled)                   │
//   │  ─ Items in this tier ───────────────────────────────────────────────────  │
//   │    [Icon] Item Name  [ ] Enabled  Override weight: [ ] [____]  [Move▼]    │
//   │    ...                                                                      │
//   ├───────────────────────────────────────────────────────────────────────────┤
//   │  [Cancel]  [Apply & Sync]                                                  │
//   └───────────────────────────────────────────────────────────────────────────┘

using System;
using System.Collections.Generic;
using System.Linq;
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
        // The host edits this working copy; "Apply" commits it.
        // Clients display the TierConfigSyncer.CurrentSnapshot in read-only mode.
        private SpawnConfigSnapshot _working;

        // ── UI state ──────────────────────────────────────────────────────────
        private int _selectedTier = 1;
        private Vector2 _tierItemScrollPos;
        private Vector2 _windowScrollPos;

        // String buffers for text fields (IMGUI text-field limitation workaround)
        private Dictionary<string, string> _textBuffers = new Dictionary<string, string>();

        // Window rect — draggable
        private Rect _windowRect = new Rect(80, 60, 1280, 720);

        // Styles (initialised lazily on first OnGUI call)
        private GUIStyle _styleHeader;
        private GUIStyle _styleTierTabActive;
        private GUIStyle _styleTierTabInactive;
        private GUIStyle _styleItemRow;
        private GUIStyle _styleIconBox;
        private GUIStyle _styleReadOnly;
        private bool _stylesInitialised;

        // Tier name labels
        private static readonly Dictionary<int, string> TierLabels = new Dictionary<int, string>
        {
            { 1, "Tier 1 — Common" },
            { 2, "Tier 2 — Standard" },
            { 3, "Tier 3 — Rare / Powerful" },
            { 4, "Tier 4 — Very Rare" },
            { 5, "Tier 5 — Ultra Rare" },
        };

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

            // Toggle on configured hotkey (default F8)
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
            if (ModConfig.Global.NumTiers == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[SpawnConfigUI] Cannot open — Configuration not initialised."
                );
                return;
            }

            _visible = true;
            // Always re-build the working copy from the current authoritative snapshot
            // so edits always start from the latest state.
            var authoritativeSnap =
                TierConfigSyncer.Instance?.CurrentSnapshot
                ?? SpawnConfigSnapshot.FromConfiguration();

            _working = authoritativeSnap.DeepCopy();

            // Ensure all tiers in the working copy are known.
            _selectedTier = _working.TierSettings.Keys.OrderBy(t => t).FirstOrDefault();
            _textBuffers.Clear();

            IssaPluginPlugin.Log.LogInfo("[SpawnConfigUI] Opened.");
        }

        public void Close()
        {
            _visible = false;
            IssaPluginPlugin.Log.LogInfo("[SpawnConfigUI] Closed.");
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_visible || _working == null)
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
            var snap = _working; // editing copy (host) or display copy (client)

            // ── Window header ─────────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Label("⚙  Spawn Config", _styleHeader);
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

            bool customEnabled = snap.CustomItemSpawnsEnabled;
            bool newCustomEnabled = GUILayout.Toggle(
                customEnabled,
                " Custom items enabled",
                GUILayout.Width(200)
            );
            if (isHost && newCustomEnabled != customEnabled)
                snap.CustomItemSpawnsEnabled = newCustomEnabled;

            GUILayout.Space(20);
            GUILayout.Label("Global rate multiplier:", GUILayout.Width(150));
            snap.GlobalSpawnRateMultiplier = DrawFloatField(
                "globalRate",
                snap.GlobalSpawnRateMultiplier,
                isHost,
                0f,
                10f,
                80
            );

            GUILayout.Space(20);
            GUILayout.Label("Catchup boost:", GUILayout.Width(110));
            snap.CatchupBoostFactor = DrawFloatField(
                "catchup",
                snap.CatchupBoostFactor,
                isHost,
                0f,
                5f,
                60
            );

            GUILayout.Space(20);
            GUILayout.Label("Tiers:", GUILayout.Width(42));
            int currentNumTiers = _working.TierSettings.Count;

            // "−" button: remove the highest tier (only if it has no items assigned to it)
            int _topTier = currentNumTiers > 1 ? _working.TierSettings.Keys.Max() : 0;
            bool _topTierHasItems =
                _topTier > 0
                && _working.ItemOverrides.Values.Any(o => o.AssignedTier == _topTier);
            GUI.enabled = isHost && currentNumTiers > 1 && !_topTierHasItems;
            if (GUILayout.Button("−", GUILayout.Width(24), GUILayout.Height(22)))
            {
                _working.TierSettings.Remove(_topTier);
                if (_selectedTier == _topTier)
                    _selectedTier = _topTier - 1;
                _textBuffers.Clear(); // reset field buffers after structural change
            }
            GUI.enabled = true;

            GUILayout.Label(currentNumTiers.ToString(), GUILayout.Width(20), GUILayout.Height(22));

            // "+" button: add a new tier above the current highest
            GUI.enabled = isHost && currentNumTiers < ModConfig.MaxTiers;
            if (GUILayout.Button("+", GUILayout.Width(24), GUILayout.Height(22)))
            {
                int newTier = _working.TierSettings.Keys.Max() + 1;
                // Default weight: half the current highest tier's weight, minimum 1.
                float prevWeight = _working.TierSettings[newTier - 1].SpawnWeight;
                _working.TierSettings[newTier] = new TierSettings(
                    newTier,
                    Mathf.Max(1f, prevWeight * 0.5f)
                );
                _selectedTier = newTier;
                _textBuffers.Clear();
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // ── Tier tab bar ──────────────────────────────────────────────────
            var sortedTiers = snap.TierSettings.Keys.OrderBy(t => t).ToList();

            GUILayout.BeginHorizontal();
            foreach (int tier in sortedTiers)
            {
                bool active = tier == _selectedTier;
                GUIStyle tabStyle = active ? _styleTierTabActive : _styleTierTabInactive;
                string label = TierLabels.TryGetValue(tier, out var tl) ? tl : $"Tier {tier}";

                // Show a small indicator if tier is disabled
                if (!snap.TierSettings[tier].TierEnabled)
                    label = "⊘ " + label;

                if (GUILayout.Button(label, tabStyle, GUILayout.Height(28)))
                    _selectedTier = tier;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // ── Main scroll area ──────────────────────────────────────────────
            _windowScrollPos = GUILayout.BeginScrollView(_windowScrollPos);

            if (snap.TierSettings.TryGetValue(_selectedTier, out var tierSettings))
                DrawTierPanel(tierSettings, snap, isHost, sortedTiers);

            GUILayout.EndScrollView();

            // ── Bottom action bar (host only) ─────────────────────────────────
            if (isHost)
            {
                GUILayout.Space(4);
                GUILayout.Box("", GUILayout.ExpandWidth(true), GUILayout.Height(1));
                GUILayout.Space(4);

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(100), GUILayout.Height(28)))
                    Close();

                GUILayout.Space(10);

                GUI.backgroundColor = new Color(0.3f, 0.9f, 0.3f);
                if (GUILayout.Button("✔  Apply & Sync", GUILayout.Width(140), GUILayout.Height(28)))
                    ApplyAndClose();
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();
            }

            // Make window draggable by any non-control area.
            GUI.DragWindow();
        }

        // ── Tier panel ────────────────────────────────────────────────────────

        private void DrawTierPanel(
            TierSettings ts,
            SpawnConfigSnapshot snap,
            bool isHost,
            List<int> allTiers
        )
        {
            // ── Tier-level controls ───────────────────────────────────────────
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            bool newTierEnabled = GUILayout.Toggle(
                ts.TierEnabled,
                " Tier enabled",
                GUILayout.Width(140)
            );
            if (isHost)
                ts.TierEnabled = newTierEnabled;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Spawn weight:", GUILayout.Width(130));
            ts.SpawnWeight = DrawFloatField(
                $"tier{ts.Tier}weight",
                ts.SpawnWeight,
                isHost,
                0f,
                100f,
                80
            );
            GUILayout.Space(20);
            GUILayout.Label("(relative to other tier weights)", GUILayout.Width(240));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // ── Distance gating ───────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Label("Min distance behind leader:", GUILayout.Width(200));
            ts.MinDistanceBehindLeader = DrawFloatField(
                $"tier{ts.Tier}dist",
                ts.MinDistanceBehindLeader,
                isHost,
                0f,
                10000f,
                80
            );
            GUILayout.Space(8);
            GUILayout.Label("units  (0 = no gate)", GUILayout.Width(150));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // ── Place gating ──────────────────────────────────────────────────
            GUILayout.BeginHorizontal();
            GUILayout.Label("Min place to trigger:", GUILayout.Width(200));
            ts.MinPlaceTrigger = DrawIntField(
                $"tier{ts.Tier}place",
                ts.MinPlaceTrigger,
                isHost,
                0,
                32,
                50
            );
            GUILayout.Space(8);
            GUILayout.Label(
                "  (e.g. 3 = only 3rd place or worse; 0 = no gate)",
                GUILayout.Width(340)
            );
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.EndVertical();

            GUILayout.Space(8);

            // ── Items in this tier ────────────────────────────────────────────
            GUILayout.Label($"  Items in Tier {ts.Tier}:", _styleHeader);
            GUILayout.Space(4);

            var itemsInTier = ItemRegistry
                .AllItems.Where(def => GetEffectiveTier(def, snap) == ts.Tier)
                .OrderBy(def => def.DisplayName)
                .ToList();

            if (itemsInTier.Count == 0)
            {
                GUILayout.Label("    (no items assigned to this tier)", _styleReadOnly);
            }
            else
            {
                _tierItemScrollPos = GUILayout.BeginScrollView(
                    _tierItemScrollPos,
                    false,
                    true,
                    GUILayout.Height(Mathf.Min(itemsInTier.Count * 52 + 16, 320))
                );

                foreach (var def in itemsInTier)
                    DrawItemRow(def, snap, isHost, allTiers);

                GUILayout.EndScrollView();
            }
        }

        // ── Item row ──────────────────────────────────────────────────────────

        private void DrawItemRow(
            CustomItemDefinition def,
            SpawnConfigSnapshot snap,
            bool isHost,
            List<int> allTiers
        )
        {
            int id = (int)def.ItemType;
            if (!snap.ItemOverrides.TryGetValue(id, out var ov))
                return;

            GUILayout.BeginHorizontal(_styleItemRow, GUILayout.Height(48));

            // ── Icon ──────────────────────────────────────────────────────────
            if (def.Icon != null)
            {
                Texture2D tex = def.Icon.texture;
                GUILayout.Box(tex, _styleIconBox, GUILayout.Width(40), GUILayout.Height(40));
            }
            else
            {
                GUILayout.Box("?", _styleIconBox, GUILayout.Width(40), GUILayout.Height(40));
            }

            GUILayout.Space(6);

            // ── Name ─────────────────────────────────────────────────────────
            GUILayout.Label(def.DisplayName, GUILayout.Width(160), GUILayout.Height(40));

            GUILayout.Space(8);

            // ── Enabled toggle ────────────────────────────────────────────────
            bool newEnabled = GUILayout.Toggle(
                ov.Enabled,
                " Enabled",
                GUILayout.Width(90),
                GUILayout.Height(40)
            );
            if (isHost)
                ov.Enabled = newEnabled;

            GUILayout.Space(10);

            // ── Spawn weight override ─────────────────────────────────────────
            bool newOvEnabled = GUILayout.Toggle(
                ov.SpawnWeightOverrideEnabled,
                " Override weight:",
                GUILayout.Width(145),
                GUILayout.Height(40)
            );
            if (isHost)
                ov.SpawnWeightOverrideEnabled = newOvEnabled;

            GUI.enabled = isHost && ov.SpawnWeightOverrideEnabled;
            float newWeight = DrawFloatField(
                $"item{id}weight",
                ov.SpawnWeightOverride,
                isHost && ov.SpawnWeightOverrideEnabled,
                0f,
                100f,
                60
            );
            if (isHost)
                ov.SpawnWeightOverride = newWeight;
            GUI.enabled = true;

            GUILayout.Space(10);

            // ── Effective weight display ──────────────────────────────────────
            float effectiveWeight = ov.SpawnWeightOverrideEnabled
                ? ov.SpawnWeightOverride
                : (
                    snap.TierSettings.TryGetValue(GetEffectiveTier(def, snap), out var tierS)
                        ? tierS.SpawnWeight
                        : 0f
                );
            GUILayout.Label(
                $"Eff: {effectiveWeight:0.#}",
                GUILayout.Width(60),
                GUILayout.Height(40)
            );

            GUILayout.FlexibleSpace();

            // ── Move to tier dropdown ─────────────────────────────────────────
            if (isHost && allTiers.Count > 1)
            {
                GUILayout.Label("Move →", GUILayout.Width(48), GUILayout.Height(40));
                foreach (int targetTier in allTiers)
                {
                    if (targetTier == GetEffectiveTier(def, snap))
                        continue;
                    string tierLabel = TierLabels.TryGetValue(targetTier, out var tl)
                        ? $"T{targetTier}"
                        : $"T{targetTier}";
                    if (GUILayout.Button(tierLabel, GUILayout.Width(32), GUILayout.Height(28)))
                        MoveItemToTier(def, targetTier);
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// Returns the tier that should currently be associated with this item
        /// (checks if _working has a runtime re-assignment, then falls back to def.Tier).
        private int GetEffectiveTier(CustomItemDefinition def, SpawnConfigSnapshot snap)
        {
            int id = (int)def.ItemType;
            if (snap.ItemOverrides.TryGetValue(id, out var ov))
                return ov.AssignedTier;
            return def.Tier;
        }

        private void MoveItemToTier(CustomItemDefinition def, int newTier)
        {
            int id = (int)def.ItemType;
            if (_working.ItemOverrides.TryGetValue(id, out var ov))
            {
                ov.AssignedTier = newTier;
                // Clear any per-item weight override so the item inherits its new tier's weight.
                ov.SpawnWeightOverrideEnabled = false;
            }
            IssaPluginPlugin.Log.LogInfo(
                $"[SpawnConfigUI] Moved {def.DisplayName} to Tier {newTier} (pending apply)."
            );
        }

        private void ApplyAndClose()
        {
            TierConfigSyncer.Instance?.ApplyAndBroadcast(_working);
            Close();
        }

        // ── IMGUI field helpers ───────────────────────────────────────────────

        private float DrawFloatField(
            string key,
            float value,
            bool editable,
            float min,
            float max,
            int width
        )
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

        private int DrawIntField(string key, int value, bool editable, int min, int max, int width)
        {
            if (!_textBuffers.ContainsKey(key))
                _textBuffers[key] = value.ToString();

            if (editable)
            {
                string newText = GUILayout.TextField(_textBuffers[key], GUILayout.Width(width));
                if (newText != _textBuffers[key])
                {
                    _textBuffers[key] = newText;
                    if (int.TryParse(newText, out int parsed))
                        value = Mathf.Clamp(parsed, min, max);
                }
            }
            else
            {
                GUILayout.Label(value.ToString(), _styleReadOnly, GUILayout.Width(width));
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

            _styleTierTabActive = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                normal = { background = MakeTex(2, 2, new Color(0.25f, 0.55f, 0.95f, 1f)) },
            };

            _styleTierTabInactive = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Normal };

            _styleItemRow = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(6, 6, 4, 4),
                margin = new RectOffset(0, 0, 2, 2),
            };

            _styleIconBox = new GUIStyle(GUI.skin.box) { padding = new RectOffset(2, 2, 2, 2) };

            _styleReadOnly = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.65f, 0.65f, 0.65f) },
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
