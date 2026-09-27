using IssaPlugin.Items;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Choice shown when the local player uses an AC130.
    /// The item stays equipped until they pick a mode. Escape, or switching
    /// to another item, closes the prompt and leaves the choice unsent.
    /// F and G are used instead of the number row, which is the hotbar.
    /// </summary>
    public class AC130DeployPrompt : MonoBehaviour
    {
        private static AC130DeployPrompt _instance;

        private bool _visible;
        private int _suppressShowUntilFrame = -1;
        private string _notice;
        private float _noticeUntil;

        private GUIStyle _panelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _buttonStyle;
        private GUIStyle _cancelStyle;
        private GUIStyle _noticeStyle;
        private Texture2D _panelTex;
        private Texture2D _buttonTex;
        private Texture2D _buttonHoverTex;
        private Texture2D _cancelTex;

        private const float ReferenceHeight = 1080f;
        private const float NoticeSeconds = 4f;

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_visible)
                CursorManager.SetCursorForceUnlocked(false);

            if (_instance == this)
                _instance = null;

            DestroyTex(_panelTex);
            DestroyTex(_buttonTex);
            DestroyTex(_buttonHoverTex);
            DestroyTex(_cancelTex);
        }

        public static void Show()
        {
            if (_instance == null)
                return;

            // The swing button both uses the item and clicks these buttons.
            // Ignore the use that lands on the same click as a choice.
            if (Time.frameCount <= _instance._suppressShowUntilFrame)
                return;

            _instance._visible = true;
            CursorManager.SetCursorForceUnlocked(true);
        }

        public static void Hide()
        {
            if (_instance == null)
                return;

            _instance._visible = false;
            CursorManager.SetCursorForceUnlocked(false);
        }

        public static void ShowNotice(string message)
        {
            if (_instance == null || string.IsNullOrEmpty(message))
                return;

            _instance._notice = message;
            _instance._noticeUntil = Time.time + NoticeSeconds;
        }

        private void Update()
        {
            if (_notice != null && Time.time >= _noticeUntil)
                _notice = null;

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

            if (keyboard.fKey.wasPressedThisFrame)
                Choose(autonomous: false);
            else if (keyboard.gKey.wasPressedThisFrame)
                Choose(autonomous: true);
            else if (keyboard.escapeKey.wasPressedThisFrame)
                Hide();
        }

        private void Choose(bool autonomous)
        {
            _suppressShowUntilFrame = Time.frameCount + 1;
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
            if (_notice != null)
                DrawNotice();

            if (!_visible)
                return;

            EnsureStyles();

            float scale = Screen.height / ReferenceHeight;
            float width = 640f * scale;
            float height = 248f * scale;
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.42f, width, height);

            GUI.Box(rect, GUIContent.none, _panelStyle);

            _titleStyle.fontSize = Mathf.RoundToInt(22f * scale);
            _buttonStyle.fontSize = Mathf.RoundToInt(18f * scale);
            _cancelStyle.fontSize = Mathf.RoundToInt(16f * scale);

            float pad = 18f * scale;
            float buttonH = 42f * scale;
            float gap = 8f * scale;
            var title = new Rect(rect.x + pad, rect.y + pad, rect.width - pad * 2f, 36f * scale);
            var pilot = new Rect(title.x, title.yMax + gap, title.width, buttonH);
            var deploy = new Rect(pilot.x, pilot.yMax + gap, pilot.width, buttonH);
            var cancel = new Rect(deploy.x, deploy.yMax + gap, deploy.width, 36f * scale);

            GUI.Label(title, "AC-130 Gunship", _titleStyle);
            if (GUI.Button(pilot, "Pilot    [F]", _buttonStyle))
                Choose(autonomous: false);
            if (GUI.Button(deploy, "Deploy autonomously    [G]", _buttonStyle))
                Choose(autonomous: true);
            if (GUI.Button(cancel, "Cancel    [Esc]", _cancelStyle))
                Hide();
        }

        private void DrawNotice()
        {
            EnsureStyles();

            float scale = Screen.height / ReferenceHeight;
            _noticeStyle.fontSize = Mathf.RoundToInt(18f * scale);
            float width = 560f * scale;
            float height = 48f * scale;
            var rect = new Rect((Screen.width - width) * 0.5f, 72f * scale, width, height);
            GUI.Box(rect, GUIContent.none, _panelStyle);
            GUI.Label(rect, _notice, _noticeStyle);
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
                return;

            _panelTex = Solid(new Color(0.05f, 0.06f, 0.07f, 0.92f));
            _buttonTex = Solid(new Color(0.18f, 0.22f, 0.28f, 1f));
            _buttonHoverTex = Solid(new Color(0.28f, 0.36f, 0.46f, 1f));
            _cancelTex = Solid(new Color(0.14f, 0.14f, 0.16f, 1f));

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.background = _panelTex;

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.95f, 0.9f, 0.75f) },
            };

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
            };
            _buttonStyle.normal.background = _buttonTex;
            _buttonStyle.hover.background = _buttonHoverTex;
            _buttonStyle.active.background = _buttonHoverTex;
            _buttonStyle.normal.textColor = Color.white;
            _buttonStyle.hover.textColor = Color.white;
            _buttonStyle.active.textColor = Color.white;

            _cancelStyle = new GUIStyle(_buttonStyle);
            _cancelStyle.normal.background = _cancelTex;
            _cancelStyle.hover.background = _buttonTex;
            _cancelStyle.active.background = _buttonTex;

            _noticeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.85f, 0.45f) },
            };
        }

        private static Texture2D Solid(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private static void DestroyTex(Texture2D tex)
        {
            if (tex != null)
                Destroy(tex);
        }
    }
}
