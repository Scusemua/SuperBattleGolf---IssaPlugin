using System.Collections.Generic;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Renders a brief toast feed in the top-right corner whenever a Harrier or Bear
    /// that the local player deployed lands a hit on another player.
    ///
    /// Usage: call HitNotificationOverlay.Instance?.AddNotification("text") from any
    /// client-side NetworkMessage handler.  Notifications auto-expire after a fixed
    /// lifetime and fade out in their final second.
    ///
    /// Added to the Plugin's persistent GameObject in Plugin.cs.
    /// </summary>
    public class HitNotificationOverlay : MonoBehaviour
    {
        public static HitNotificationOverlay Instance { get; private set; }

        private const float Lifetime  = 4f;
        private const int   MaxToasts = 5;

        private readonly List<(string Text, float ExpireTime)> _toasts = new();

        private GUIStyle _style;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // Expire old toasts once per frame (not inside OnGUI, which runs twice).
            float now = Time.time;
            _toasts.RemoveAll(t => t.ExpireTime <= now);
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void AddNotification(string message)
        {
            // Drop the oldest entry if we're at capacity so the list never grows unbounded.
            if (_toasts.Count >= MaxToasts)
                _toasts.RemoveAt(0);

            _toasts.Add((message, Time.time + Lifetime));
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (_toasts.Count == 0)
                return;

            EnsureStyle();

            const float Width    = 310f;
            const float LineH    = 22f;
            const float PadRight = 14f;
            const float PadTop   = 14f;

            float sw  = Screen.width;
            float now = Time.time;

            for (int i = 0; i < _toasts.Count; i++)
            {
                var (text, expireTime) = _toasts[i];

                float remaining = expireTime - now;
                float alpha = remaining < 1f ? remaining : 1f; // fast fade in final second

                _style.normal.textColor = new Color(1f, 0.95f, 0.6f, alpha);
                GUI.Label(
                    new Rect(sw - Width - PadRight, PadTop + i * LineH, Width, LineH),
                    text,
                    _style
                );
            }
        }

        private void EnsureStyle()
        {
            if (_style != null)
                return;

            _style = new GUIStyle(GUI.skin.label)
            {
                alignment  = TextAnchor.MiddleRight,
                fontSize   = 13,
                fontStyle  = FontStyle.Bold,
            };
        }
    }
}
