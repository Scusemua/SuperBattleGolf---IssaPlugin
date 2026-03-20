using System.Collections.Generic;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Renders world-space health bars above every live bear, visible to all players.
    ///
    /// HP state is maintained via BearHPUpdateMessage, which the server broadcasts
    /// whenever a bear takes damage. The overlay simply stores (currentHP, maxHP)
    /// keyed by bear netId, then in OnGUI it projects each live bear's world position
    /// to screen space and draws a colour-coded bar.
    ///
    /// Bears that are no longer in NetworkClient.spawned (dead / cleaned up) are
    /// skipped automatically; stale entries are pruned at the end of each OnGUI call.
    ///
    /// Added to the Plugin's persistent GameObject in Plugin.cs.
    /// </summary>
    public class BearHealthBarOverlay : MonoBehaviour
    {
        public static BearHealthBarOverlay Instance { get; private set; }

        // ── HP data ────────────────────────────────────────────────────────────
        private readonly Dictionary<uint, (float current, float max)> _bearHPs = new();
        private readonly List<uint> _toRemove = new();

        // ── Rendering resources ────────────────────────────────────────────────
        private Texture2D _solidTex;
        private GUIStyle _labelStyle;

        // ── Bar dimensions (screen-space) ─────────────────────────────────────
        private const float BarWidth = 80f;
        private const float BarHeight = 8f;
        private const float LabelH = 14f;

        // ── Colours ────────────────────────────────────────────────────────────
        private static readonly Color BackColour = new Color(0.1f, 0.1f, 0.1f, 0.75f);
        private static readonly Color HealthHigh = new Color(0.15f, 0.85f, 0.15f, 0.90f);
        private static readonly Color HealthMid = new Color(0.90f, 0.75f, 0.10f, 0.90f);
        private static readonly Color HealthLow = new Color(0.90f, 0.15f, 0.10f, 0.90f);

        // ── Unity lifecycle ────────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;

            _solidTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _solidTex.SetPixel(0, 0, Color.white);
            _solidTex.Apply();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (_solidTex != null)
                Destroy(_solidTex);
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Called by the BearHPUpdateMessage handler to update stored HP for a bear.
        /// </summary>
        public static void HandleBearHPUpdate(BearHPUpdateMessage msg)
        {
            if (Instance == null)
                return;
            Instance._bearHPs[msg.BearNetId] = (msg.CurrentHP, msg.MaxHP);
        }

        // ── Rendering ──────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (_bearHPs.Count == 0)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            EnsureStyles();

            _toRemove.Clear();

            foreach (var kvp in _bearHPs)
            {
                uint netId = kvp.Key;
                (float current, float max) = kvp.Value;

                if (!NetworkClient.spawned.TryGetValue(netId, out var ni) || ni == null)
                {
                    _toRemove.Add(netId);
                    continue;
                }

                // Convert bear's head position to screen space.
                Vector3 worldPos = ni.transform.position + Vector3.up * 2.6f;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                // Skip if behind the camera or out of view.
                if (screenPos.z < 0f)
                    continue;
                if (screenPos.x < -BarWidth || screenPos.x > Screen.width + BarWidth)
                    continue;
                if (screenPos.y < -BarHeight || screenPos.y > Screen.height + BarHeight)
                    continue;

                // Flip Y (GUI uses top-left origin; Camera uses bottom-left).
                float sx = screenPos.x - BarWidth * 0.5f;
                float sy = Screen.height - screenPos.y - BarHeight * 0.5f;

                float fraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;

                // Background
                GUI.color = BackColour;
                GUI.DrawTexture(new Rect(sx - 1, sy - 1, BarWidth + 2, BarHeight + 2), _solidTex);

                // Filled portion — colour transitions green → yellow → red
                GUI.color =
                    fraction > 0.5f
                        ? Color.Lerp(HealthMid, HealthHigh, (fraction - 0.5f) * 2f)
                        : Color.Lerp(HealthLow, HealthMid, fraction * 2f);
                GUI.DrawTexture(new Rect(sx, sy, BarWidth * fraction, BarHeight), _solidTex);

                GUI.color = Color.white;

                // HP label centred below the bar.
                int hp = Mathf.CeilToInt(current);
                int hpMax = Mathf.RoundToInt(max);
                GUI.Label(
                    new Rect(sx, sy + BarHeight + 1f, BarWidth, LabelH),
                    $"{hp}/{hpMax}",
                    _labelStyle
                );
            }

            // Prune stale entries.
            foreach (var id in _toRemove)
                _bearHPs.Remove(id);
        }

        // ── Style initialisation ───────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 9,
                    fontStyle = FontStyle.Bold,
                };
                _labelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
            }
        }
    }
}
