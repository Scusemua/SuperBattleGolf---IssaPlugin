using IssaPlugin.Items;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// Remaining rounds while the minigun is equipped. Lower left.
    public class MinigunAmmoOverlay : MonoBehaviour
    {
        private GUIStyle _titleStyle;
        private GUIStyle _countStyle;
        private Texture2D _panelTex;

        // Lowest remaining seen during this trigger hold. The first accepted server
        // decrement syncs back and wipes the local override, which jumps the live
        // count up. The readout keeps falling through that.
        private int _displayRemaining = -1;

        private const float ReferenceHeight = 1080f;
        private const float PanelWidth = 240f;
        private const float PanelHeight = 88f;
        private const float MarginX = 28f;
        private const float MarginY = 36f;

        private static float UiScale => Mathf.Max(1f, Screen.height / ReferenceHeight);

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var inventory = GameManager.LocalPlayerInfo?.Inventory;
            if (
                inventory == null
                || inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.MinigunItemType
            )
            {
                _displayRemaining = -1;
                return;
            }

            int remaining = ShownRemaining(inventory);
            int full = Mathf.Max(1, (int)ModConfig.Minigun.Uses.Value);
            EnsureStyles();

            float scale = UiScale;
            float panelW = PanelWidth * scale;
            float panelH = PanelHeight * scale;
            var panel = new Rect(
                MarginX * scale,
                Screen.height - (MarginY * scale) - panelH,
                panelW,
                panelH
            );

            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(panel, _panelTex, ScaleMode.StretchToFill);
            GUI.color = Color.white;

            float pad = 14f * scale;
            GUI.Label(
                new Rect(panel.x + pad, panel.y + 8f * scale, panel.width - pad * 2f, 28f * scale),
                "MINIGUN",
                _titleStyle
            );
            GUI.Label(
                new Rect(panel.x + pad, panel.y + 36f * scale, panel.width - pad * 2f, 40f * scale),
                remaining + " / " + full,
                _countStyle
            );
        }

        private int ShownRemaining(PlayerInventory inventory)
        {
            int live = ItemRegistry.GetEffectiveRemainingUses(
                inventory,
                inventory.EquippedItemIndex
            );

            // While the trigger is down, ignore a count that climbed. That climb is
            // the synced slot replacing the local override, not ammo coming back.
            if (
                !Firearm.IsHolding(inventory, ItemRegistry.MinigunItemType)
                || _displayRemaining < 0
            )
                _displayRemaining = live;
            else if (live < _displayRemaining)
                _displayRemaining = live;

            return _displayRemaining;
        }

        private void EnsureStyles()
        {
            if (_panelTex == null)
            {
                _panelTex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _panelTex.SetPixel(0, 0, Color.white);
                _panelTex.Apply();
            }

            int titleSize = Mathf.RoundToInt(16f * UiScale);
            int countSize = Mathf.RoundToInt(28f * UiScale);

            if (_titleStyle == null || _titleStyle.fontSize != titleSize)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = titleSize,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                };
                _titleStyle.normal.textColor = new Color(0.85f, 0.92f, 1f, 1f);
            }

            if (_countStyle == null || _countStyle.fontSize != countSize)
            {
                _countStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = countSize,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleLeft,
                };
                _countStyle.normal.textColor = Color.white;
            }
        }

        private void OnDestroy()
        {
            if (_panelTex != null)
                Destroy(_panelTex);
        }
    }
}
