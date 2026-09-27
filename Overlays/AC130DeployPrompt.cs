using IssaPlugin.Items;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Choice shown when the local player uses an AC130.
    /// The item stays equipped until they pick a mode. Escape closes the prompt
    /// and leaves the item in hand.
    /// </summary>
    public class AC130DeployPrompt : MonoBehaviour
    {
        private static AC130DeployPrompt _instance;

        private bool _visible;
        private int _suppressSlotSwitchFrame = -1;
        private GUIStyle _panelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _lineStyle;
        private Texture2D _panelTex;

        private const float ReferenceHeight = 1080f;

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            if (_panelTex != null)
                Destroy(_panelTex);
        }

        public static void Show()
        {
            if (_instance == null)
                return;

            _instance._visible = true;
        }

        public static void Hide()
        {
            if (_instance == null)
                return;

            _instance._visible = false;
        }

        /// <summary>
        /// True while the prompt is up, and for the rest of the frame a choice is made.
        /// Number keys also select hotbar slots, so the slot change has to stay blocked
        /// after the prompt hides or it still swaps in the golf club.
        /// </summary>
        public static bool SuppressesSlotSwitch =>
            _instance != null
            && (
                _instance._visible
                || _instance._suppressSlotSwitchFrame == Time.frameCount
            );

        private void Update()
        {
            if (!_visible)
                return;

            if (!IsHoldingAC130())
            {
                Hide();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.digit1Key.wasPressedThisFrame || keyboard.numpad1Key.wasPressedThisFrame)
                Choose(autonomous: false);
            else if (keyboard.digit2Key.wasPressedThisFrame || keyboard.numpad2Key.wasPressedThisFrame)
                Choose(autonomous: true);
            else if (keyboard.escapeKey.wasPressedThisFrame)
                Hide();
        }

        private void Choose(bool autonomous)
        {
            _suppressSlotSwitchFrame = Time.frameCount;
            Hide();
            if (!NetworkClient.active || !IsHoldingAC130())
                return;

            NetworkClient.Send(new AC130StartMessage { Autonomous = autonomous });
        }

        private static bool IsHoldingAC130()
        {
            PlayerInventory inventory =
                NetworkClient.localPlayer != null
                    ? NetworkClient.localPlayer.GetComponent<PlayerInventory>()
                    : null;
            return inventory != null
                && inventory.GetEffectivelyEquippedItem(true) == ItemRegistry.AC130ItemType;
        }

        private void OnGUI()
        {
            if (!_visible)
                return;

            EnsureStyles();

            float scale = Screen.height / ReferenceHeight;
            float width = 760f * scale;
            float height = 168f * scale;
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.42f, width, height);

            GUI.Box(rect, GUIContent.none, _panelStyle);

            _titleStyle.fontSize = Mathf.RoundToInt(22f * scale);
            _lineStyle.fontSize = Mathf.RoundToInt(18f * scale);

            float pad = 18f * scale;
            var title = new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2f, 36f * scale);
            var pilot = new Rect(title.x, title.yMax + 8f * scale, title.width, 32f * scale);
            var deploy = new Rect(pilot.x, pilot.yMax + 4f * scale, pilot.width, 32f * scale);
            var cancel = new Rect(deploy.x, deploy.yMax + 4f * scale, deploy.width, 28f * scale);

            GUI.Label(title, "AC-130 Gunship", _titleStyle);
            GUI.Label(pilot, "Press (1) to pilot the AC130", _lineStyle);
            GUI.Label(deploy, "Press (2) to deploy it autonomously", _lineStyle);
            GUI.Label(cancel, "Press (Esc) to cancel", _lineStyle);
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
                return;

            _panelTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _panelTex.SetPixel(0, 0, new Color(0.05f, 0.06f, 0.07f, 0.92f));
            _panelTex.Apply();

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.background = _panelTex;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.95f, 0.9f, 0.75f) },
            };

            _lineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white },
            };
        }
    }
}
