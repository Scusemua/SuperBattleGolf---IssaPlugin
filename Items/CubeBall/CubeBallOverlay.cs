using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// HUD overlay for the Cube Ball item.
    ///
    /// Shows a target-selection panel when the player uses the item.  On click
    /// the CubeBallRequestMessage is sent to the server and the panel closes.
    public class CubeBallOverlay : MonoBehaviour
    {
        public static CubeBallOverlay Instance { get; private set; }

        // ── State ─────────────────────────────────────────────────────────────

        private bool _isOpen;
        private int _equippedSlotIndex;
        private readonly List<PlayerInfo> _cachedPlayers = new List<PlayerInfo>();

        // ── Layout constants ──────────────────────────────────────────────────

        private const float PanelWidth = 500f;
        private const float RowHeight = 42f;
        private const float HeaderH = 48f;
        private const float TipH = 22f;
        private const float Padding = 14f;
        private const float ButtonW = 84f;
        private const float ButtonH = 30f;

        // ── Styles (lazy-initialised) ─────────────────────────────────────────

        private bool _stylesReady;
        private Texture2D _panelBgTex;
        private Texture2D _selectOnTex;
        private Texture2D _selectHovTex;
        private Texture2D _cancelBgTex;
        private Texture2D _cancelHovTex;
        private GUIStyle _panelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _tipStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _subStyle;
        private GUIStyle _selectBtnStyle;
        private GUIStyle _cancelStyle;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            Object.Destroy(_panelBgTex);
            Object.Destroy(_selectOnTex);
            Object.Destroy(_selectHovTex);
            Object.Destroy(_cancelBgTex);
            Object.Destroy(_cancelHovTex);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// Opens the chooser panel (called from CubeBallItemDefinition.OnUse).
        public void OpenChooser(int equippedSlotIndex)
        {
            _equippedSlotIndex = equippedSlotIndex;
            _isOpen = true;
            RefreshPlayerList();
        }

        /// Closes the panel without sending a request (hole transition / item lost).
        public void ForceClose() => _isOpen = false;

        // ── GUI ───────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_isOpen)
                return;

            EnsureStyles();
            DrawChooser();
        }

        private void DrawChooser()
        {
            int rowCount = Mathf.Max(1, _cachedPlayers.Count);
            float panelH =
                HeaderH + TipH + Padding + RowHeight * rowCount + Padding + ButtonH + Padding;

            float px = (Screen.width - PanelWidth) * 0.5f;
            float py = (Screen.height - panelH) * 0.5f;

            GUI.Box(new Rect(px, py, PanelWidth, panelH), GUIContent.none, _panelStyle);

            float cy = py + Padding * 0.5f;

            GUI.Label(
                new Rect(px, cy, PanelWidth, HeaderH),
                "Cube Ball — Choose Target",
                _titleStyle
            );
            cy += HeaderH;

            GUI.Label(
                new Rect(px, cy, PanelWidth, TipH),
                "Select a player to turn their ball into a cube",
                _tipStyle
            );
            cy += TipH + Padding;

            if (_cachedPlayers.Count == 0)
            {
                GUI.Label(
                    new Rect(px + Padding, cy, PanelWidth - Padding * 2f, RowHeight),
                    "No other players in the session.",
                    _rowStyle
                );
                cy += RowHeight;
            }
            else
            {
                float labelW = PanelWidth - Padding * 2f - ButtonW - 8f;

                foreach (var player in _cachedPlayers)
                {
                    string nameLabel = player.PlayerId.PlayerName;
                    string infoLabel = BuildInfoLabel(player);
                    float btnY = cy + (RowHeight - ButtonH) * 0.5f;

                    GUI.Label(
                        new Rect(px + Padding, cy, labelW, RowHeight * 0.55f),
                        nameLabel,
                        _rowStyle
                    );
                    GUI.Label(
                        new Rect(px + Padding, cy + RowHeight * 0.52f, labelW, RowHeight * 0.48f),
                        infoLabel,
                        _subStyle
                    );

                    if (
                        GUI.Button(
                            new Rect(px + Padding + labelW + 8f, btnY, ButtonW, ButtonH),
                            "CUBIFY",
                            _selectBtnStyle
                        )
                    )
                    {
                        var identity = player.GetComponent<NetworkIdentity>();
                        if (identity != null)
                        {
                            NetworkClient.Send(
                                new CubeBallRequestMessage
                                {
                                    TargetNetId = identity.netId,
                                    EquippedSlotIndex = _equippedSlotIndex,
                                }
                            );
                            _isOpen = false;
                        }
                    }

                    cy += RowHeight;
                }
            }

            cy += Padding;

            float cancelW = 110f;
            if (
                GUI.Button(
                    new Rect(px + (PanelWidth - cancelW) * 0.5f, cy, cancelW, ButtonH),
                    "Cancel",
                    _cancelStyle
                )
            )
            {
                _isOpen = false;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void RefreshPlayerList()
        {
            _cachedPlayers.Clear();
            var remotes = GameManager.RemotePlayers;
            if (remotes != null)
                _cachedPlayers.AddRange(remotes);
        }

        private static string BuildInfoLabel(PlayerInfo player)
        {
            var localPos = GameManager.LocalPlayerInfo?.transform.position ?? Vector3.zero;
            float dist = Vector3.Distance(localPos, player.transform.position);

            Vector3? holePos = GolfHoleManager.MainHole?.transform.position;
            string holeStr = holePos.HasValue
                ? $"{Mathf.RoundToInt(Vector3.Distance(holePos.Value, player.transform.position))}m to hole"
                : "? to hole";

            return $"{Mathf.RoundToInt(dist)}m away  ·  {holeStr}";
        }

        // ── Style setup ───────────────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_stylesReady)
                return;
            _stylesReady = true;

            _panelBgTex = Tex(new Color(0.06f, 0.06f, 0.16f, 0.93f));
            _selectOnTex = Tex(new Color(0.70f, 0.35f, 0.10f, 1.00f));
            _selectHovTex = Tex(new Color(0.90f, 0.50f, 0.15f, 1.00f));
            _cancelBgTex = Tex(new Color(0.40f, 0.10f, 0.10f, 1.00f));
            _cancelHovTex = Tex(new Color(0.60f, 0.15f, 0.15f, 1.00f));

            _panelStyle = new GUIStyle(GUI.skin.box) { normal = { background = _panelBgTex } };

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 21,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };

            _tipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.60f, 0.60f, 0.60f) },
            };

            _rowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.LowerLeft,
                normal = { textColor = Color.white },
            };

            _subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.UpperLeft,
                normal = { textColor = new Color(0.85f, 0.65f, 0.40f) },
            };

            _selectBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _selectOnTex, textColor = Color.white },
                hover = { background = _selectHovTex, textColor = Color.white },
            };

            _cancelStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _cancelBgTex, textColor = Color.white },
                hover = { background = _cancelHovTex, textColor = Color.white },
            };
        }

        private static Texture2D Tex(Color col)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, col);
            t.Apply();
            return t;
        }
    }
}
