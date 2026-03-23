using IssaPlugin.Items;
using IssaPlugin.Network;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// Renders the voting UI for all players during an active vote session.
    /// Visible whenever VoteManager.VoteActive is true.
    public class VoteOverlay : MonoBehaviour
    {
        // ── Layout constants ─────────────────────────────────────────────────────
        private const float PanelWidth  = 520f;
        private const float RowHeight   = 36f;
        private const float HeaderH     = 52f;
        private const float TimerH      = 28f;
        private const float SubmitH     = 46f;
        private const float Padding     = 14f;
        private const float ButtonW     = 54f;
        private const float ButtonH     = 28f;
        private const float ButtonGap   = 4f;

        // ── Styles / textures (lazy-initialised once) ────────────────────────────
        private bool _stylesReady;
        private GUIStyle _panelStyle, _titleStyle, _timerStyle, _waitStyle;
        private GUIStyle _itemStyle;
        private GUIStyle _yesOn, _yesOff, _noOn, _noOff, _submitStyle;

        private void OnGUI()
        {
            if (!VoteManager.VoteActive) return;

            EnsureStyles();

            int itemCount = ItemRegistry.AllItems.Count;
            float panelH  = HeaderH + TimerH + Padding
                           + RowHeight * itemCount + Padding
                           + SubmitH + Padding;

            float px = (Screen.width  - PanelWidth) * 0.5f;
            float py = (Screen.height - panelH)      * 0.5f;

            GUI.Box(new Rect(px, py, PanelWidth, panelH), GUIContent.none, _panelStyle);

            float cy = py + Padding * 0.5f;

            // ── Title ────────────────────────────────────────────────────────────
            GUI.Label(new Rect(px, cy, PanelWidth, HeaderH), "Vote: Enable Custom Items?", _titleStyle);
            cy += HeaderH;

            // ── Timer / waiting line ─────────────────────────────────────────────
            bool submitted = VoteManager.LocalVoteSubmitted;
            string timerText = submitted
                ? "Waiting for other players..."
                : $"Time remaining: {Mathf.CeilToInt(VoteManager.VoteTimeRemaining)}s";
            GUI.Label(new Rect(px, cy, PanelWidth, TimerH), timerText,
                submitted ? _waitStyle : _timerStyle);
            cy += TimerH + Padding;

            // ── Item rows ────────────────────────────────────────────────────────
            float labelW  = PanelWidth - Padding * 2f - ButtonW * 2f - ButtonGap - 8f;
            float btnBaseX = px + Padding + labelW + 8f;

            foreach (var item in ItemRegistry.AllItems)
            {
                int  key      = (int)item.ItemType;
                bool votedYes = VoteManager.LocalVotes.TryGetValue(key, out bool v) && v;

                float btnY = cy + (RowHeight - ButtonH) * 0.5f;

                GUI.Label(new Rect(px + Padding, cy, labelW, RowHeight), item.DisplayName, _itemStyle);

                if (!submitted)
                {
                    if (GUI.Button(new Rect(btnBaseX,              btnY, ButtonW, ButtonH), "YES",
                            votedYes ? _yesOn : _yesOff))
                        VoteManager.LocalVotes[key] = true;

                    if (GUI.Button(new Rect(btnBaseX + ButtonW + ButtonGap, btnY, ButtonW, ButtonH), "NO",
                            !votedYes ? _noOn : _noOff))
                        VoteManager.LocalVotes[key] = false;
                }
                else
                {
                    // Non-interactive display after submission
                    GUI.Label(new Rect(btnBaseX,              btnY, ButtonW, ButtonH), "YES",
                        votedYes ? _yesOn : _yesOff);
                    GUI.Label(new Rect(btnBaseX + ButtonW + ButtonGap, btnY, ButtonW, ButtonH), "NO",
                        !votedYes ? _noOn : _noOff);
                }

                cy += RowHeight;
            }

            cy += Padding;

            // ── Submit button ────────────────────────────────────────────────────
            if (!submitted)
            {
                float submitW = 140f;
                if (GUI.Button(new Rect(px + (PanelWidth - submitW) * 0.5f, cy, submitW, SubmitH),
                        "Submit Vote", _submitStyle))
                    VoteManager.SubmitLocalVotes();
            }
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            var panelBg  = Tex(new Color(0.06f, 0.06f, 0.14f, 0.93f));
            var greenOn  = Tex(new Color(0.10f, 0.55f, 0.10f, 1.00f));
            var greenHov = Tex(new Color(0.15f, 0.70f, 0.15f, 1.00f));
            var redOn    = Tex(new Color(0.55f, 0.10f, 0.10f, 1.00f));
            var redHov   = Tex(new Color(0.70f, 0.15f, 0.15f, 1.00f));
            var dim      = Tex(new Color(0.28f, 0.28f, 0.28f, 0.85f));
            var blue     = Tex(new Color(0.15f, 0.35f, 0.75f, 1.00f));
            var blueHov  = Tex(new Color(0.25f, 0.50f, 1.00f, 1.00f));

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = panelBg }
            };

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = Color.white }
            };

            _timerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 13,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.85f, 0.85f, 0.85f) }
            };

            _waitStyle = new GUIStyle(_timerStyle)
            {
                normal = { textColor = new Color(1f, 0.85f, 0.3f) }
            };

            _itemStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = 14,
                alignment = TextAnchor.MiddleLeft,
                normal    = { textColor = Color.white }
            };

            _yesOn = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 12, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { background = greenOn,  textColor = Color.white },
                hover     = { background = greenHov, textColor = Color.white },
            };
            _yesOff = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 12,
                alignment = TextAnchor.MiddleCenter,
                normal    = { background = dim,     textColor = new Color(0.6f, 0.6f, 0.6f) },
                hover     = { background = greenOn, textColor = Color.white },
            };
            _noOn = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 12, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { background = redOn,  textColor = Color.white },
                hover     = { background = redHov, textColor = Color.white },
            };
            _noOff = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 12,
                alignment = TextAnchor.MiddleCenter,
                normal    = { background = dim,   textColor = new Color(0.6f, 0.6f, 0.6f) },
                hover     = { background = redOn, textColor = Color.white },
            };

            _submitStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize  = 16, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { background = blue,    textColor = Color.white },
                hover     = { background = blueHov, textColor = Color.white },
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
