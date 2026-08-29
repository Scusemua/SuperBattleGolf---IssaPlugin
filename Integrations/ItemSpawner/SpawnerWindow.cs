using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using Mirror;

namespace IssaPlugin.Integrations.SpawnerUI
{
    /// <summary>
    /// A self-contained item spawner window: a multi-column icon grid with a live search
    /// box and a source filter (ours / base game / other mods).
    ///
    /// This does not depend on the ItemSpawner mod. Everything it needs comes from the
    /// base game (the item collection, the player roster, ServerTryAddItem) or from our
    /// own <see cref="Items.ItemRegistry"/>. When ItemSpawner *is* installed,
    /// <see cref="ItemSpawnerIntegration"/> suppresses its window so the two do not
    /// overlap — but that is a courtesy, not a requirement.
    ///
    /// Custom items route correctly because our own ServerTryAddItemPatch intercepts
    /// them; no special-casing is needed here.
    /// </summary>
    internal class SpawnerWindow : MonoBehaviour
    {
        private const int WindowId = 0x1554A;
        private const string SearchControlName = "ISSA_SPAWNER_SEARCH";
        private const float IconSize = 48f;
        private const float CellHeight = 78f;
        private const float CellSpacing = 6f;
        private const float CaptionHeight = 26f;
        private const float MinCellWidth = 90f;

        /// <summary>Window chrome plus the scroll view's scrollbar.</summary>
        private const float GridPadding = 48f;

        private bool _open;
        private Rect _windowRect = new Rect(120, 120, 720, 520);
        private Vector2 _scroll;

        private string _search = string.Empty;
        private int _sourceIndex;
        private List<string> _sourceOptions = new List<string> { SpawnerItemCatalog.AllSources };

        private List<SpawnerItemCatalog.Entry> _catalog = new List<SpawnerItemCatalog.Entry>();
        private List<SpawnerItemCatalog.Entry> _filtered = new List<SpawnerItemCatalog.Entry>();

        private List<SpawnerPlayerRoster.Player> _players = new List<SpawnerPlayerRoster.Player>();
        private int _playerIndex;

        // Styles are rebuilt on scene load because their textures do not survive it.
        private GUIStyle _windowStyle;
        private GUIStyle _cellStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _cellLabelStyle;
        private GUIStyle _searchStyle;
        private GUIStyle _pillStyle;
        private GUIStyle _pillActiveStyle;
        private bool _stylesReady;
        private readonly List<Texture2D> _textures = new List<Texture2D>();

        /// <summary>
        /// True while the IMGUI search field owns keyboard focus. Suppresses the toggle
        /// hotkey so typing its letter into the box does not also close the window.
        /// </summary>
        private bool _searchFocused;

        /// <summary>Cached so we do not re-resolve the toggle key every frame.</summary>
        private KeyControl _toggleControl;
        private Key _toggleKey = Key.None;

        private void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

            // Never leave the cursor force-unlocked because we went away while open.
            if (_open) Close();
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        {
            // GUIStyle textures are destroyed with the scene; rebuild them lazily.
            _stylesReady = false;
            if (_open) Close();
        }

        private void OnDestroy()
        {
            if (_open) Close();

            foreach (var texture in _textures)
            {
                if (texture != null) Destroy(texture);
            }
            _textures.Clear();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            // While the search box has focus the toggle key is being typed into it, so
            // it must not also close the window. Escape still works as an escape hatch.
            if (!_searchFocused)
            {
                KeyControl control = ResolveToggleControl(keyboard);
                if (control != null && control.wasPressedThisFrame) Toggle();
            }

            // Escape closes, matching the rest of the game's menus.
            if (_open && keyboard.escapeKey.wasPressedThisFrame) Close();
        }

        /// <summary>
        /// Resolves the configured key to an input control once, re-resolving only when
        /// the config value actually changes. The equivalent per-frame Enum.Parse is a
        /// needless allocation on every frame the game runs.
        /// </summary>
        private KeyControl ResolveToggleControl(Keyboard keyboard)
        {
            Key configured = ModConfig.Global.SpawnerToggleKey.Value;
            if (configured == Key.None) return null;

            if (_toggleControl == null || configured != _toggleKey)
            {
                _toggleKey = configured;
                _toggleControl = keyboard[configured];
            }

            return _toggleControl;
        }

