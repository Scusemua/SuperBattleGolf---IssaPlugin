using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Draws a fuel gauge bar while the local player has the Jetpack equipped.
    /// The bar depletes in real time while the player is thrusting and resets to
    /// full at the start of each new canister.
    ///
    /// Slot assignment: stacks above any active Freeze / LowGravity bars so the
    /// bars never overlap.
    ///
    /// Added to the Plugin's persistent GameObject in Plugin.cs.
    /// </summary>
    public class JetpackOverlay : MonoBehaviour
    {
        public static JetpackOverlay Instance { get; private set; }

        // ── Textures ──────────────────────────────────────────────────────
        private Texture2D _barBgTexture;
        private Texture2D _barFillTexture;
        private int _cachedBarW = -1;

        // Orange-amber fill — distinct from Freeze blue and LowGravity purple
        private static readonly Color FillColor = new Color(1.0f, 0.55f, 0.05f, 0.9f);
        private static readonly Color BgColor = new Color(0f, 0f, 0f, 0.55f);

        // Lazily initialised inside OnGUI (GUI.skin only valid during GUI events)
        private GUIStyle _labelStyle;

        // ── Unity lifecycle ───────────────────────────────────────────────

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            DestroyBarTextures();
        }

        // ── Rendering ─────────────────────────────────────────────────────

        private void OnGUI()
        {
            // Only show for the local player while the jetpack is their active item.
            var localInventory = NetworkClient.localPlayer?.GetComponent<PlayerInventory>();
            if (
                localInventory == null
                || localInventory.GetEffectivelyEquippedItem(true) != ItemRegistry.JetpackItemType
            )
                return;

            // Rebuild bar textures if the screen width changed.
            int barWInt = (int)EffectBarLayout.GetBarWidth();
            if (barWInt != _cachedBarW)
                RebuildBarTextures(barWInt);

            if (_barBgTexture == null || _barFillTexture == null)
                return;

            float fraction = JetpackItem.FuelFraction;

            float barW = EffectBarLayout.GetBarWidth();
            float barH = EffectBarLayout.BarHeight;
            float barX = EffectBarLayout.GetBarX();

            // Stack above any active world-effect bars so they never overlap.
            int slot = (FreezeItem.IsFrozen ? 1 : 0) + (LowGravityItem.IsActive ? 1 : 0);
            float barY = EffectBarLayout.GetBarY(slot);

            // Background rounded track (full width)
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), _barBgTexture);

            // Coloured fill — clipped to fill width so the right edge depletes cleanly
            // while the left rounded corners stay intact.
            GUI.BeginGroup(new Rect(barX, barY, barW * fraction, barH));
            GUI.DrawTexture(new Rect(0, 0, barW, barH), _barFillTexture);
            GUI.EndGroup();

            // Centred label
            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
            };
            _labelStyle.normal.textColor = Color.white;

            string label =
                $"Jetpack Fuel Remaining  {fraction * Configuration.JetpackFuelPerUse.Value:F1}s";
            GUI.Label(new Rect(barX, barY, barW, barH), label, _labelStyle);
        }

        // ── Texture helpers ───────────────────────────────────────────────

        private void RebuildBarTextures(int barW)
        {
            DestroyBarTextures();
            int barH = (int)EffectBarLayout.BarHeight;
            int radius = barH / 3;
            _barBgTexture = GenerateRoundedRectTexture(barW, barH, radius, BgColor);
            _barFillTexture = GenerateRoundedRectTexture(barW, barH, radius, FillColor);
            _cachedBarW = barW;
        }

        private void DestroyBarTextures()
        {
            if (_barBgTexture != null)
            {
                Destroy(_barBgTexture);
                _barBgTexture = null;
            }
            if (_barFillTexture != null)
            {
                Destroy(_barFillTexture);
                _barFillTexture = null;
            }
            _cachedBarW = -1;
        }

        private static Texture2D GenerateRoundedRectTexture(int w, int h, int radius, Color color)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var pixels = new Color[w * h];

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = IsInsideRoundedRect(x, y, w, h, radius) ? color : Color.clear;

            tex.SetPixels(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return tex;
        }

        private static bool IsInsideRoundedRect(int x, int y, int w, int h, int r)
        {
            bool inLeftZone = x < r;
            bool inRightZone = x >= w - r;
            bool inTopZone = y < r;
            bool inBotZone = y >= h - r;

            if (!((inLeftZone || inRightZone) && (inTopZone || inBotZone)))
                return true;

            int cx = inLeftZone ? r : (w - 1 - r);
            int cy = inTopZone ? r : (h - 1 - r);

            float dx = x - cx;
            float dy = y - cy;
            return dx * dx + dy * dy <= r * r;
        }
    }
}
