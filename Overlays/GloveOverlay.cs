using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Local HUD for the Glove: hold-time remaining bar and throw charge meter.
    /// </summary>
    public class GloveOverlay : MonoBehaviour
    {
        public static GloveOverlay Instance { get; private set; }

        private bool _holding;
        private float _holdStart;
        private float _holdDuration;

        private Texture2D _barBg;
        private Texture2D _holdFill;
        private Texture2D _chargeFill;
        private int _cachedBarW = -1;

        private GUIStyle _labelStyle;

        private static readonly Color HoldFillColor = new Color(0.95f, 0.85f, 0.2f, 0.9f);
        private static readonly Color ChargeFillColor = new Color(0.2f, 0.75f, 1f, 0.92f);
        private static readonly Color BgColor = new Color(0f, 0f, 0f, 0.55f);

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            DestroyTextures();
        }

        public void SetHolding(bool holding, float timeRemaining = 0f)
        {
            _holding = holding;
            if (holding)
            {
                _holdDuration = Mathf.Max(0.1f, timeRemaining);
                _holdStart = Time.time;
            }
        }

        public void ForceClose()
        {
            _holding = false;
        }

        private void OnGUI()
        {
            var bridge = GameManager.LocalPlayerInfo?.GetComponent<GloveNetworkBridge>();
            if (bridge == null || !bridge.IsHolding)
            {
                _holding = false;
                return;
            }

            _holding = true;
            if (_holdDuration <= 0f)
                _holdDuration = bridge.HoldDuration;

            int barWInt = (int)EffectBarLayout.GetBarWidth();
            if (barWInt != _cachedBarW)
                RebuildTextures(barWInt);
            if (_barBg == null)
                return;

            float holdElapsed =
                Time.time - (bridge.HoldStartTime > 0f ? bridge.HoldStartTime : _holdStart);
            float holdFraction = Mathf.Clamp01(
                1f - holdElapsed / Mathf.Max(0.1f, bridge.HoldDuration)
            );
            float holdRemaining = Mathf.Max(0f, bridge.HoldDuration - holdElapsed);

            float barW = EffectBarLayout.GetBarWidth();
            float barH = EffectBarLayout.BarHeight;
            float barX = EffectBarLayout.GetBarX();
            int slot = GetStackSlot();
            float barY = EffectBarLayout.GetBarY(slot);

            DrawBar(
                barX,
                barY,
                barW,
                barH,
                holdFraction,
                _holdFill,
                $"Ball Hold: {holdRemaining:F1}s"
            );

            if (bridge.IsCharging)
            {
                float chargeY = EffectBarLayout.GetBarY(slot + 1);
                DrawBar(
                    barX,
                    chargeY,
                    barW,
                    barH,
                    bridge.Charge01,
                    _chargeFill,
                    $"Throw Power: {Mathf.RoundToInt(bridge.Charge01 * 100f)}%"
                );
            }
        }

        private static int GetStackSlot()
        {
            var localInventory = NetworkClient.localPlayer?.GetComponent<PlayerInventory>();
            bool jetpackShowing =
                localInventory != null
                && localInventory.GetEffectivelyEquippedItem(true) == ItemRegistry.JetpackItemType;

            return (FreezeItem.IsFrozen ? 1 : 0)
                + (LowGravityItem.IsActive ? 1 : 0)
                + (WindStormOverlay.IsActive ? 1 : 0)
                + (NightOverlay.IsActive ? 1 : 0)
                + (jetpackShowing ? 1 : 0)
                + (SpinachBehaviour.IsActive ? 1 : 0);
        }

        private void DrawBar(
            float x,
            float y,
            float w,
            float h,
            float fraction,
            Texture2D fill,
            string label
        )
        {
            GUI.DrawTexture(new Rect(x, y, w, h), _barBg);
            GUI.BeginGroup(new Rect(x, y, w * Mathf.Clamp01(fraction), h));
            GUI.DrawTexture(new Rect(0, 0, w, h), fill);
            GUI.EndGroup();

            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 13,
                fontStyle = FontStyle.Bold,
            };
            _labelStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(x, y, w, h), label, _labelStyle);
        }

        private void RebuildTextures(int width)
        {
            DestroyTextures();
            _cachedBarW = width;
            _barBg = MakeTexture(width, (int)EffectBarLayout.BarHeight, BgColor);
            _holdFill = MakeTexture(width, (int)EffectBarLayout.BarHeight, HoldFillColor);
            _chargeFill = MakeTexture(width, (int)EffectBarLayout.BarHeight, ChargeFillColor);
        }

        private static Texture2D MakeTexture(int w, int h, Color color)
        {
            var tex = new Texture2D(Mathf.Max(1, w), Mathf.Max(1, h), TextureFormat.RGBA32, false);
            var pixels = new Color[tex.width * tex.height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private void DestroyTextures()
        {
            if (_barBg != null)
                Destroy(_barBg);
            if (_holdFill != null)
                Destroy(_holdFill);
            if (_chargeFill != null)
                Destroy(_chargeFill);
            _barBg = _holdFill = _chargeFill = null;
            _cachedBarW = -1;
        }
    }
}