        private void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        private void Open()
        {
            if (!NetworkServer.active)
            {
                IssaPluginPlugin.Log.LogInfo("[Spawner] Only the host can spawn items.");
                return;
            }

            RefreshCatalog();
            RefreshPlayers();

            _open = true;

            // Use the game's own cursor owner rather than setting Cursor.lockState
            // directly: it is a force-unlock override, so clearing it restores whatever
            // state the game wanted (including Confined for gamepad) instead of us
            // guessing "Locked" and stealing the cursor from e.g. an open pause menu.
            CursorManager.SetCursorForceUnlocked(true);
        }

        private void Close()
        {
            _open = false;
            _searchFocused = false;
            CursorManager.SetCursorForceUnlocked(false);
        }

        /// <summary>
        /// Rebuilds the item catalog. Done on open rather than per frame: resolving
        /// localized names allocates, and the item set only changes on reload.
        /// </summary>
        private void RefreshCatalog()
        {
            var items = new List<ItemData>();
            try
            {
                // ItemCollection.items is private; Count/GetItemAtIndex are the public
                // accessors, so enumerate through those rather than reflecting.
                var collection = GameManager.AllItems;
                if (collection != null)
                {
                    for (int i = 0; i < collection.Count; i++) items.Add(collection.GetItemAtIndex(i));
                }
            }
            catch (System.Exception ex)
            {
                IssaPluginPlugin.Log.LogWarning($"[Spawner] Could not read the item collection: {ex.Message}");
            }

            _catalog = SpawnerItemCatalog.Build(items);
            _sourceOptions = SpawnerItemCatalog.BuildSourceOptions(_catalog);
            if (_sourceIndex >= _sourceOptions.Count) _sourceIndex = 0;
            ApplyFilter();
        }

        /// <summary>
        /// Rebuilds the roster, keeping the selection pinned to the same player rather
        /// than the same index. If someone disconnects the list shifts, and a bare index
        /// would silently retarget the item at whoever moved into that slot.
        /// </summary>
        private void RefreshPlayers()
        {
            PlayerInventory previous =
                _playerIndex >= 0 && _playerIndex < _players.Count
                    ? _players[_playerIndex].Inventory
                    : null;

            _players = SpawnerPlayerRoster.Build();

            _playerIndex = 0;
            if (previous == null) return;

            for (int i = 0; i < _players.Count; i++)
            {
                if (_players[i].Inventory == previous)
                {
                    _playerIndex = i;
                    return;
                }
            }

            // The previously selected player is gone; fall back to the local player.
            IssaPluginPlugin.Log.LogInfo(
                "[Spawner] The selected player is no longer available; selecting yourself.");
        }

        private void ApplyFilter()
        {
            string source = _sourceIndex >= 0 && _sourceIndex < _sourceOptions.Count
                ? _sourceOptions[_sourceIndex]
                : SpawnerItemCatalog.AllSources;

            _filtered = SpawnerItemCatalog.Filter(_catalog, source, _search);
        }

        private void OnGUI()
        {
            if (!_open) return;

            EnsureStyles();
            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow, "Item Spawner", _windowStyle);
        }

        private void DrawWindow(int id)
        {
            GUI.DragWindow(new Rect(0, 0, 100000, 28));

            DrawControls();
            GUILayout.Space(6);
            DrawGrid();
            GUILayout.Space(6);
            DrawFooter();
        }

        private void DrawControls()
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label("Search", _labelStyle, GUILayout.Width(52));

            GUI.SetNextControlName(SearchControlName);
            string search = GUILayout.TextField(_search, _searchStyle, GUILayout.MinWidth(180));
            _searchFocused = GUI.GetNameOfFocusedControl() == SearchControlName;
            if (search != _search)
            {
                _search = search;
                ApplyFilter();
            }

            if (GUILayout.Button("x", GUILayout.Width(24)) && _search.Length > 0)
            {
                _search = string.Empty;
                GUI.FocusControl(null);
                ApplyFilter();
            }

