using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Client-side HUD for the UFO Abduction item.
    ///
    /// Idle: highlights the nearest player in the aim cone with a lock-on reticle.
    /// Active (local player is being abducted): shows an "ABDUCTION IN PROGRESS" banner
    ///   and countdown bar.
    /// </summary>
    public class UfoAbductionOverlay : MonoBehaviour
    {
        // ── Styles ────────────────────────────────────────────────────────────
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _timerStyle;
        private GUIStyle _instructionStyle;
        private GUIStyle _pipLabelStyle;

        // ── Cached state ──────────────────────────────────────────────────────
        private float _sw;
        private float _sh;
        private float _time;

        // True when the local player is the abduction *victim*.
        private bool _victimSessionActive;
        private UfoAbductionSessionState _activeSession;

        // True when the local player is the abduction *wielder* (fired the item).
        private bool _wielderSessionActive;

        private bool _hasTarget;
        private Vector3 _targetScreenPos;
        private float _busyUntil;

        /// The best lock-on target identity found this frame; null when none.
        public NetworkIdentity BestTargetIdentity { get; private set; }

        // ── Singleton ─────────────────────────────────────────────────────────
        public static UfoAbductionOverlay Instance { get; private set; }

        private void OnEnable() => Instance = this;

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Update()
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null || localInfo.Inventory == null)
                return;

            _sw = Screen.width;
            _sh = Screen.height;
            _time = Time.time;

            uint localNetId = localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u;
            _victimSessionActive =
                localNetId != 0u
                && UfoAbductionClientLogic.TryGetSession(localNetId, out _activeSession);
            _wielderSessionActive =
                localNetId != 0u
                && !_victimSessionActive
                && UfoAbductionClientLogic.TryGetSessionForWielder(localNetId, out _);

            if (_victimSessionActive || _wielderSessionActive)
                _hasTarget = false;
            else
                UpdateLockOnTarget();
        }

        // ── Lock-on target detection ──────────────────────────────────────────

        private void UpdateLockOnTarget()
        {
            _hasTarget = false;
            BestTargetIdentity = null;

            var localInventory = GameManager.LocalPlayerInventory;
            if (
                localInventory == null
                || localInventory.GetEffectivelyEquippedItem(true)
                    != ItemRegistry.UfoAbductionItemType
            )
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            float cosHalf = Mathf.Cos(
                ModConfig.UfoAbduction.LockOnConeAngleDeg.Value * 0.5f * Mathf.Deg2Rad
            );
            float rangeSq =
                ModConfig.UfoAbduction.LockOnRange.Value * ModConfig.UfoAbduction.LockOnRange.Value;

            Vector3 camPos = cam.transform.position;
            Vector3 camFwd = cam.transform.forward;

            float bestDot = -1f;
            Transform bestTransform = null;
            NetworkIdentity bestIdentity = null;

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

            // In solo play (no remote players), allow the local player to self-target.
            if (bestTransform == null && (remotePlayers == null || remotePlayers.Count == 0))
            {
                var localInfo = GameManager.LocalPlayerInfo;
                if (localInfo != null)
                {
                    var nid = localInfo.GetComponent<NetworkIdentity>();
                    if (nid != null)
                    {
                        Vector3 toTarget = localInfo.transform.position - camPos;
                        if (toTarget.sqrMagnitude <= rangeSq)
                        {
                            float dot = Vector3.Dot(camFwd, toTarget.normalized);
                            if (dot >= cosHalf)
                            {
                                bestTransform = localInfo.transform;
                                bestIdentity = nid;
                            }
                        }
                    }
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

            if (_sw <= 0f || _sh <= 0f)
                return;

            EnsureStyles();

            // PiP is shown to every client whenever any abduction session is active
            var pipTex = UfoAbductionClientLogic.GetActivePipTexture();
            if (pipTex != null && pipTex.IsCreated())
                DrawPipView(pipTex);

            bool equipped =
                localInfo.Inventory.GetEffectivelyEquippedItem(true)
                == ItemRegistry.UfoAbductionItemType;
            if (!equipped && !_victimSessionActive && !_wielderSessionActive)
                return;

            if (_victimSessionActive)
                DrawActiveHUD();
            else if (_wielderSessionActive)
                DrawWielderHUD();
            else if (_time < _busyUntil)
                DrawBusyMessage();
            else if (_hasTarget)
                DrawLockOnReticle();
        }

        // ── Picture-in-picture ────────────────────────────────────────────────

        private void DrawPipView(RenderTexture tex)
        {
            const float pipW = 320f;
            const float pipH = 180f;
            const float margin = 16f;
            const float labelH = 22f;

            float x = _sw - pipW - margin;
            float y = margin;

            // Green border
            GUI.color = new Color(0.3f, 1f, 0.5f, 0.85f);
            GUI.DrawTexture(new Rect(x - 2f, y - 2f, pipW + 4f, pipH + 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.DrawTexture(new Rect(x, y, pipW, pipH), tex, ScaleMode.StretchToFill, false);

            _pipLabelStyle.normal.textColor = new Color(0.3f, 1f, 0.5f, 0.9f);
            GUI.Label(new Rect(x, y + pipH + 3f, pipW, labelH), "ABDUCTION IN PROGRESS", _pipLabelStyle);
        }

        // ── Wielder HUD (fired the item) ──────────────────────────────────────

        private void DrawWielderHUD()
        {
            const float h = 42f;
            float y = _sh - 12f - h;
            _titleStyle.normal.textColor = new Color(0.3f, 1f, 0.5f, 0.9f);
            GUI.Label(new Rect(0, y, _sw, h), "UFO ABDUCTION ACTIVE", _titleStyle);
        }

        // ── Busy message ──────────────────────────────────────────────────────

        public void ShowBusy() => _busyUntil = Time.time + 2.5f;

        private void DrawBusyMessage()
        {
            const float h = 42f;
            float y = _sh - 12f - h;
            _instructionStyle.normal.textColor = new Color(0.3f, 1f, 0.5f, 0.9f);
            GUI.Label(
                new Rect(0, y, _sw, h),
                "UFO ABDUCTION ALREADY IN PROGRESS",
                _instructionStyle
            );
            _instructionStyle.normal.textColor = Color.white;
        }

        // ── Active HUD ────────────────────────────────────────────────────────

        private void DrawActiveHUD()
        {
            if (_activeSession == null)
                return;

            float totalDuration = _activeSession.TotalDuration;
            float elapsed = _time - _activeSession.StartTime;
            float remaining = Mathf.Max(0f, totalDuration - elapsed);
            bool lowTime = remaining <= 3f;

            // Phase label
            string phaseLabel;
            Color phaseColor;
            if (elapsed < _activeSession.ApproachDuration)
            {
                phaseLabel = "UFO INCOMING";
                phaseColor = new Color(0.3f, 1f, 0.5f, 0.9f);
            }
            else if (elapsed < _activeSession.ApproachDuration + _activeSession.AbductionDuration)
            {
                phaseLabel = "BEING ABDUCTED";
                phaseColor = lowTime
                    ? new Color(1f, 0.25f, 0.25f, 0.9f)
                    : new Color(0.3f, 1f, 0.5f, 0.9f);
            }
            else
            {
                phaseLabel = "ENTERING BLACK HOLE";
                phaseColor = new Color(1f, 0.25f, 0.25f, 0.7f + 0.3f * Mathf.Sin(_time * 6f));
            }

            const float bottomMargin = 12f;
            const float barH = 10f;
            const float titleH = 42f;
            const float rowGap = 10f;

            float barY = _sh - bottomMargin - barH;
            float titleY = barY - rowGap - titleH;

            _titleStyle.normal.textColor = phaseColor;
            GUI.Label(new Rect(0, titleY, _sw, titleH), phaseLabel, _titleStyle);

            float barW = 280f;
            float barX = (_sw - barW) * 0.5f;
            float fill = totalDuration > 0f ? Mathf.Clamp01(remaining / totalDuration) : 0f;

            GUI.color = new Color(1f, 1f, 1f, 0.2f);
            GUI.DrawTexture(new Rect(barX, barY, barW, barH), Texture2D.whiteTexture);

            GUI.color = phaseColor;
            GUI.DrawTexture(new Rect(barX, barY, barW * fill, barH), Texture2D.whiteTexture);
            GUI.color = Color.white;

            _timerStyle.normal.textColor = phaseColor;
            GUI.Label(
                new Rect(barX + barW + 8f, barY - 14f, 80f, 38f),
                $"{remaining:F1}s",
                _timerStyle
            );
        }

        // ── Lock-on reticle ───────────────────────────────────────────────────

        private void DrawLockOnReticle()
        {
            float cx = _targetScreenPos.x;
            float cy = _targetScreenPos.y;

            float inner = 28f;
            float outer = 42f;
            float thick = 4f;
            Color col = new Color(0.3f, 1f, 0.5f, 0.85f); // green

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
            GUI.DrawTexture(new Rect(cx - 3f, cy - 3f, 6f, 6f), Texture2D.whiteTexture);

            GUI.color = Color.white;

            _labelStyle.normal.textColor = col;
            string lockOnLabel = "LOCK ON";
            if (BestTargetIdentity != null)
            {
                var pi = BestTargetIdentity.GetComponentInParent<PlayerInfo>();
                string targetName = pi?.PlayerId?.PlayerName;
                if (!string.IsNullOrEmpty(targetName))
                    lockOnLabel = "LOCK ON\n" + targetName;
            }
            GUI.Label(new Rect(cx - 200f, cy + outer + 4f, 400f, 76f), lockOnLabel, _labelStyle);
        }

        // ── Styles ────────────────────────────────────────────────────────────

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
                _titleStyle.normal.textColor = new Color(0.3f, 1f, 0.5f, 0.9f);
            }

            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                    wordWrap = false,
                };
                _labelStyle.normal.textColor = new Color(0.3f, 1f, 0.5f, 0.85f);
            }

            if (_timerStyle == null)
            {
                _timerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                };
                _timerStyle.normal.textColor = new Color(0.3f, 1f, 0.5f, 0.9f);
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

            if (_pipLabelStyle == null)
            {
                _pipLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontSize = 13,
                    fontStyle = FontStyle.Bold,
                };
                _pipLabelStyle.normal.textColor = new Color(0.3f, 1f, 0.5f, 0.9f);
            }
        }
    }
}
