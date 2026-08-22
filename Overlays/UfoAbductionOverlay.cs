using System;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Client-side HUD for the UFO Abduction item.
    ///
    /// States (in dispatch priority order):
    ///   Targeting UI  — wielder is selecting a drop zone (full-screen overlay)
    ///   Being targeted — local player is the victim during drop-zone selection
    ///   Aborted       — abduction was cancelled; shows brief "CANCELLED" message
    ///   Victim session — abduction is in progress; shows phase banner + countdown bar
    ///   Wielder session — shows "ABDUCTION ACTIVE" footer
    ///   Busy          — another session is already active
    ///   Lock-on idle  — highlights the nearest player in the aim cone with a reticle
    /// </summary>
    public class UfoAbductionOverlay : MonoBehaviour
    {
        // ── HUD styles ────────────────────────────────────────────────────────
        private GUIStyle _titleStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _timerStyle;
        private GUIStyle _instructionStyle;
        private GUIStyle _pipLabelStyle;

        // Targeting-overlay styles (lazily initialised inside OnGUI like the rest)
        private GUIStyle _targetingTitleStyle;
        private GUIStyle _targetingInstructionStyle;
        private GUIStyle _targetingCornerStyle;

        // ── Targeting-overlay textures ────────────────────────────────────────
        private static Texture2D _targetingBgTex;
        private static Texture2D _targetingScanlineTex;
        private static Texture2D _targetingNoiseTex;
        private float _targetingNoiseTimer;

        /// UV offset into the static targeting-noise texture, reshuffled on the noise
        /// cadence to fake regeneration without touching pixel data.
        private Vector2 _targetingNoiseOffset;

        /// Rows per scanline cycle: one tinted row followed by two transparent ones.
        /// Also the height of the tiled scanline texture.
        private const int ScanlinePeriod = 3;
        private const float TargetingNoiseUpdateRate = 0.04f;

        // ── Session state ─────────────────────────────────────────────────────
        private float _sw;
        private float _sh;
        private float _time;

        private bool _victimSessionActive;
        private UfoAbductionSessionState _activeSession;
        private bool _wielderSessionActive;

        private bool _hasTarget;
        private Vector3 _targetScreenPos;
        private float _busyUntil;

        // Pre-session state
        private bool _beingTargeted;
        private float _abortedUntil;

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

        // ── Public state mutators (called from UfoAbductionNetworkBridge) ─────

        public void ShowBeingTargeted() => _beingTargeted = true;

        public void ClearBeingTargeted() => _beingTargeted = false;

        public void ShowAborted() => _abortedUntil = Time.time + 2.5f;

        public void ShowBusy() => _busyUntil = Time.time + 2.5f;

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

            // Session start supersedes the "being targeted" banner.
            if (_victimSessionActive)
                _beingTargeted = false;

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

            // Cheap state flags first. Everything below this point only matters when
            // something is actually on screen, and this overlay is idle for the vast
            // majority of a match — so the idle path must not touch styles, query the
            // inventory, or look up the PiP texture.
            var pipTex = UfoAbductionClientLogic.GetActivePipTexture();
            bool hasPip = pipTex != null && pipTex.IsCreated();

            bool sessionActive =
                _victimSessionActive
                || _wielderSessionActive
                || _beingTargeted
                || _time < _abortedUntil
                || _time < _busyUntil
                || _hasTarget;

            // GetEffectivelyEquippedItem walks the inventory, so only ask when no
            // cheaper flag already tells us we have something to draw.
            bool equipped =
                sessionActive
                || localInfo.Inventory.GetEffectivelyEquippedItem(true)
                    == ItemRegistry.UfoAbductionItemType;

            if (!hasPip && !equipped)
                return;

            // Styles are only needed once we know something will be drawn.
            EnsureStyles();

            // PiP is shown to every client whenever any abduction session is active.
            if (hasPip)
                DrawPipView(pipTex);

            if (!equipped && !sessionActive)
                return;

            if (UfoAbductionTargeting.IsSelectingDropoff)
                DrawDropoffTargetingOverlay();
            else if (_beingTargeted && !_victimSessionActive)
                DrawBeingTargetedHUD();
            else if (_time < _abortedUntil)
                DrawAbortedMessage();
            else if (_victimSessionActive)
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

            GUI.color = new Color(0.3f, 1f, 0.5f, 0.85f);
            GUI.DrawTexture(new Rect(x - 2f, y - 2f, pipW + 4f, pipH + 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.DrawTexture(new Rect(x, y, pipW, pipH), tex, ScaleMode.StretchToFill, false);

            _pipLabelStyle.normal.textColor = new Color(0.3f, 1f, 0.5f, 0.9f);
            GUI.Label(
                new Rect(x, y + pipH + 3f, pipW, labelH),
                "ABDUCTION IN PROGRESS",
                _pipLabelStyle
            );
        }

        // ── Drop-zone targeting overlay (full-screen, wielder only) ──────────

        private void DrawDropoffTargetingOverlay()
        {
            EnsureTargetingTextures();

            // Scanlines — alien green tint. Tiled from a 1x3 strip: the UV rect repeats
            // once per ScanlinePeriod screen pixels, placing one tinted row every third
            // row exactly as the old screen-sized texture did.
            GUI.color = new Color(0f, 0.4f, 0f, 0.12f);
            GUI.DrawTextureWithTexCoords(
                new Rect(0, 0, _sw, _sh),
                _targetingScanlineTex,
                new Rect(0f, 0f, 1f, _sh / ScanlinePeriod)
            );

            // Noise — offset the UVs into a static texture rather than rebuilding its
            // pixels. Regenerating a 256x256 texture several times a second means 65k
            // Random calls plus a GPU upload on the main thread; scrolling looks the
            // same at 3% alpha and costs one Rect. Same approach as BomberOverlay.
            _targetingNoiseTimer += Time.deltaTime;
            if (_targetingNoiseTimer >= TargetingNoiseUpdateRate)
            {
                _targetingNoiseOffset = new Vector2(
                    UnityEngine.Random.value,
                    UnityEngine.Random.value
                );
                _targetingNoiseTimer = 0f;
            }
            GUI.color = new Color(1f, 1f, 1f, 0.03f);
            GUI.DrawTextureWithTexCoords(
                new Rect(0, 0, _sw, _sh),
                _targetingNoiseTex,
                new Rect(_targetingNoiseOffset.x, _targetingNoiseOffset.y, 1f, 1f)
            );
            GUI.color = Color.white;

            // Corner brackets
            float bSize = 60f,
                bThick = 3f;
            DrawTargetingCornerBracket(0, 0, bSize, bThick, true, true);
            DrawTargetingCornerBracket(_sw - bSize, 0, bSize, bThick, false, true);
            DrawTargetingCornerBracket(0, _sh - bSize, bSize, bThick, true, false);
            DrawTargetingCornerBracket(_sw - bSize, _sh - bSize, bSize, bThick, false, false);

            // Crosshair
            float cLen = 20f,
                cGap = 6f;
            float cx = _sw / 2f,
                cy = _sh / 2f;
            GUI.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            GUI.DrawTexture(new Rect(cx - cLen - cGap, cy - 1f, cLen, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + cGap, cy - 1f, cLen, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy - cLen - cGap, 2f, cLen), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy + cGap, 2f, cLen), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Telemetry — top-left
            GUI.Label(
                new Rect(16, 12, 300, 24),
                DateTime.UtcNow.ToString("HH:mm:ss") + "Z",
                _targetingCornerStyle
            );
            GUI.Label(new Rect(16, 34, 300, 24), "SYS: ARMED", _targetingCornerStyle);
            GUI.Label(
                new Rect(16, 56, 300, 24),
                "MODE: ABDUCTION TARGETING",
                _targetingCornerStyle
            );

            // Bottom instruction bar
            GUI.DrawTexture(new Rect(0, _sh - 80, _sw, 80), _targetingBgTex);
            GUI.Label(
                new Rect(0, _sh - 75, _sw, 35),
                "UFO ABDUCTION — SELECT DROP ZONE",
                _targetingTitleStyle
            );
            GUI.Label(
                new Rect(0, _sh - 42, _sw, 30),
                "WASD: Move   |   Scroll: Zoom   |   Click / Enter: Confirm   |   Space / RMB: Cancel",
                _targetingInstructionStyle
            );
        }

        private void DrawTargetingCornerBracket(
            float x,
            float y,
            float size,
            float thick,
            bool leftSide,
            bool topSide
        )
        {
            GUI.color = new Color(0.2f, 1f, 0.4f, 0.9f);

            float hx = leftSide ? x : x + size - size;
            float hy = topSide ? y : y + size - thick;
            GUI.DrawTexture(new Rect(hx, hy, size, thick), Texture2D.whiteTexture);

            float vx = leftSide ? x : x + size - thick;
            float vy = topSide ? y : y + size - size;
            GUI.DrawTexture(new Rect(vx, vy, thick, size), Texture2D.whiteTexture);

            GUI.color = Color.white;
        }

        // ── Being-targeted banner (victim during drop-zone selection) ─────────

        private void DrawBeingTargetedHUD()
        {
            const float h = 42f;
            float y = _sh - 12f - h;
            float pulse = 0.7f + 0.3f * Mathf.Sin(_time * 4f);
            _titleStyle.normal.textColor = new Color(1f, 0.6f, 0.2f, pulse);
            GUI.Label(new Rect(0, y, _sw, h), "YOU ARE BEING TARGETED", _titleStyle);
            _titleStyle.normal.textColor = new Color(0.3f, 1f, 0.5f, 0.9f); // restore default
        }

        // ── Aborted message ───────────────────────────────────────────────────

        private void DrawAbortedMessage()
        {
            const float h = 42f;
            float y = _sh - 12f - h;
            _instructionStyle.normal.textColor = new Color(0.3f, 1f, 0.5f, 0.9f);
            GUI.Label(new Rect(0, y, _sw, h), "UFO ABDUCTION CANCELLED", _instructionStyle);
            _instructionStyle.normal.textColor = Color.white;
        }

        // ── Wielder HUD ───────────────────────────────────────────────────────

        private void DrawWielderHUD()
        {
            const float h = 42f;
            float y = _sh - 12f - h;
            _titleStyle.normal.textColor = new Color(0.3f, 1f, 0.5f, 0.9f);
            GUI.Label(new Rect(0, y, _sw, h), "UFO ABDUCTION ACTIVE", _titleStyle);
        }

        // ── Busy message ──────────────────────────────────────────────────────

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

        // ── Active HUD (victim session) ───────────────────────────────────────

        private void DrawActiveHUD()
        {
            if (_activeSession == null)
                return;

            float totalDuration = _activeSession.TotalDuration;
            float elapsed = _time - _activeSession.StartTime;
            float remaining = Mathf.Max(0f, totalDuration - elapsed);
            bool lowTime = remaining <= 3f;

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
                phaseLabel = "IN TRANSIT";
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
            Color col = new Color(0.3f, 1f, 0.5f, 0.85f);

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

            _targetingTitleStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.4f, 1f, 0.6f) },
            };

            _targetingInstructionStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 1f, 0.8f) },
            };

            _targetingCornerStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.3f, 1f, 0.5f, 0.85f) },
            };
        }

        private void EnsureTargetingTextures()
        {
            if (_targetingBgTex == null)
            {
                _targetingBgTex = new Texture2D(1, 1);
                _targetingBgTex.SetPixel(0, 0, new Color(0f, 0.08f, 0.04f, 0.82f));
                _targetingBgTex.Apply();
            }

            // Scanlines are a 1x3 strip, not a screen-sized texture: the pattern only
            // varies along Y, so a full-resolution copy stored the same three rows a
            // few hundred times over and was rebuilt on every resolution change.
            // Drawn tiled in DrawDropoffTargetingOverlay for a pixel-identical result.
            if (_targetingScanlineTex == null)
            {
                _targetingScanlineTex = new Texture2D(1, ScanlinePeriod)
                {
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Point,
                };
                var pixels = new Color32[ScanlinePeriod];
                for (int py = 0; py < ScanlinePeriod; py++)
                    pixels[py] = py == 0 ? new Color32(0, 128, 26, 38) : new Color32(0, 0, 0, 0);
                _targetingScanlineTex.SetPixels32(pixels);
                _targetingScanlineTex.Apply(false);
            }

            if (_targetingNoiseTex == null)
            {
                _targetingNoiseTex = new Texture2D(256, 256);
                // Repeat so the drawn UV rect can be offset and still tile.
                _targetingNoiseTex.wrapMode = TextureWrapMode.Repeat;
                FillTargetingNoise(_targetingNoiseTex);
            }
        }

        /// Fills the targeting noise texture once. The animated "static" comes from
        /// offsetting the sampled UVs each tick rather than refilling these pixels,
        /// which previously allocated a 65k-element Color array and re-uploaded the
        /// texture several times a second.
        private static void FillTargetingNoise(Texture2D tex)
        {
            int count = tex.width * tex.height;
            var pixels = new Color32[count];
            for (int i = 0; i < count; i++)
            {
                byte v = (byte)(UnityEngine.Random.value * 255f);
                pixels[i] = new Color32(
                    (byte)(v * 0.3f),
                    v,
                    (byte)(v * 0.4f),
                    (byte)(UnityEngine.Random.Range(0.01f, 0.06f) * 255f)
                );
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);
        }
    }
}
