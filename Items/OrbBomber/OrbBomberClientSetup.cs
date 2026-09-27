using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Baked onto the orb prefab for the scale/flash writer. The prefab already
    /// has its collider, rigidbody, and network components. Does not move the pivot.
    public class OrbBomberClientSetup : MonoBehaviour
    {
        /// Added to the ground height so the visible bottom sits on the ground at scale 1.
        /// This is the pivot-to-bottom distance, not the sphere's radius. Negative when the
        /// mesh sits entirely above the pivot.
        public float BaseRadius { get; private set; } = 0.5f;

        /// Horizontal distance from the pivot to the outside of the sphere at scale 1.
        public float BodyRadius { get; private set; } = 0.5f;

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

            // Leave the prefab's model offset in place. The mesh is authored above
            // this pivot; moving it down onto the pivot buried the sphere.
            float drop = 0.5f;
            float reach = 0.5f;
            Vector3 massPoint = transform.position;
            var sphere = GetComponentInChildren<SphereCollider>(true);
            if (sphere != null)
            {
                Vector3 lossy = sphere.transform.lossyScale;
                float maxAxis = Mathf.Max(
                    Mathf.Abs(lossy.x),
                    Mathf.Abs(lossy.y),
                    Mathf.Abs(lossy.z)
                );
                float worldRadius = sphere.radius * maxAxis;
                Vector3 center = sphere.transform.TransformPoint(sphere.center);
                drop = transform.position.y - (center.y - worldRadius);
                reach = HorizontalReach(center, worldRadius);
                massPoint = center;
            }

            if (TryGetVisualBounds(out Bounds bounds))
            {
                drop = Mathf.Max(drop, transform.position.y - bounds.min.y);
                reach = Mathf.Max(
                    reach,
                    HorizontalReach(bounds.center, Mathf.Max(bounds.extents.x, bounds.extents.z))
                );
                massPoint = bounds.center;
            }

            BaseRadius = drop;
            BodyRadius = Mathf.Max(0.05f, reach);

            var body = GetComponent<Rigidbody>();
            if (body != null)
                body.centerOfMass = transform.InverseTransformPoint(massPoint);
        }

        private float HorizontalReach(Vector3 worldCenter, float radius)
        {
            float dx = worldCenter.x - transform.position.x;
            float dz = worldCenter.z - transform.position.z;
            return Mathf.Sqrt(dx * dx + dz * dz) + radius;
        }

        private bool TryGetVisualBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            if (_renderers == null)
                return false;

            foreach (var renderer in _renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
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
