using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Baked onto the orb prefab for the scale/flash writer. The prefab already
    /// has its collider, rigidbody, and network components. Does not move the pivot.
    public class OrbBomberClientSetup : MonoBehaviour
    {
        /// Added to the ground height to place the pivot so the visible bottom sits on the ground at scale 1.
        /// Negative when the mesh sits above its origin.
        public float BaseRadius { get; private set; } = 0.5f;

        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private bool _playing;
        private float _start;
        private float _duration = 1f;
        private int _flashCount = 1;
        private float _sizeMultiplier = 1f;

        private void Awake()
        {
            // AssetLoader adds this component to the loaded prefab asset.
            // Measure the instance, not that asset.
            if (!gameObject.scene.IsValid())
                return;

            SetHittableLayer(gameObject);

            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();

            // Leave the prefab's 1 m model offset in place. Shifting that child
            // down onto this pivot buried the sphere. Plant from whichever
            // bottom sits lower: the collider, or the visible mesh.
            float drop = 0.5f;
            Vector3 massPoint = transform.position;
            var sphere = GetComponentInChildren<SphereCollider>(true);
            if (sphere != null)
            {
                Vector3 center = sphere.transform.TransformPoint(sphere.center);
                Vector3 bottom = sphere.transform.TransformPoint(
                    sphere.center + Vector3.down * sphere.radius
                );
                drop = transform.position.y - bottom.y;
                massPoint = center;
            }

            if (TryGetVisualBounds(out Bounds bounds))
            {
                drop = Mathf.Max(drop, transform.position.y - bounds.min.y);
                massPoint = bounds.center;
            }

            BaseRadius = drop;

            var body = GetComponent<Rigidbody>();
            if (body != null)
                body.centerOfMass = transform.InverseTransformPoint(massPoint);
        }

        private bool TryGetVisualBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            if (_renderers == null)
                return false;

            foreach (var renderer in _renderers)
            {
                if (renderer == null)
                    continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        public void PlaySequence(float duration, int flashCount, float sizeMultiplier)
        {
            _playing = true;
            _start = Time.time;
            _duration = Mathf.Max(0.05f, duration);
            _flashCount = Mathf.Max(1, flashCount);
            _sizeMultiplier = Mathf.Max(1f, sizeMultiplier);
            ApplyVisual(0f);
        }

        public void ResetSequence()
        {
            _playing = false;
            transform.localScale = Vector3.one;
            ClearFlash();
        }

        /// Writes localScale and the white flash from a single elapsed time.
        /// The server reads the returned scale for its pivot lift.
        public float ApplyVisual(float elapsed)
        {
            float u = Mathf.Clamp01(elapsed / _duration);
            float scale = Mathf.Lerp(1f, _sizeMultiplier, Mathf.SmoothStep(0f, 1f, u));
            transform.localScale = new Vector3(scale, scale, scale);

            float period = _duration / _flashCount;
            float phase = period > 0.0001f ? (elapsed % period) / period : 0f;
            if (u < 1f && phase < 0.5f)
                SetFlash(Color.white);
            else
                ClearFlash();

            return scale;
        }

        private void Update()
        {
            // The host's behaviour calls ApplyVisual from FixedUpdate. Running it
            // here as well would advance a second clock on the same object.
            if (!_playing || NetworkServer.active)
                return;

            ApplyVisual(Time.time - _start);
        }

        public static void HandleSequenceStart(OrbBomberSequenceStartMessage msg)
        {
            if (NetworkServer.active)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.OrbNetId, out var identity))
                return;

            identity.GetComponent<OrbBomberClientSetup>()
                ?.PlaySequence(msg.Duration, msg.FlashCount, msg.SizeMultiplier);
        }

        public static void HandleSequenceReset(OrbBomberSequenceResetMessage msg)
        {
            if (NetworkServer.active)
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.OrbNetId, out var identity))
                return;

            identity.GetComponent<OrbBomberClientSetup>()?.ResetSequence();
        }

        private void SetFlash(Color color)
        {
            if (_renderers == null || _block == null)
                return;

            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            _block.SetColor(EmissionId, color);
            foreach (var renderer in _renderers)
            {
                if (renderer != null)
                    renderer.SetPropertyBlock(_block);
            }
        }

        private void ClearFlash()
        {
            if (_renderers == null)
                return;

            foreach (var renderer in _renderers)
            {
                if (renderer != null)
                    renderer.SetPropertyBlock(null);
            }
        }

        private static void SetHittableLayer(GameObject go)
        {
            var layers = GameManager.LayerSettings;
            if (layers == null)
                return;

            ApplyLayer(go, layers.HittablesLayer);
        }

        private static void ApplyLayer(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                ApplyLayer(go.transform.GetChild(i).gameObject, layer);
        }
    }
}
