using System.Collections.Generic;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// HUD overlay for the Position Swap item.
    ///
    /// States:
    ///   Chooser  — visible while the player is selecting a swap target.
    ///   Pending  — countdown shown after a target has been confirmed by the server.
    ///   Hidden   — default; no swap in progress.
    /// </summary>
    public class PositionSwapOverlay : MonoBehaviour
    {
        public static PositionSwapOverlay Instance { get; private set; }

        // ── State ─────────────────────────────────────────────────────────────────

        private bool _isChooserOpen;
        private bool _isPendingSwap;
        private float _pendingCountdown;
        private readonly List<PlayerInfo> _cachedPlayers = new List<PlayerInfo>();

        // ── Layout constants ──────────────────────────────────────────────────────

        private const float PanelWidth = 500f;
        private const float RowHeight = 42f;
        private const float HeaderH = 48f;
        private const float TipH = 22f;
        private const float Padding = 14f;
        private const float ButtonW = 84f;
        private const float ButtonH = 30f;

        // ── Styles (lazy-initialised) ─────────────────────────────────────────────

        private bool _areStylesReady;
        private Texture2D _panelBgTex;
        private Texture2D _swapOnTex;
        private Texture2D _swapHovTex;
        private Texture2D _cancelBgTex;
        private Texture2D _cancelHovTex;
        private GUIStyle _panelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _tipStyle;
        private GUIStyle _rowStyle;
        private GUIStyle _subStyle;
        private GUIStyle _swapBtnStyle;
        private GUIStyle _cancelStyle;
        private GUIStyle _countdownStyle;

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            Object.Destroy(_panelBgTex);
            Object.Destroy(_swapOnTex);
            Object.Destroy(_swapHovTex);
            Object.Destroy(_cancelBgTex);
            Object.Destroy(_cancelHovTex);
        }

        private void Update()
        {
            if (!_isPendingSwap)
                return;
            _pendingCountdown -= Time.deltaTime;
            if (_pendingCountdown < 0f)
                _pendingCountdown = 0f;
        }

        // ── Public API ────────────────────────────────────────────────────────────

        /// Opens the player-selection panel (called from PositionSwapItemDefinition.OnUse).
        public void OpenChooser()
        {
            _isChooserOpen = true;
            _isPendingSwap = false;
            RefreshPlayerList();
        }

        private void RefreshPlayerList()
        {
            _cachedPlayers.Clear();
            var remotes = GameManager.RemotePlayers;
            if (remotes != null)
                _cachedPlayers.AddRange(remotes);
        }

        /// Called when the server acknowledges a swap request and starts the delay.
        /// Only switches to the countdown state for the client that initiated the swap.
        public void OnSwapWarning(uint initiatorNetId, uint targetNetId, float delay)
        {
            var localIdentity = GameManager.LocalPlayerInfo?.GetComponent<NetworkIdentity>();
            if (localIdentity != null && localIdentity.netId == initiatorNetId)
            {
                _isChooserOpen = false;
                _isPendingSwap = true;
                _pendingCountdown = delay;
            }
        }

        /// Called on all clients when the swap executes.
        public void OnSwapExecuted(uint initiatorNetId, uint targetNetId)
        {
            _isChooserOpen = false;
            _isPendingSwap = false;
        }

        /// Forces all overlay state closed unconditionally (used by hole-transition cleanup).
        public void ForceClose()
        {
            _isChooserOpen = false;
            _isPendingSwap = false;
        }

        /// Called on all clients when the server cancels a pending swap.
        /// Only closes the UI if the local player was the initiator.
        public void OnSwapCancelled(uint initiatorNetId)
        {
            var localIdentity = GameManager.LocalPlayerInfo?.GetComponent<NetworkIdentity>();
            if (localIdentity == null || localIdentity.netId != initiatorNetId)
                return;

            _isChooserOpen = false;
            _isPendingSwap = false;
        }

        // ── GUI ───────────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_isChooserOpen && !_isPendingSwap)
                return;

            EnsureStyles();

            if (_isPendingSwap)
                DrawCountdown();
            else
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
                "Position Swap — Choose Target",
                _titleStyle
            );
            cy += HeaderH;

            GUI.Label(
                new Rect(px, cy, PanelWidth, TipH),
                "Select a player to swap positions with (pause for a cursor)",
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
                var localPos = GameManager.LocalPlayerInfo?.transform.position ?? Vector3.zero;
                Vector3? holePos = GolfHoleManager.MainHole?.transform.position;

                float labelW = PanelWidth - Padding * 2f - ButtonW - 8f;

                foreach (var player in _cachedPlayers)
                {
                    bool inCart = player.ActiveGolfCartSeat.IsValid();
                    float distFromMe = Vector3.Distance(localPos, player.transform.position);

                    string nameLabel = player.PlayerId.PlayerName;
                    string infoLabel;
                    if (inCart)
                    {
                        infoLabel = $"{Mathf.RoundToInt(distFromMe)}m away  ·  In a golf cart";
                    }
                    else
                    {
                        string holeStr = holePos.HasValue
                            ? $"{Mathf.RoundToInt(Vector3.Distance(holePos.Value, player.transform.position))}m to hole"
                            : "? to hole";
                        infoLabel = $"{Mathf.RoundToInt(distFromMe)}m away  ·  {holeStr}";
                    }

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

                    GUI.enabled = !inCart;
                    if (
                        GUI.Button(
                            new Rect(px + Padding + labelW + 8f, btnY, ButtonW, ButtonH),
                            inCart ? "In cart" : "SWAP",
                            _swapBtnStyle
                        )
                    )
                    {
                        var identity = player.GetComponent<NetworkIdentity>();
                        if (identity != null)
                        {
                            NetworkClient.Send(
                                new PositionSwapRequestMessage { TargetNetId = identity.netId }
                            );
                            _isChooserOpen = false;
                        }
                    }
                    GUI.enabled = true;

                    cy += RowHeight;
                }
            }

            cy += Padding;

            // Cancel button
            float cancelW = 110f;
            if (
                GUI.Button(
                    new Rect(px + (PanelWidth - cancelW) * 0.5f, cy, cancelW, ButtonH),
                    "Cancel",
                    _cancelStyle
                )
            )
            {
                _isChooserOpen = false;
            }
        }

        private void DrawCountdown()
        {
            const float W = 360f;
            const float H = 76f;
            float px = (Screen.width - W) * 0.5f;
            float py = Screen.height * 0.18f;

            GUI.Box(new Rect(px, py, W, H), GUIContent.none, _panelStyle);

            string text =
                _pendingCountdown > 0.05f ? $"Swapping in {_pendingCountdown:F1}s..." : "Swapping!";
            GUI.Label(new Rect(px, py, W, H), text, _countdownStyle);
        }

        // ── Style setup ───────────────────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_areStylesReady)
                return;
            _areStylesReady = true;

            _panelBgTex = Tex(new Color(0.06f, 0.06f, 0.16f, 0.93f));
            _swapOnTex = Tex(new Color(0.15f, 0.50f, 0.90f, 1.00f));
            _swapHovTex = Tex(new Color(0.25f, 0.65f, 1.00f, 1.00f));
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
                normal = { textColor = new Color(0.65f, 0.75f, 0.95f) },
            };

            _swapBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _swapOnTex, textColor = Color.white },
                hover = { background = _swapHovTex, textColor = Color.white },
            };

            _cancelStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = _cancelBgTex, textColor = Color.white },
                hover = { background = _cancelHovTex, textColor = Color.white },
            };

            _countdownStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.90f, 0.30f) },
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
