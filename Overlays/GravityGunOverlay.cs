using IssaPlugin.Items;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Client-side HUD component added to the local player's inventory object
    /// while the Gravity Gun item is equipped (via GravityGunItemDefinition.OnEquip).
    ///
    /// Idle (equipped, no session active):
    ///   Highlights the nearest valid lock-on target in the aim cone with a
    ///   targeting reticle in the centre of the screen.
    ///
    /// Active (LocalSessionActive == true):
    ///   Shows an "GRAVITY GUN ACTIVE" banner, a countdown bar, and a
    ///   release-key hint.
    ///
    /// The component removes itself when the GravityGun item is no longer equipped.
    /// </summary>
    public class GravityGunOverlay : MonoBehaviour
    {
        // ── Styles ────────────────────────────────────────────────────────────
        private GUIStyle _titleStyle;
        private GUIStyle _instructionStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _timerStyle;

        // ── Cached state updated in Update, consumed in OnGUI ─────────────────
        private float _sw;
        private float _sh;
        private float _time;
        private bool _sessionActive;
        private float _sessionElapsed; // seconds since session started
        private float _sessionDuration; // from GravityGunConnectedMessage

        // Nearest lock-on candidate (updated in Update, idle only)
        private bool _hasTarget;
        private bool _aimingIn;
        private Vector3 _targetScreenPos;

        /// The best lock-on target identity found this frame; null when none in cone.
        /// Read by GravityGunNetworkBridge.ClientUse to avoid a duplicate scan.
        public NetworkIdentity BestTargetIdentity { get; private set; }

        // Golf cart list — refreshed at most once per second to avoid per-frame FindObjectsByType.
        private GolfCartInfo[] _cachedCarts = [];
        private float _cartCacheTime = float.MinValue;
        private const float CartCacheInterval = 1f;

        // Session start time (set in Update when session becomes active)
        private float _sessionStartTime;
        private bool _wasSessionActive;

        // Set by ShowBusy() when the server rejects a lock-on due to a live session elsewhere.
        private float _busyUntil;

        // ── Singleton ─────────────────────────────────────────────────────────
        public static GravityGunOverlay Instance { get; private set; }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void OnEnable()  { Instance = this; }
        private void OnDisable() { if (Instance == this) Instance = null; }

        private void Update()
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null || localInfo.Inventory == null)
            {
                return;
            }

            _sw = Screen.width;
            _sh = Screen.height;
            _time = Time.time;

            // This component lives on the Plugin's persistent GameObject, so look up
            // the local player's bridge rather than GetComponent on self.
            var bridge = localInfo?.GetComponent<GravityGunNetworkBridge>();
            _sessionActive = bridge != null && bridge.LocalSessionActive;

            // Track session start so we can display elapsed time.
            if (_sessionActive && !_wasSessionActive)
            {
                _sessionStartTime = _time;
                _sessionDuration = ModConfig.GravityGun.TetherDuration.Value;
            }
            _wasSessionActive = _sessionActive;

            if (_sessionActive)
            {
                _sessionElapsed = _time - _sessionStartTime;
                _hasTarget = false;
            }
            else
            {
                _sessionElapsed = 0f;
                UpdateLockOnTarget();
            }
        }

        // ── Idle target detection ─────────────────────────────────────────────

        private void UpdateLockOnTarget()
        {
            _hasTarget = false;
            BestTargetIdentity = null;

            // Only show the lock-on reticle while the item is actively equipped.
            var localInventory = GameManager.LocalPlayerInventory;
            if (
                localInventory == null
                || localInventory.GetEffectivelyEquippedItem(true)
                    != ItemRegistry.GravityGunItemType
            )
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            float cosHalf = Mathf.Cos(
                ModConfig.GravityGun.LockOnConeAngleDeg.Value * 0.5f * Mathf.Deg2Rad
            );
            float rangeSq =
                ModConfig.GravityGun.LockOnRange.Value
                * ModConfig.GravityGun.LockOnRange.Value;

            Vector3 camPos = cam.transform.position;
            Vector3 camFwd = cam.transform.forward;

            float bestDot = -1f;
            Transform bestTransform = null;
            NetworkIdentity bestIdentity = null;

            // ── Scan remote players ───────────────────────────────────────────
            var remotePlayers = GameManager.RemotePlayers;
            if (remotePlayers != null)
            {
                foreach (var player in remotePlayers)
                {
                    if (player == null)
                        continue;
                    var nid = player.GetComponent<NetworkIdentity>();
                    if (nid == null)
                        continue;
                    Vector3 toTarget = player.transform.position - camPos;
                    if (toTarget.sqrMagnitude > rangeSq)
                        continue;
                    float dot = Vector3.Dot(camFwd, toTarget.normalized);
                    if (dot < cosHalf || dot <= bestDot)
                        continue;
                    bestDot = dot;
                    bestTransform = player.transform;
                    bestIdentity = nid;
                }
            }

            // ── Scan golf carts ───────────────────────────────────────────────
            if (_time - _cartCacheTime >= CartCacheInterval)
            {
                _cachedCarts = FindObjectsByType<GolfCartInfo>(FindObjectsSortMode.None);
                _cartCacheTime = _time;
            }

            foreach (var cart in _cachedCarts)
            {
                if (cart == null || !cart.gameObject.activeInHierarchy)
                    continue;
                var nid = cart.GetComponent<NetworkIdentity>();
                if (nid == null)
                    continue;
                Vector3 toTarget = cart.transform.position - camPos;
                if (toTarget.sqrMagnitude > rangeSq)
                    continue;
                float dot = Vector3.Dot(camFwd, toTarget.normalized);
                if (dot < cosHalf || dot <= bestDot)
                    continue;
                bestDot = dot;
                bestTransform = cart.transform;
                bestIdentity = nid;
            }

            if (bestTransform == null)
                return;

            BestTargetIdentity = bestIdentity;

            // Project target onto screen for reticle placement.
            Vector3 screenPos = cam.WorldToScreenPoint(bestTransform.position);
            if (screenPos.z <= 0f)
                return;

            // InputSystem screen coords are bottom-up; GUI coords are top-down.
            _targetScreenPos = new Vector3(screenPos.x, _sh - screenPos.y, 0f);
            _hasTarget = true;
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null || localInfo.Inventory == null)
            {
                return;
            }

            // Only show while the Gravity Gun is equipped.
            if (
                localInfo.Inventory.GetEffectivelyEquippedItem(true)
                != ItemRegistry.GravityGunItemType
            )
            {
                return;
            }

            if (_sw <= 0f || _sh <= 0f)
            {
                return;
            }

            EnsureStyles();

            if (_sessionActive)
                DrawActiveHUD();
            else if (_time < _busyUntil)
                DrawBusyMessage();
            else if (_hasTarget)
                DrawLockOnReticle();

            // Only show while aiming in (right-click held).
            if (Mouse.current == null || !Mouse.current.rightButton.isPressed)
            {
                if (_aimingIn)
                {
                    FixLookRotationAfterScope();
                }
                _aimingIn = false;
                return;
            }

            _aimingIn = true;

            CorrectAimRotation();
        }

        // ── Busy notification ─────────────────────────────────────────────────

        /// Called by GravityGunNetworkBridge.HandleGravityGunBusy to show a
        /// brief on-screen message when another player's session blocks the lock-on.
        public void ShowBusy() => _busyUntil = Time.time + 2.5f;

        private void DrawBusyMessage()
        {
            const float h = 42f;
            float y = _sh - 12f - h;
            _instructionStyle.normal.textColor = new Color(1f, 0.6f, 0.1f, 0.9f);
            GUI.Label(new Rect(0, y, _sw, h), "GRAVITY GUN ALREADY IN USE", _instructionStyle);
            _instructionStyle.normal.textColor = Color.white; // restore default
        }

        // ── Active session HUD ────────────────────────────────────────────────

        private void DrawActiveHUD()
        {
            float remaining = Mathf.Max(0f, _sessionDuration - _sessionElapsed);
            bool lowTime = remaining <= 3f;

            // Corner brackets — electric blue.
            Color bracketColor = lowTime
                ? new Color(1f, 0.25f, 0.25f, 0.9f)
                : new Color(0.2f, 0.85f, 1f, 0.9f);
            float bSize = 50f,
                bThick = 3f;
            DrawCornerBracket(0, 0, bSize, bThick, true, true, bracketColor);
            DrawCornerBracket(_sw - bSize, 0, bSize, bThick, false, true, bracketColor);
            DrawCornerBracket(0, _sh - bSize, bSize, bThick, true, false, bracketColor);
            DrawCornerBracket(_sw - bSize, _sh - bSize, bSize, bThick, false, false, bracketColor);

            // Layout: stacked bottom-centre, reading top-to-bottom:
            //   "GRAVITY GUN ACTIVE" title
            //   timer bar + countdown
            //   release-key hint  ← anchored to bottom edge
            const float bottomMargin = 12f;
            const float hintH = 42f;
            const float rowGap = 10f;
            const float barH = 10f;
            const float titleH = 42f;

            float hintY = _sh - bottomMargin - hintH;
            float barY = hintY - rowGap - barH;
            float titleY = barY - rowGap - titleH;

            // Title banner.
            _titleStyle.normal.textColor = lowTime
                ? new Color(1f, 0.25f, 0.25f, 0.7f + 0.3f * Mathf.Sin(_time * 6f))
                : new Color(0.2f, 0.85f, 1f, 0.9f);

            GUI.Label(new Rect(0, titleY, _sw, titleH), "GRAVITY GUN ACTIVE", _titleStyle);

            // Timer bar (centred, below title).
            float barW = 280f;
            float barX = (_sw - barW) * 0.5f;
            float fill = _sessionDuration > 0f ? Mathf.Clamp01(remaining / _sessionDuration) : 0f;

            // Track
            GUI.color = new Color(1f, 1f, 1f, 0.2f);
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);

            // Fill
            Color fillColor = lowTime
                ? new Color(1f, 0.3f, 0.3f, 0.85f)
                : new Color(0.2f, 0.85f, 1f, 0.85f);
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(barX, barY, barW * fill, barH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Timer label (right of bar, vertically centred on bar).
            _timerStyle.normal.textColor = lowTime
                ? new Color(1f, 0.3f, 0.3f, 0.9f)
                : new Color(0.2f, 0.85f, 1f, 0.9f);
            GUI.Label(
                new Rect(barX + barW + 8f, barY - 14f, 80f, 38f),
                $"{remaining:F1}s",
                _timerStyle
            );

            // Release hint (anchored to bottom).
            GUI.Label(
                new Rect(0, hintY, _sw, hintH),
                "Left-click to Release the Tether Line",
                _instructionStyle
            );
        }

        // ── Idle lock-on reticle ──────────────────────────────────────────────

        private void DrawLockOnReticle()
        {
            if (!_aimingIn)
                return;

            float cx = _targetScreenPos.x;
            float cy = _targetScreenPos.y;

            float inner = 24f;
            float outer = 36f;
            float thick = 4f;
            Color col = new Color(0.2f, 0.85f, 1f, 0.85f);

            GUI.color = col;

            // Four gap-separated tick marks around the target.
            float gap = inner;
            float len = outer - inner;

            // Left
            GUI.DrawTexture(
                new Rect(cx - outer, cy - thick * 0.5f, len, thick),
                Texture2D.whiteTexture
            );
            // Right
            GUI.DrawTexture(
                new Rect(cx + gap, cy - thick * 0.5f, len, thick),
                Texture2D.whiteTexture
            );
            // Up
            GUI.DrawTexture(
                new Rect(cx - thick * 0.5f, cy - outer, thick, len),
                Texture2D.whiteTexture
            );
            // Down
            GUI.DrawTexture(
                new Rect(cx - thick * 0.5f, cy + gap, thick, len),
                Texture2D.whiteTexture
            );

            // Centre dot.
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);

            GUI.color = Color.white;

            // "LOCK ON" label just below the reticle.
            _labelStyle.normal.textColor = col;
            GUI.Label(new Rect(cx - 70f, cy + outer + 4f, 140f, 38f), "LOCK ON", _labelStyle);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void DrawCornerBracket(
            float x,
            float y,
            float size,
            float thick,
            bool leftSide,
            bool topSide,
            Color c
        )
        {
            GUI.color = c;
            float hx = leftSide ? x : x + size - thick;
            GUI.DrawTexture(new Rect(hx, y, thick, size), Texture2D.whiteTexture);
            float hy = topSide ? y : y + size - thick;
            GUI.DrawTexture(new Rect(x, hy, size, thick), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void CorrectAimRotation()
        {
            var li = GameManager.LocalPlayerInfo;
            if (li?.Movement == null)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f)
                return;
            li.Movement.transform.rotation =
                Quaternion.LookRotation(fwd.normalized) * Quaternion.Euler(0f, 65f, 0f);
        }

        private void FixLookRotationAfterScope()
        {
            if (Camera.main != null)
                GameManager
                    .LocalPlayerInfo
                    ?.Movement
                    ?.transform.rotation = Quaternion.LookRotation(
                        Camera.main.transform.forward.normalized
                    );
        }

        private void EnsureStyles()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 32,
                    fontStyle = FontStyle.Bold,
                };
                _titleStyle.normal.textColor = new Color(0.2f, 0.85f, 1f, 0.9f);
            }

            if (_instructionStyle == null)
            {
                _instructionStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 32,
                    fontStyle = FontStyle.Bold,
                };
                _instructionStyle.normal.textColor = Color.white;
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                };
                _labelStyle.normal.textColor = new Color(0.2f, 0.85f, 1f, 0.85f);
            }

            if (_timerStyle == null)
            {
                _timerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                };
                _timerStyle.normal.textColor = new Color(0.2f, 0.85f, 1f, 0.9f);
            }
        }
    }
}
