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

        // Every dimension below is authored against a 1080p-tall screen and multiplied
        // by the current scale at use. At raw pixel values the whole window renders at a
        // fraction of its intended apparent size on a 1440p or 4K display.
        //
        // The scaling is per-dimension rather than a GUI.matrix transform because this
        // window is interactive: GUILayout.Window's drag rect, the text field's caret,
        // and both scroll views hit-test in unscaled screen space, and a matrix scale
        // desynchronises those from what is drawn.
        private const float ReferenceHeight = 1080f;

        private const float IconSize = 48f;
        private const float CellHeight = 78f;
        private const float CellSpacing = 6f;
        private const float CaptionHeight = 26f;
        private const float MinCellWidth = 90f;
        private const float PlayerRowHeight = 30f;

        /// <summary>Window size at the reference height. The layout never feeds back into it.</summary>
        private const float WindowWidth = 720f;
        private const float WindowHeight = 520f;

        /// <summary>Roughly the width we want a cell to be; drives the column count.</summary>
        private const float TargetCellWidth = 140f;

        /// <summary>
        /// Width budget the grid gives up to window chrome and the vertical scrollbar.
        /// Deliberately generous: under-reserving leaves a stray horizontal scrollbar,
        /// while over-reserving only costs a little unused margin.
        /// </summary>
        private const float GridPadding = 60f;

        // Font sizes, also at the reference height.
        private const int LabelFontSize = 11;
        private const int CellLabelFontSize = 10;
        private const int SearchFontSize = 12;
        private const int TitleFontSize = 12;

        /// <summary>
        /// Scale from the reference layout to this screen. A configured value wins;
        /// 0 (the default) derives it from screen height.
        ///
        /// Only height is considered, on purpose. An ultra-wide monitor is wide but no
        /// taller than an ordinary one of the same vertical resolution, so scaling by
        /// width would inflate the window past the screen height on a 32:9 display.
        /// Never scales below 1: the layout has hand-tuned minimums that stop reading
        /// correctly when shrunk.
        /// </summary>
        private static float CurrentScale
        {
            get
            {
                float configured = ModConfig.Global.SpawnerUiScale.Value;
                if (configured > 0f) return configured;

                return Mathf.Max(1f, Screen.height / ReferenceHeight);
            }
        }

        /// <summary>
        /// Scale the styles were last built at, so a resolution change or a config edit
        /// rebuilds the font sizes rather than leaving them at the old size.
        /// </summary>
        private float _styleScale;

        /// <summary>
        /// Height of the draggable title bar, taken from the window style's own top
        /// padding when the styles are built. Read from the style rather than scaled
        /// independently so the drag strip can never cover a control row.
        /// </summary>
        private float _titleBarHeight = 20f;

        /// <summary>
        /// The scale for the frame being drawn. Sampled once per OnGUI so every pass of
        /// an IMGUI frame (layout, repaint, input) uses an identical value -- if layout
        /// and repaint disagreed, controls would be drawn off their own hitboxes.
        /// </summary>
        private float _scale = 1f;

        /// <summary>Footer summary, rebuilt only when the filter changes.</summary>
        private string _footerText = string.Empty;

        private bool _open;
        private Rect _windowRect = new Rect(120, 120, WindowWidth, WindowHeight);
        private Vector2 _scroll;
        private Vector2 _playerScroll;

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

            ReleaseTextures();
        }

        /// <summary>
        /// Destroys every texture minted for the current styles. Called both on teardown
        /// and before a style rebuild, since GUIStyle textures are not garbage collected.
        /// </summary>
        private void ReleaseTextures()
        {
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
            _footerText = $"{_filtered.Count} of {_catalog.Count} items";
        }

        private void OnGUI()
        {
            if (!_open) return;

            // One sample per frame, used by every pass. Reading the scale separately in
            // layout and repaint would let a mid-frame resolution change split them.
            _scale = CurrentScale;

            EnsureStyles();

            // Cap at the screen: a scaled-up window on a short screen would otherwise
            // push the footer and its Close button below the bottom edge.
            float windowW = Mathf.Min(WindowWidth * _scale, Screen.width);
            float windowH = Mathf.Min(WindowHeight * _scale, Screen.height);

            // Pin the size. GUILayout.Window otherwise auto-sizes to its content and
            // writes the result back into _windowRect, so any row even slightly too wide
            // grows the window, which widens the grid, which grows the window again --
            // the window visibly creeps rightward every frame. The size is always derived
            // from the constants and the scale, never from _windowRect, so no measured
            // value can feed back in.
            _windowRect.width = windowW;
            _windowRect.height = windowH;

            _windowRect = GUILayout.Window(
                WindowId,
                _windowRect,
                DrawWindow,
                "Item Spawner",
                _windowStyle,
                GUILayout.Width(windowW),
                GUILayout.Height(windowH));

            // Keep it on screen: with a fixed size it can otherwise be dragged mostly off.
            float edge = 80f * _scale;
            _windowRect.x = Mathf.Clamp(_windowRect.x, -windowW + edge, Screen.width - edge);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Screen.height - (40f * _scale));
        }

        private void DrawWindow(int id)
        {
            DrawControls();
            GUILayout.Space(6f * _scale);
            DrawGrid();
            GUILayout.Space(6f * _scale);
            DrawFooter();

            // Declared last on purpose. IMGUI hands an event to controls in declaration
            // order, so a drag strip declared first consumes clicks inside its bounds
            // before the controls under it ever run -- which is exactly what killed the
            // search box and the filter pills once the strip grew with the scale. Last,
            // it only picks up clicks no control claimed. The height comes from the
            // window style's own title-bar padding, so the two cannot drift apart.
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, _titleBarHeight));
        }

        private void DrawControls()
        {
            GUILayout.BeginHorizontal();

            GUILayout.Label("Search", _labelStyle, GUILayout.Width(52f * _scale));

            GUI.SetNextControlName(SearchControlName);
            string search = GUILayout.TextField(
                _search, _searchStyle, GUILayout.MinWidth(180f * _scale));
            _searchFocused = GUI.GetNameOfFocusedControl() == SearchControlName;
            if (search != _search)
            {
                _search = search;
                ApplyFilter();
            }

            if (GUILayout.Button("x", _pillStyle, GUILayout.Width(24f * _scale))
                && _search.Length > 0)
            {
                _search = string.Empty;
                GUI.FocusControl(null);
                ApplyFilter();
            }

            GUILayout.Space(10f * _scale);
            GUILayout.Label("Show", _labelStyle, GUILayout.Width(42f * _scale));

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
            GUILayout.Label("Give to", _labelStyle, GUILayout.Width(52f * _scale));

            if (_players.Count == 0)
            {
                GUILayout.Label("No players found.", _labelStyle);
                GUILayout.FlexibleSpace();
            }
            else
            {
                // Player pills scale with the lobby size and with name length, so this
                // row can exceed the window. Scroll it horizontally rather than letting
                // it dictate the window's width.
                _playerScroll = GUILayout.BeginScrollView(
                    _playerScroll,
                    false,
                    false,
                    GUI.skin.horizontalScrollbar,
                    GUIStyle.none,
                    GUIStyle.none,
                    GUILayout.Height(PlayerRowHeight * _scale));

                GUILayout.BeginHorizontal();
                for (int i = 0; i < _players.Count; i++)
                {
                    GUIStyle style = i == _playerIndex ? _pillActiveStyle : _pillStyle;
                    if (GUILayout.Button(_players[i].Name, style)) _playerIndex = i;
                }
                GUILayout.EndHorizontal();

                GUILayout.EndScrollView();
            }

            if (GUILayout.Button("Refresh", _pillStyle, GUILayout.Width(70f * _scale)))
                RefreshPlayers();

            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws the items as a grid. Column count is derived from the window width so
        /// the grid reflows when the window is resized, rather than being fixed.
        /// </summary>
        private void DrawGrid()
        {
            // Derive the viewport from the window rect, which is stable. Measuring the
            // scroll view's own inner width with an ExpandWidth probe fed back into
            // itself: the content width set the measurement and the measurement set the
            // content width, so the grid grew a little every frame.
            float viewport = _windowRect.width - (GridPadding * _scale);

            // Both sides of this division are scaled, so the column count stays the same
            // as at 1080p and the extra width goes into larger cells -- which is the
            // point: the grid should look identical, just bigger, not reflow into more
            // columns of the same tiny size.
            int columns = Mathf.Clamp(
                Mathf.FloorToInt(viewport / (TargetCellWidth * _scale)), 1, 6);

            // Cells must be an explicit, uniform width or IMGUI sizes each one to its own
            // label and the "grid" ends up as ragged columns that do not line up.
            // Rows lay out as (columns - 1) gaps between columns cells, so only the gaps
            // are subtracted -- charging every cell for a trailing gap overshoots the
            // viewport by one CellSpacing and reintroduces the horizontal scrollbar.
            float gaps = CellSpacing * _scale * (columns - 1);
            float cellWidth = Mathf.Max(MinCellWidth * _scale, (viewport - gaps) / columns);

            _scroll = GUILayout.BeginScrollView(
                _scroll,
                false,
                false,
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUILayout.ExpandHeight(true));

            if (_filtered.Count == 0)
            {
                GUILayout.Space(20f * _scale);
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

                    // Gap between columns only -- never after the last one.
                    if (column > 0) GUILayout.Space(CellSpacing * _scale);

                    if (index >= _filtered.Count)
                    {
                        // Reserve a real cell-sized gap so the last row lines up with the
                        // rows above. A FlexibleSpace would instead absorb all remaining
                        // width and stretch the row's real cells out of alignment.
                        GUILayout.Space(cellWidth);
                        continue;
                    }

                    DrawCell(_filtered[index], cellWidth);
                }

                GUILayout.EndHorizontal();
                GUILayout.Space(4f * _scale);
            }

            GUILayout.EndScrollView();
        }

        private void DrawCell(SpawnerItemCatalog.Entry entry, float width)
        {
            float iconSize = IconSize * _scale;

            GUILayout.BeginVertical(GUILayout.Width(width), GUILayout.Height(CellHeight * _scale));

            // Draw the button first, then blit the icon into it. Sprite.texture returns
            // the whole source texture, which for an atlased sprite is the entire atlas —
            // so the icon has to be drawn through its textureRect rather than handed to
            // GUIContent directly.
            bool clicked = GUILayout.Button(
                GUIContent.none,
                _cellStyle,
                GUILayout.Width(width),
                GUILayout.Height(iconSize + (8f * _scale)));

            Rect buttonRect = GUILayoutUtility.GetLastRect();
            Sprite icon = entry.Data.Icon;

            if (icon != null && icon.texture != null)
            {
                var iconRect = new Rect(
                    buttonRect.x + (buttonRect.width - iconSize) * 0.5f,
                    buttonRect.y + (buttonRect.height - iconSize) * 0.5f,
                    iconSize,
                    iconSize);

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

            GUILayout.Label(
                entry.DisplayName,
                _cellLabelStyle,
                GUILayout.Width(width),
                GUILayout.Height(CaptionHeight * _scale));
            GUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            GUILayout.BeginHorizontal();
            // Cached: OnGUI runs several times per frame (layout + repaint + input
            // events), and this is the one string that would otherwise be rebuilt on
            // every one of them while the window is open.
            GUILayout.Label(_footerText, _labelStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    "Close",
                    _pillStyle,
                    GUILayout.Width(90f * _scale),
                    GUILayout.Height(26f * _scale)))
                Close();
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
            // Font sizes bake the scale in, so a resolution change or a config edit has
            // to rebuild them -- otherwise the box grows and the text inside it does not.
            if (_stylesReady && Mathf.Approximately(_styleScale, _scale)) return;

            // This can now run more than once per scene (the scale changed), and every
            // style below mints fresh textures. Release the previous batch first or each
            // rebuild strands its predecessors -- GUIStyle textures are not collected on
            // their own. Safe on the first pass: the list is empty.
            ReleaseTextures();

            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                fontSize = Mathf.RoundToInt(TitleFontSize * _scale),
            };
            _windowStyle.normal.background = MakeTexture(new Color(0.10f, 0.10f, 0.12f, 0.94f));

            // The skin's window padding reserves the title bar (its .top) and the frame
            // inset. Left unscaled it stays at the stock ~20px while everything else
            // grows, so the title text overflows its bar and the first control row rides
            // up under it. Scale it with the rest of the layout.
            RectOffset skinPad = GUI.skin.window.padding;
            _windowStyle.padding = new RectOffset(
                Mathf.RoundToInt(skinPad.left * _scale),
                Mathf.RoundToInt(skinPad.right * _scale),
                Mathf.RoundToInt(skinPad.top * _scale),
                Mathf.RoundToInt(skinPad.bottom * _scale));

            // Border drives how the 9-slice background is stretched; scaling it keeps the
            // frame's corners proportional instead of leaving a hairline edge at 2x.
            RectOffset skinBorder = GUI.skin.window.border;
            _windowStyle.border = new RectOffset(
                Mathf.RoundToInt(skinBorder.left * _scale),
                Mathf.RoundToInt(skinBorder.right * _scale),
                Mathf.RoundToInt(skinBorder.top * _scale),
                Mathf.RoundToInt(skinBorder.bottom * _scale));

            // The draggable strip must match the title bar the padding just reserved.
            // Deriving it here rather than scaling a separate constant is the whole fix
            // for the dead controls: an independently scaled drag rect grew taller than
            // the title bar and swallowed the entire search/filter row, because
            // GUI.DragWindow consumes those clicks before any control sees them.
            _titleBarHeight = _windowStyle.padding.top;

            int cellPad = Mathf.RoundToInt(4f * _scale);
            _cellStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(cellPad, cellPad, cellPad, cellPad),
                imagePosition = ImagePosition.ImageOnly,
            };
            _cellStyle.normal.background = MakeTexture(new Color(0.24f, 0.24f, 0.28f, 0.85f));
            _cellStyle.hover.background = MakeTexture(new Color(0.36f, 0.36f, 0.42f, 0.95f));
            _cellStyle.active.background = MakeTexture(new Color(0.18f, 0.18f, 0.22f, 0.95f));

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(LabelFontSize * _scale),
                wordWrap = false,
            };
            _labelStyle.normal.textColor = Color.white;

            // Cell captions get their own style: item names like "Rocket Tether Grenade"
            // are wider than a cell, so these wrap and clip rather than bleeding into
            // the neighbouring column the way the non-wrapping label style would.
            _cellLabelStyle = new GUIStyle(_labelStyle)
            {
                wordWrap = true,
                fontSize = Mathf.RoundToInt(CellLabelFontSize * _scale),
                clipping = TextClipping.Clip,
                alignment = TextAnchor.UpperCenter,
            };

            _searchStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = Mathf.RoundToInt(SearchFontSize * _scale),
            };

            int pillPadX = Mathf.RoundToInt(10f * _scale);
            int pillPadY = Mathf.RoundToInt(4f * _scale);
            _pillStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(LabelFontSize * _scale),
                padding = new RectOffset(pillPadX, pillPadX, pillPadY, pillPadY),
            };
            _pillStyle.normal.background = MakeTexture(new Color(0.24f, 0.24f, 0.28f, 0.85f));
            _pillStyle.normal.textColor = Color.white;

            _pillActiveStyle = new GUIStyle(_pillStyle);
            _pillActiveStyle.normal.background = MakeTexture(new Color(0.30f, 0.62f, 0.36f, 0.92f));
            _pillActiveStyle.normal.textColor = Color.white;

            _styleScale = _scale;
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
