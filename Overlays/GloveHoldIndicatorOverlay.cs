using System.Collections.Generic;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

namespace IssaPlugin.Overlays
{
    /// <summary>
    /// Local-only held-ball indicators above every player currently carrying
    /// a golf ball with the Glove or Evil Glove. One instance per holder netId —
    /// every client spawns its own copy when it receives <c>GloveHoldStartedMessage</c>.
    ///
    /// Built at runtime from <see cref="AssetLoader.GloveBallIndicatorIcon"/> (a PNG
    /// sprite) — no prefab required. Height uses <see cref="ModConfig.Glove"/>'s
    /// IndicatorHeight for both glove types.
    /// </summary>
    public class GloveHoldIndicatorOverlay : MonoBehaviour
    {
        /// <summary>World scale of the PNG billboard (smaller than the inventory icon).</summary>
        private const float IndicatorWorldScale = 0.22f;

        public static GloveHoldIndicatorOverlay Instance { get; private set; }

        private readonly Dictionary<uint, IndicatorInstance> _indicators = new();
        private Material _sharedBillboardMaterial;

        private struct IndicatorInstance
        {
            public GameObject Go;
            public Material OwnedMaterial;
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            ClearAll();
            if (_sharedBillboardMaterial != null)
            {
                Destroy(_sharedBillboardMaterial);
                _sharedBillboardMaterial = null;
            }
        }

        public void Show(uint holderNetId)
        {
            if (_indicators.ContainsKey(holderNetId))
                return;

            if (!TryResolvePlayer(holderNetId, out var playerInfo))
                return;

            var sprite = AssetLoader.GloveBallIndicatorIcon;
            if (sprite == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Glove] No indicator icon loaded (glove_ball_indicator.png / glove_icon.png)."
                );
                return;
            }

            var indicator = new GameObject("GloveBallIndicator");
            indicator.layer = 0; // Default — visible to gameplay cameras

            var renderer = indicator.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            var mat = GetOrCreateBillboardMaterial();
            if (mat != null)
                renderer.sharedMaterial = mat;
            renderer.sortingOrder = 200;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Parent to HeadBone so the offset sits above the skull, not the feet.
            Transform anchor =
                playerInfo.HeadBone != null ? playerInfo.HeadBone : playerInfo.transform;
            indicator.transform.SetParent(anchor, false);
            indicator.transform.localPosition = new Vector3(0f, GetHeightAboveHead(), 0f);
            indicator.transform.localRotation = Quaternion.identity;
            indicator.transform.localScale = Vector3.one * IndicatorWorldScale;
            indicator.SetActive(true);

            _indicators[holderNetId] = new IndicatorInstance
            {
                Go = indicator,
                OwnedMaterial = null,
            };
        }

        public void Hide(uint holderNetId)
        {
            if (!_indicators.TryGetValue(holderNetId, out var inst))
                return;
            _indicators.Remove(holderNetId);
            if (inst.Go != null)
                Destroy(inst.Go);
            if (inst.OwnedMaterial != null)
                Destroy(inst.OwnedMaterial);
        }

        public void ClearAll()
        {
            foreach (var inst in _indicators.Values)
            {
                if (inst.Go != null)
                    Destroy(inst.Go);
                if (inst.OwnedMaterial != null)
                    Destroy(inst.OwnedMaterial);
            }
            _indicators.Clear();
        }

        private static bool TryResolvePlayer(uint holderNetId, out PlayerInfo playerInfo)
        {
            playerInfo = null;

            // Prefer the client map so pure clients resolve remote holders.
            if (
                NetworkClient.active
                && NetworkClient.spawned.TryGetValue(holderNetId, out var clientId)
                && clientId != null
            )
            {
                playerInfo = clientId.GetComponent<PlayerInfo>();
                if (playerInfo != null)
                    return true;
            }

            if (
                NetworkServer.active
                && NetworkServer.spawned.TryGetValue(holderNetId, out var serverId)
                && serverId != null
            )
            {
                playerInfo = serverId.GetComponent<PlayerInfo>();
                return playerInfo != null;
            }

            return false;
        }

        private static float GetHeightAboveHead() =>
            Mathf.Max(0.15f, ModConfig.Glove.IndicatorHeight.Value);

        private Material GetOrCreateBillboardMaterial()
        {
            if (_sharedBillboardMaterial != null)
                return _sharedBillboardMaterial;

            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                IssaPluginPlugin.Log.LogWarning("[Glove] Sprites/Default shader not found.");
                return null;
            }

            _sharedBillboardMaterial = new Material(shader);
            return _sharedBillboardMaterial;
        }

        private void LateUpdate()
        {
            float height = GetHeightAboveHead();
            var cam = Camera.main;
            var stale = new List<uint>();

            foreach (var kv in _indicators)
            {
                var go = kv.Value.Go;
                if (go == null)
                {
                    stale.Add(kv.Key);
                    continue;
                }

                go.transform.localPosition = new Vector3(0f, height, 0f);
                go.transform.localScale = Vector3.one * IndicatorWorldScale;

                // Face each viewer's camera so the PNG is readable for everyone.
                if (cam != null)
                {
                    Vector3 toCam = cam.transform.position - go.transform.position;
                    if (toCam.sqrMagnitude > 0.0001f)
                        go.transform.rotation = Quaternion.LookRotation(
                            toCam.normalized,
                            cam.transform.up
                        );
                }

                bool stillSpawned =
                    (NetworkClient.active && NetworkClient.spawned.ContainsKey(kv.Key))
                    || (NetworkServer.active && NetworkServer.spawned.ContainsKey(kv.Key));
                if (!stillSpawned)
                    stale.Add(kv.Key);
            }

            foreach (var id in stale)
                Hide(id);
        }
    }
}
