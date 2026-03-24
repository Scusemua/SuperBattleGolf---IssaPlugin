using System.Collections.Generic;
using IssaPlugin.Items;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Displays warning banners and optional PiP cameras when a player activates a
    /// warning-enabled item.
    ///
    /// Warning text — stacked yellow banners, horizontally centered at 20% screen height.
    /// PiP camera   — bottom-right corner, 320×180, tracks the networked item object.
    ///
    /// Items with PiP camera support:
    ///   101  Stealth Bomber  — polls StealthBomberItem.ActiveBomberVisual
    ///   103  AC-130 Gunship  — tracks gunship by netId
    ///   107  Donut           — tracks donut by netId
    ///   111  Nuke            — tracks falling bomb by netId; zooms out on detonation
    ///
    /// Added to the plugin's persistent GameObject in Plugin.cs.
    /// </summary>
    public class ItemWarningOverlay : MonoBehaviour
    {
        public static ItemWarningOverlay Instance { get; private set; }

        // ── PiP constants ─────────────────────────────────────────────────────
        private const int PiPWidth = 320;
        private const int PiPHeight = 180;
        private const float PiPMargin = 16f;
        private const float PostDetonationDuration = 2.5f;

        // ── Banner constants ──────────────────────────────────────────────────
        private const float BannerWidth = 640f;
        private const float BannerHeight = 48f;
        private const float BannerSpacing = 6f;
        private const float BannerStartY = 0.20f; // fraction of screen height

        // ── Item type ints for switch statements ──────────────────────────────
        private static readonly int TypeBomber = (int)ItemRegistry.StealthBomberItemType;
        private static readonly int TypeAC130 = (int)ItemRegistry.AC130ItemType;
        private static readonly int TypeDonut = (int)ItemRegistry.DonutItemType;
        private static readonly int TypeNuke = (int)ItemRegistry.NukeItemType;

        // ── Warning entry ─────────────────────────────────────────────────────
        private class WarningEntry
        {
            public string Text;
            public float ExpiryTime;

            // Warning particle (attached to camera so it's always on-screen)
            public GameObject ParticleInstance;

            // PiP camera
            public bool ShowPiP;
            public uint TrackedNetId;
            public int ItemTypeValue;
            public Camera PiPCamera;
            public RenderTexture PiPTexture;
            public GameObject PiPCameraGo;

            // Stealth Bomber: visual spawns slightly after the warning message
            public bool IsPollingForBomber;

            // Nuke post-detonation zoom-out
            public bool TargetLost;
            public Vector3 LastKnownPosition;
            public float PostDetonationTimer;
        }

        private readonly List<WarningEntry> _warnings = new();

        // ── GUI resources (lazy-initialised on first OnGUI call) ──────────────
        private GUIStyle _bannerStyle;
        private GUIStyle _shadowStyle;
        private GUIStyle _pipLabelStyle;
        private Texture2D _solidTex;
        private bool _stylesReady;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            foreach (var w in _warnings)
                CleanupEntry(w);
            _warnings.Clear();
        }

        // ── Public entry point (called by NetworkClient message handler) ───────

        public static void HandleWarningMessage(ItemWarningMessage msg)
        {
            Instance?.AddWarning(msg);
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private void AddWarning(ItemWarningMessage msg)
        {
            bool isSelf =
                !Configuration.WarningShowForSelf.Value
                && NetworkClient.localPlayer != null
                && NetworkClient.localPlayer.netId == msg.SenderNetId;

            float duration = Configuration.WarningDuration.Value;

            var entry = new WarningEntry
            {
                Text = $"WARNING: {msg.PlayerName} has deployed {msg.ItemDisplayName}!",
                ExpiryTime = Time.time + duration,
                TrackedNetId = msg.TrackedNetId,
                ItemTypeValue = msg.ItemTypeValue,
                ShowPiP = !isSelf && SupportsPiP(msg.ItemTypeValue),
            };

            // Warning particle — attach to camera so it follows the view
            if (!isSelf && AssetLoader.WarningParticlePrefab != null)
            {
                var go = Instantiate(AssetLoader.WarningParticlePrefab);
                var cam = Camera.main;
                if (cam != null)
                {
                    go.transform.SetParent(cam.transform, worldPositionStays: false);
                    go.transform.localPosition = new Vector3(0f, 0f, 2.5f);
                    go.transform.localRotation = Quaternion.identity;
                }
                entry.ParticleInstance = go;
            }

            // PiP camera setup
            if (entry.ShowPiP)
            {
                if (msg.ItemTypeValue == TypeBomber)
                {
                    entry.IsPollingForBomber = true;
                    TryAttachBomberCamera(entry); // may succeed immediately on host
                }
                else if (msg.TrackedNetId != 0)
                {
                    TryAttachNetIdCamera(entry);
                }
            }

            _warnings.Add(entry);
        }

        private void LateUpdate()
        {
            float now = Time.time;

            for (int i = _warnings.Count - 1; i >= 0; i--)
            {
                var w = _warnings[i];

                // ── Try to attach bomber camera once the visual becomes available ──
                if (w.IsPollingForBomber && w.PiPCameraGo == null)
                    TryAttachBomberCamera(w);

                // ── Track PiP target ──────────────────────────────────────────────
                if (w.PiPCameraGo != null && !w.TargetLost)
                {
                    Transform target = GetPiPTarget(w);

                    if (target != null)
                    {
                        w.LastKnownPosition = target.position;
                        PositionPiPCamera(w.PiPCameraGo.transform, target, w.ItemTypeValue);
                    }
                    else if (w.ItemTypeValue == TypeNuke)
                    {
                        // Bomb has detonated — start zoom-out phase
                        w.TargetLost = true;
                        w.PostDetonationTimer = PostDetonationDuration;
                        // Ensure the warning stays visible through the zoom-out
                        w.ExpiryTime = Mathf.Max(w.ExpiryTime, now + PostDetonationDuration + 0.5f);
                    }
                    else
                    {
                        // Non-nuke object gone — drop PiP
                        DestroyPiPCamera(w);
                    }
                }

                // ── Nuke post-detonation: pull camera back for dramatic reveal ────
                if (w.TargetLost && w.ItemTypeValue == TypeNuke && w.PiPCameraGo != null)
                {
                    w.PostDetonationTimer -= Time.deltaTime;
                    if (w.PostDetonationTimer <= 0f)
                    {
                        DestroyPiPCamera(w);
                    }
                    else
                    {
                        float t = 1f - w.PostDetonationTimer / PostDetonationDuration;
                        float pullback = Mathf.Lerp(25f, 90f, t);
                        var camT = w.PiPCameraGo.transform;
                        Vector3 dir = (camT.position - w.LastKnownPosition).normalized;
                        if (dir == Vector3.zero)
                            dir = Vector3.up;
                        camT.position = w.LastKnownPosition + dir * pullback;
                        camT.LookAt(w.LastKnownPosition);
                    }
                }

                // ── Expire ────────────────────────────────────────────────────────
                if (now >= w.ExpiryTime)
                {
                    CleanupEntry(w);
                    _warnings.RemoveAt(i);
                }
            }
        }

        private void OnGUI()
        {
            if (_warnings.Count == 0)
                return;

            EnsureStyles();

            float sw = Screen.width;
            float sh = Screen.height;

            // ── Warning banners (stacked, centered) ───────────────────────────
            float bannerX = (sw - BannerWidth) * 0.5f;
            float bannerY = sh * BannerStartY;

            for (int i = 0; i < _warnings.Count; i++)
            {
                float y = bannerY + i * (BannerHeight + BannerSpacing);
                DrawBanner(new Rect(bannerX, y, BannerWidth, BannerHeight), _warnings[i].Text);
            }

            // ── PiP (most recently activated entry with a live camera) ─────────
            for (int i = _warnings.Count - 1; i >= 0; i--)
            {
                var w = _warnings[i];
                if (w.PiPCamera == null || w.PiPTexture == null)
                    continue;

                float pipX = sw - PiPWidth - PiPMargin;
                float pipY = sh - PiPHeight - PiPMargin;
                var pipRect = new Rect(pipX, pipY, PiPWidth, PiPHeight);

                // Yellow border
                var saved = GUI.color;
                GUI.color = new Color(1f, 0.85f, 0.1f, 0.9f);
                GUI.DrawTexture(
                    new Rect(pipRect.x - 3, pipRect.y - 3, pipRect.width + 6, pipRect.height + 6),
                    _solidTex
                );
                GUI.color = saved;

                // Camera feed
                GUI.DrawTexture(pipRect, w.PiPTexture, ScaleMode.ScaleToFit);

                // Small label inside the PiP box
                GUI.Label(new Rect(pipX + 4, pipY + 4, PiPWidth - 8, 22), w.Text, _pipLabelStyle);

                break; // only the most recent PiP
            }
        }

        // ── PiP camera helpers ────────────────────────────────────────────────

        private static bool SupportsPiP(int itemTypeValue) =>
            itemTypeValue == TypeBomber
            || itemTypeValue == TypeAC130
            || itemTypeValue == TypeDonut
            || itemTypeValue == TypeNuke;

        private void TryAttachBomberCamera(WarningEntry entry)
        {
            if (StealthBomberItem.ActiveBomberVisual == null)
                return;
            AllocatePiPCamera(entry);
            entry.IsPollingForBomber = false;
        }

        private void TryAttachNetIdCamera(WarningEntry entry)
        {
            if (!NetworkClient.spawned.TryGetValue(entry.TrackedNetId, out var ni) || ni == null)
                return;
            AllocatePiPCamera(entry);
        }

        private void AllocatePiPCamera(WarningEntry entry)
        {
            if (entry.PiPCameraGo != null)
                return;

            var go = new GameObject("[IssaMod] PiP Camera");
            DontDestroyOnLoad(go);

            var rt = new RenderTexture(PiPWidth, PiPHeight, 16, RenderTextureFormat.Default);
            rt.Create();

            var cam = go.AddComponent<Camera>();
            cam.targetTexture = rt;
            cam.fieldOfView = 60f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 2000f;
            cam.cullingMask = Camera.main?.cullingMask ?? -1;
            cam.depth = (Camera.main?.depth ?? 0f) - 1f;

            entry.PiPCamera = cam;
            entry.PiPTexture = rt;
            entry.PiPCameraGo = go;
        }

        /// Positions the PiP camera relative to <paramref name="target"/> using
        /// item-type-specific offsets. Local-space offsets are used where the object
        /// has a meaningful forward direction (bomber, AC130, donut); world-space is
        /// used for the nuke bomb which falls straight down.
        private static void PositionPiPCamera(Transform camT, Transform target, int itemTypeValue)
        {
            if (itemTypeValue == TypeNuke)
            {
                // World-space: above and slightly behind the falling bomb
                camT.position = target.position + new Vector3(0f, 20f, -10f);
                camT.LookAt(target.position);
                return;
            }

            // All other items: local-space offset so camera stays behind the object
            Vector3 localOffset;
            Vector3 lookOffset = Vector3.up * 1.5f;

            if (itemTypeValue == TypeBomber)
                localOffset = new Vector3(0f, 6f, -22f);
            else if (itemTypeValue == TypeAC130)
                localOffset = new Vector3(0f, 8f, -22f);
            else if (itemTypeValue == TypeDonut)
                localOffset = new Vector3(0f, 12f, -18f);
            else
                localOffset = new Vector3(0f, 10f, -20f);

            camT.position = target.position + target.TransformDirection(localOffset);
            camT.LookAt(target.position + lookOffset);
        }

        private static Transform GetPiPTarget(WarningEntry entry)
        {
            if (entry.ItemTypeValue == TypeBomber)
                return StealthBomberItem.ActiveBomberVisual?.transform;

            if (entry.TrackedNetId == 0)
                return null;

            return NetworkClient.spawned.TryGetValue(entry.TrackedNetId, out var ni) && ni != null
                ? ni.transform
                : null;
        }

        private static void DestroyPiPCamera(WarningEntry entry)
        {
            if (entry.PiPCamera != null)
            {
                entry.PiPCamera.targetTexture = null;
                entry.PiPCamera = null;
            }
            if (entry.PiPTexture != null)
            {
                entry.PiPTexture.Release();
                Destroy(entry.PiPTexture);
                entry.PiPTexture = null;
            }
            if (entry.PiPCameraGo != null)
            {
                Destroy(entry.PiPCameraGo);
                entry.PiPCameraGo = null;
            }
        }

        private static void CleanupEntry(WarningEntry entry)
        {
            DestroyPiPCamera(entry);
            if (entry.ParticleInstance != null)
            {
                Destroy(entry.ParticleInstance);
                entry.ParticleInstance = null;
            }
        }

        // ── GUI helpers ───────────────────────────────────────────────────────

        private void DrawBanner(Rect rect, string text)
        {
            var saved = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(rect, _solidTex);
            GUI.color = saved;

            GUI.Label(
                new Rect(rect.x + 2, rect.y + 2, rect.width, rect.height),
                text,
                _shadowStyle
            );
            GUI.Label(rect, text, _bannerStyle);
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
                return;

            _solidTex = new Texture2D(1, 1);
            _solidTex.SetPixel(0, 0, Color.white);
            _solidTex.Apply();

            _bannerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.90f, 0.1f, 1f) },
            };
            _shadowStyle = new GUIStyle(_bannerStyle)
            {
                normal = { textColor = new Color(0f, 0f, 0f, 0.85f) },
            };
            _pipLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.90f, 0.1f, 1f) },
            };

            _stylesReady = true;
        }
    }
}