            GUILayout.Space(10);
            GUILayout.Label("Show", _labelStyle, GUILayout.Width(42));

            // A simple pill row rather than a dropdown: IMGUI has no native dropdown,
            // and with only three or four sources a row is clearer and one click less.
            for (int i = 0; i < _sourceOptions.Count; i++)
            {
                GUIStyle style = i == _sourceIndex ? _pillActiveStyle : _pillStyle;
                if (GUILayout.Button(_sourceOptions[i], style))
                {
                    _sourceIndex = i;
                    ApplyFilter();
                }
            }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Give to", _labelStyle, GUILayout.Width(52));

            if (_players.Count == 0)
            {
                GUILayout.Label("No players found.", _labelStyle);
            }
            else
            {
                for (int i = 0; i < _players.Count; i++)
                {
                    GUIStyle style = i == _playerIndex ? _pillActiveStyle : _pillStyle;
                    if (GUILayout.Button(_players[i].Name, style)) _playerIndex = i;
                }
            }

            if (GUILayout.Button("Refresh", _pillStyle, GUILayout.Width(70))) RefreshPlayers();

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws the items as a grid. Column count is derived from the window width so
        /// the grid reflows when the window is resized, rather than being fixed.
        /// </summary>
        private void DrawGrid()
        {
            int columns = Mathf.Clamp(Mathf.FloorToInt((_windowRect.width - 40f) / 140f), 1, 6);

            // Cells must be an explicit, uniform width or IMGUI sizes each one to its own
            // label and the "grid" ends up as ragged columns that do not line up.
            float available = _windowRect.width - GridPadding;
            float cellWidth = Mathf.Max(MinCellWidth, (available / columns) - CellSpacing);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

            if (_filtered.Count == 0)
            {
                GUILayout.Space(20);
                GUILayout.Label(
                    _catalog.Count == 0
                        ? "Items not loaded yet."
                        : $"No items match \"{_search}\".",
                    _labelStyle);
            }

            for (int i = 0; i < _filtered.Count; i += columns)
            {
                GUILayout.BeginHorizontal();

                for (int column = 0; column < columns; column++)
                {
                    int index = i + column;
                    if (index >= _filtered.Count)
                    {
                        // Reserve a real cell-sized gap so the last row lines up with the
                        // rows above. A FlexibleSpace would instead absorb all remaining
                        // width and stretch the row's real cells out of alignment.
                        GUILayout.Space(cellWidth + CellSpacing);
                        continue;
                    }

                    DrawCell(_filtered[index], cellWidth);
                }

                GUILayout.EndHorizontal();
                GUILayout.Space(4);
            }

            GUILayout.EndScrollView();
        }

        private void DrawCell(SpawnerItemCatalog.Entry entry, float width)
        {
            GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.Height(CellHeight));

            // Draw the button first, then blit the icon into it. Sprite.texture returns
            // the whole source texture, which for an atlased sprite is the entire atlas —
            // so the icon has to be drawn through its textureRect rather than handed to
            // GUIContent directly.
            bool clicked = GUILayout.Button(
                GUIContent.none, _cellStyle, GUILayout.Width(width), GUILayout.Height(IconSize + 8f));

            Rect buttonRect = GUILayoutUtility.GetLastRect();
            Sprite icon = entry.Data.Icon;

            if (icon != null && icon.texture != null)
            {
                var iconRect = new Rect(
                    buttonRect.x + (buttonRect.width - IconSize) * 0.5f,
                    buttonRect.y + (buttonRect.height - IconSize) * 0.5f,
                    IconSize,
                    IconSize);

                Rect tr = icon.textureRect;
                var coords = new Rect(
                    tr.x / icon.texture.width,
                    tr.y / icon.texture.height,
                    tr.width / icon.texture.width,
                    tr.height / icon.texture.height);

                GUI.DrawTextureWithTexCoords(iconRect, icon.texture, coords);
            }

            if (clicked)
            {
                Give(entry);
            }

            GUILayout.Label(entry.DisplayName, _cellLabelStyle, GUILayout.Width(width), GUILayout.Height(CaptionHeight));
            GUILayout.EndVertical();

