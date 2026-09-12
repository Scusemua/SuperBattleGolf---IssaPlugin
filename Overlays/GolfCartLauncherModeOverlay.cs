using IssaPlugin.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// HUD indicator shown while the local player holds the Golf Cart Launcher.
    ///
    /// Displays the active firing mode and the key that toggles it:
    ///
    ///   STANDARD — the cart is launched empty (the normal weapon).
    ///   JOY RIDE — the shooter is seated in the cart as it launches, spending the
    ///              whole item.
    ///
    /// JOY RIDE is only available at full uses, since firing it consumes every
    /// remaining use. When it is unavailable the row is dimmed and annotated, so the
    /// player can see the mode exists and why they cannot pick it.
    ///
    /// The toggle key is read from ModConfig rather than hardcoded, so the label always
    /// matches what the player has bound. It is a mod key (not a base-game action)
    /// because every action in the game's own Gameplay/Hotkeys/Ingame maps is already
    /// assigned to a real function — there is no free binding to adopt.
    ///
    /// Also owns the toggle polling: this component already runs every frame only for
    /// the local player, so it avoids a second per-frame hook elsewhere.
    /// </summary>
    public class GolfCartLauncherModeOverlay : MonoBehaviour
    {
        private GUIStyle _modeStyle;
        private GUIStyle _hintStyle;
        private Texture2D _panelTex;

        // Matches the bottom-left placement of the mod's other persistent readouts.
        // Sizes are expressed at a 1080p reference height and scaled to the actual
        // screen, so the panel stays legible on high-resolution displays instead of
        // shrinking to unreadable pixels.
        private const float ReferenceHeight = 1080f;
        private const float PanelWidth = 732f;
        private const float PanelHeight = 150f;
        private const float MarginX = 28f;
        private const float MarginY = 110f;
        private const int ModeFontSize = 28;
        private const int HintFontSize = 22;

        /// <summary>Scale factor from the 1080p reference layout to this screen.</summary>
        private static float UiScale => Mathf.Max(1f, Screen.height / ReferenceHeight);

        private static readonly Color StandardColor = new(0.82f, 0.86f, 0.92f, 1f);
        private static readonly Color JoyrideColor = new(1f, 0.78f, 0.22f, 1f);
        private static readonly Color DisabledColor = new(0.55f, 0.57f, 0.62f, 1f);
        private static readonly Color HintColor = Color.white;

        private void Update()
        {
            var inventory = GameManager.LocalPlayerInfo?.Inventory;
            if (inventory == null)
                return;

            if (
                inventory.GetEffectivelyEquippedItem(true)
                != ItemRegistry.GolfCartLauncherItemType
            )
            {
                // Never leave the mode armed on an item the player is no longer holding.
                GolfCartLauncherItem.DisarmJoyride();
                return;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            Key toggleKey = ModConfig.GolfCartLauncher.JoyrideToggleKey.Value;
            if (toggleKey == Key.None)
                return;

            if (keyboard[toggleKey].wasPressedThisFrame)
                GolfCartLauncherItem.ToggleJoyride(inventory);
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            if (!ModConfig.GolfCartLauncher.ShowJoyrideHud.Value)
                return;

            var inventory = GameManager.LocalPlayerInfo?.Inventory;
            if (inventory == null)
                return;

            if (
                inventory.GetEffectivelyEquippedItem(true)
                != ItemRegistry.GolfCartLauncherItemType
            )
                return;

            EnsureStyles();

            bool armed = GolfCartLauncherItem.JoyrideArmed;
            bool canArm = GolfCartLauncherItem.CanArmJoyride(inventory);

            float scale = UiScale;
            float panelW = PanelWidth * scale;
            float panelH = PanelHeight * scale;
            float x = MarginX * scale;
            float y = Screen.height - (MarginY * scale) - panelH;
            var panel = new Rect(x, y, panelW, panelH);

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(panel, _panelTex, ScaleMode.StretchToFill);
            GUI.color = Color.white;

            string modeLabel = armed ? "JOY RIDE" : "STANDARD";
            _modeStyle.normal.textColor = armed ? JoyrideColor : StandardColor;

            GUI.Label(
                new Rect(
                    panel.x + 14f * scale,
                    panel.y + 8f * scale,
                    panel.width - 28f * scale,
                    34f * scale
                ),
                $"FIRING MODE: {modeLabel}",
                _modeStyle
            );

            // Hint line: how to toggle, or why the player cannot.
            string hint;
            if (!canArm && !armed)
            {
                _hintStyle.normal.textColor = DisabledColor;
                hint = "JOY RIDE needs full ammo";
            }
            else
            {
                _hintStyle.normal.textColor = HintColor;
                hint = $"[Press {KeyLabel()}] Toggle Firing Mode";
            }

            GUI.Label(
                new Rect(
                    panel.x + 14f * scale,
                    panel.y + 44f * scale,
                    panel.width - 28f * scale,
                    30f * scale
                ),
                hint,
                _hintStyle
            );
        }

        /// <summary>
        /// Human-readable name of the configured toggle key. Unity's Key enum uses
        /// names like "Digit1"/"NumpadPlus" that read poorly on a HUD, so the common
        /// ones are tidied up.
        /// </summary>
        private static string KeyLabel()
        {
            Key key = ModConfig.GolfCartLauncher.JoyrideToggleKey.Value;
            if (key == Key.None)
                return "UNBOUND";

            string name = key.ToString();

            if (name.StartsWith("Digit"))
                return name.Substring(5);
            if (name.StartsWith("Numpad"))
                return "NUM " + name.Substring(6).ToUpperInvariant();

            return name.ToUpperInvariant();
        }

        private void EnsureStyles()
        {
            if (_panelTex == null)
            {
                _panelTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _panelTex.SetPixel(0, 0, Color.white);
                _panelTex.Apply();
            }

            // Font sizes track the screen scale, so they are rebuilt when it changes
            // (resolution switch, windowed/fullscreen toggle).
            int modeSize = Mathf.RoundToInt(ModeFontSize * UiScale);
            int hintSize = Mathf.RoundToInt(HintFontSize * UiScale);

            if (_modeStyle == null || _modeStyle.fontSize != modeSize)
                _modeStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = modeSize,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                };

            if (_hintStyle == null || _hintStyle.fontSize != hintSize)
                _hintStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = hintSize,
                    alignment = TextAnchor.MiddleLeft,
                };
        }

        private void OnDestroy()
        {
            if (_panelTex != null)
                Destroy(_panelTex);
        }
    }
}
