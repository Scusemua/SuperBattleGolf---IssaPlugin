using IssaPlugin.Items;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Client-side HUD component for the Player Linker item.
    ///
    /// Idle (equipped, no tether active):
    ///   Highlights the nearest valid player target in the aim cone with a
    ///   targeting reticle. Players only — no golf cart targeting.
    ///
    /// Active (local player is tethered):
    ///   Shows a "PLAYER LINKED" banner and countdown bar.
    ///   Both the wielder and the target see this HUD.
    /// </summary>
    public class PlayerLinkerOverlay : MonoBehaviour
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

        // Nearest lock-on candidate (updated in Update, idle only)
        private bool _hasTarget;
        private bool _aimingIn;
        private Vector3 _targetScreenPos;

        /// The best lock-on target identity found this frame; null when none in cone.
        /// Read by PlayerLinkerItemDefinition.OnUse to avoid a duplicate scan.
        public NetworkIdentity BestTargetIdentity { get; private set; }

        // Set by ShowBusy() when the server rejects a lock-on due to a live session elsewhere.
        private float _busyUntil;

        // ── Singleton ─────────────────────────────────────────────────────────
        public static PlayerLinkerOverlay Instance { get; private set; }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null || localInfo.Inventory == null)
                return;

            _sw = Screen.width;
            _sh = Screen.height;
            _time = Time.time;

            // Local player is "in session" if tether is active AND they are one of the two.
            if (PlayerLinkerNetworkBridge.TetherActive)
            {
                uint localNetId = localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u;
                _sessionActive =
                    localNetId == PlayerLinkerNetworkBridge.WielderNetId
                    || localNetId == PlayerLinkerNetworkBridge.TargetNetId;
            }
            else
            {
                _sessionActive = false;
            }

            if (_sessionActive)
            {
                _hasTarget = false;
            }
            else
            {
                UpdateLockOnTarget();
            }
        }

        // ── Idle target detection (players only) ──────────────────────────────

        private void UpdateLockOnTarget()
        {
            _hasTarget = false;
            BestTargetIdentity = null;

            var localInventory = GameManager.LocalPlayerInventory;
            if (
                localInventory == null
                || localInventory.GetEffectivelyEquippedItem(true)
                    != ItemRegistry.PlayerLinkerItemType
            )
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            float cosHalf = Mathf.Cos(
                Configuration.PlayerLinkerLockOnConeAngleDeg.Value * 0.5f * Mathf.Deg2Rad
            );
            float rangeSq =
                Configuration.PlayerLinkerLockOnRange.Value
                * Configuration.PlayerLinkerLockOnRange.Value;

            Vector3 camPos = cam.transform.position;
            Vector3 camFwd = cam.transform.forward;

            float bestDot = -1f;
            Transform bestTransform = null;
            NetworkIdentity bestIdentity = null;

            // ── Scan remote players only (no golf cart targeting) ─────────────
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

            if (bestTransform == null)
                return;

            BestTargetIdentity = bestIdentity;

            Vector3 screenPos = cam.WorldToScreenPoint(bestTransform.position);
            if (screenPos.z <= 0f)
                return;

            _targetScreenPos = new Vector3(screenPos.x, _sh - screenPos.y, 0f);
            _hasTarget = true;
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint)
                return;

            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null || localInfo.Inventory == null)
                return;

            // Show the HUD while tethered OR while the item is equipped.
            bool equipped =
                localInfo.Inventory.GetEffectivelyEquippedItem(true)
                == ItemRegistry.PlayerLinkerItemType;
            if (!equipped && !_sessionActive)
                return;

            if (_sw <= 0f || _sh <= 0f)
                return;

            EnsureStyles();

            if (_sessionActive)
                DrawActiveHUD();
            else if (_time < _busyUntil)
                DrawBusyMessage();
            else if (_hasTarget)
                DrawLockOnReticle();

            if (Mouse.current == null || !Mouse.current.rightButton.isPressed)
            {
                if (_aimingIn)
                    FixLookRotationAfterScope();
                _aimingIn = false;
                return;
            }

            _aimingIn = true;
            CorrectAimRotation();
        }

        // ── Busy notification ─────────────────────────────────────────────────

        public void ShowBusy() => _busyUntil = Time.time + 2.5f;

        private void DrawBusyMessage()
        {
            const float h = 42f;
            float y = _sh - 12f - h;
            _instructionStyle.normal.textColor = new Color(1f, 0.5f, 0.1f, 0.9f);
            GUI.Label(new Rect(0, y, _sw, h), "PLAYER LINKER ALREADY IN USE", _instructionStyle);
            _instructionStyle.normal.textColor = Color.white;
        }

        // ── Active session HUD ────────────────────────────────────────────────

        private void DrawActiveHUD()
        {
            float duration = PlayerLinkerNetworkBridge.TetherDuration;
            float elapsed = _time - PlayerLinkerNetworkBridge.TetherStartTime;
            float remaining = Mathf.Max(0f, duration - elapsed);
            bool lowTime = remaining <= 3f;

            Color bracketColor = lowTime
                ? new Color(1f, 0.25f, 0.25f, 0.9f)
                : new Color(1f, 0.55f, 0.1f, 0.9f); // orange
            float bSize = 50f,
                bThick = 3f;
            DrawCornerBracket(0, 0, bSize, bThick, true, true, bracketColor);
            DrawCornerBracket(_sw - bSize, 0, bSize, bThick, false, true, bracketColor);
            DrawCornerBracket(0, _sh - bSize, bSize, bThick, true, false, bracketColor);
            DrawCornerBracket(_sw - bSize, _sh - bSize, bSize, bThick, false, false, bracketColor);

            const float bottomMargin = 12f;
            const float barH = 10f;
            const float titleH = 42f;
            const float rowGap = 10f;

            float barY = _sh - bottomMargin - barH;
            float titleY = barY - rowGap - titleH;

            _titleStyle.normal.textColor = lowTime
                ? new Color(1f, 0.25f, 0.25f, 0.7f + 0.3f * Mathf.Sin(_time * 6f))
                : new Color(1f, 0.55f, 0.1f, 0.9f);
            GUI.Label(new Rect(0, titleY, _sw, titleH), "PLAYER LINKED", _titleStyle);

            float barW = 280f;
            float barX = (_sw - barW) * 0.5f;
            float fill = duration > 0f ? Mathf.Clamp01(remaining / duration) : 0f;

            GUI.color = new Color(1f, 1f, 1f, 0.2f);
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);

            Color fillColor = lowTime
                ? new Color(1f, 0.3f, 0.3f, 0.85f)
                : new Color(1f, 0.55f, 0.1f, 0.85f);
            GUI.color = fillColor;
            GUI.DrawTexture(new Rect(barX, barY, barW * fill, barH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            _timerStyle.normal.textColor = lowTime
                ? new Color(1f, 0.3f, 0.3f, 0.9f)
                : new Color(1f, 0.55f, 0.1f, 0.9f);
            GUI.Label(
                new Rect(barX + barW + 8f, barY - 14f, 80f, 38f),
                $"{remaining:F1}s",
                _timerStyle
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
            Color col = new Color(1f, 0.55f, 0.1f, 0.85f); // orange

            GUI.color = col;

            float gap = inner;
            float len = outer - inner;

            GUI.DrawTexture(
                new Rect(cx - outer, cy - thick * 0.5f, len, thick),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(cx + gap, cy - thick * 0.5f, len, thick),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(cx - thick * 0.5f, cy - outer, thick, len),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(
                new Rect(cx - thick * 0.5f, cy + gap, thick, len),
                Texture2D.whiteTexture
            );
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);

            GUI.color = Color.white;

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
                _titleStyle.normal.textColor = new Color(1f, 0.55f, 0.1f, 0.9f);
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
                _labelStyle.normal.textColor = new Color(1f, 0.55f, 0.1f, 0.85f);
            }

            if (_timerStyle == null)
            {
                _timerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                };
                _timerStyle.normal.textColor = new Color(1f, 0.55f, 0.1f, 0.9f);
            }
        }
    }
}