            GUILayout.Space(CellSpacing);
        }

        private void DrawFooter()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{_filtered.Count} of {_catalog.Count} items", _labelStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(90), GUILayout.Height(26))) Close();
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Gives the item to the selected player. Uses ServerTryAddItem for every item:
        /// our ServerTryAddItemPatch already redirects custom items to
        /// DirectAddCustomItem, so both kinds take the correct path here.
        /// </summary>
        private void Give(SpawnerItemCatalog.Entry entry)
        {
            if (!NetworkServer.active)
            {
                IssaPluginPlugin.Log.LogWarning("[Spawner] Only the host can spawn items.");
                return;
            }

            if (_playerIndex < 0 || _playerIndex >= _players.Count)
            {
                IssaPluginPlugin.Log.LogWarning("[Spawner] No player selected.");
                return;
            }

            var target = _players[_playerIndex];

            // The window can sit open for a long time; verify the target still exists
            // before handing it an item. == null on a NetworkBehaviour also catches a
            // destroyed object, not just a cleared reference.
            if (target.Inventory == null)
            {
                IssaPluginPlugin.Log.LogWarning("[Spawner] That player is no longer available.");
                RefreshPlayers();
                return;
            }

            // Read uses from our registry for custom items rather than the cached
            // ItemData: ItemData.MaxUses is only refreshed when GetOrCreateItemData runs,
            // so it can be stale after the user edits the item's Uses config at runtime.
            int uses = Items.ItemRegistry.IsCustomItem(entry.Data.Type)
                ? Items.ItemRegistry.GetMaxUses(entry.Data.Type)
                : entry.Data.MaxUses;
            if (uses <= 0) uses = 1;
            bool added = target.Inventory.ServerTryAddItem(entry.Data.Type, uses);

            IssaPluginPlugin.Log.LogInfo(
                added
                    ? $"[Spawner] Gave {entry.DisplayName} to {target.Name}."
                    : $"[Spawner] Could not give {entry.DisplayName} to {target.Name} (inventory full?).");
        }

        // ── Styles ───────────────────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            _windowStyle = new GUIStyle(GUI.skin.window);
            _windowStyle.normal.background = MakeTexture(new Color(0.10f, 0.10f, 0.12f, 0.94f));

            _cellStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 4, 4),
                imagePosition = ImagePosition.ImageOnly,
            };
            _cellStyle.normal.background = MakeTexture(new Color(0.24f, 0.24f, 0.28f, 0.85f));
            _cellStyle.hover.background = MakeTexture(new Color(0.36f, 0.36f, 0.42f, 0.95f));
            _cellStyle.active.background = MakeTexture(new Color(0.18f, 0.18f, 0.22f, 0.95f));

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                wordWrap = false,
            };
            _labelStyle.normal.textColor = Color.white;

            // Cell captions get their own style: item names like "Rocket Tether Grenade"
            // are wider than a cell, so these wrap and clip rather than bleeding into
            // the neighbouring column the way the non-wrapping label style would.
            _cellLabelStyle = new GUIStyle(_labelStyle)
            {
                wordWrap = true,
                fontSize = 10,
                clipping = TextClipping.Clip,
                alignment = TextAnchor.UpperCenter,
            };

            _searchStyle = new GUIStyle(GUI.skin.textField) { fontSize = 12 };

            _pillStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                padding = new RectOffset(10, 10, 4, 4),
            };
            _pillStyle.normal.background = MakeTexture(new Color(0.24f, 0.24f, 0.28f, 0.85f));
            _pillStyle.normal.textColor = Color.white;

            _pillActiveStyle = new GUIStyle(_pillStyle);
            _pillActiveStyle.normal.background = MakeTexture(new Color(0.30f, 0.62f, 0.36f, 0.92f));
            _pillActiveStyle.normal.textColor = Color.white;

            _stylesReady = true;
        }

        /// <summary>
        /// Creates a flat 2x2 texture. Tracked so OnDestroy can release them — GUIStyle
        /// textures are not garbage collected on their own.
        /// </summary>
        private Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(2, 2);
            var pixels = new Color[4];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            texture.hideFlags = HideFlags.DontSave;

            _textures.Add(texture);
            return texture;
        }
    }
}
