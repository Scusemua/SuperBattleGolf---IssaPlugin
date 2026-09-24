using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Client-only ballistic arc + optional landing-ring preview.
    ///
    /// Callers configure launch origin/velocity and visibility via Func hooks so
    /// fixed-speed lob items (sticky grenades) and charge-based throws (Glove)
    /// share one simulator / LineRenderer implementation.
    /// </summary>
    public class BallisticTrajectoryPreview : MonoBehaviour
    {
        private const int ArcSteps = 40;
        private const float ArcTimeStep = 0.08f; // seconds per simulation step (~3.2 s total)

        private const int RingSegments = 32;
        private const float RingSurfaceOffset = 0.06f;

        /// <summary>When false, the component destroys itself (typically unequipped).</summary>
        public Func<bool> ShouldKeepAlive;

        /// <summary>When false, the arc/ring are hidden this frame but the component stays.</summary>
        public Func<bool> IsActive;

        /// <summary>World-space launch origin for this frame.</summary>
        public Func<Vector3> GetOrigin;

        /// <summary>World-space launch velocity for this frame.</summary>
        public Func<Vector3> GetVelocity;

        /// <summary>
        /// Landing-ring radius. When null, a small default ring is used.
        /// Return ≤ 0 to hide the ring while still drawing the arc.
        /// </summary>
        public Func<float> RingRadius;

        public Color ArcStartColor = new Color(1f, 0.85f, 0f, 0.9f);
        public Color ArcEndColor = new Color(1f, 0.40f, 0f, 0.1f);
        public Color RingColor = new Color(1f, 0.85f, 0f, 0.85f);

        private LineRenderer _line;
        private LineRenderer _ring;
        private readonly Vector3[] _positions = new Vector3[ArcSteps + 1];
        private readonly Vector3[] _ringPositions = new Vector3[RingSegments];

        private void Awake()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = ArcSteps + 1;
            _line.startWidth = 0.07f;
            _line.endWidth = 0.02f;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.numCapVertices = 2;

            // Sprites/Default supports vertex-color alpha and is available in
            // both legacy and URP pipelines.
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
                _line.material = new Material(shader);

            _line.startColor = ArcStartColor;
            _line.endColor = ArcEndColor;
            _line.enabled = false;

            var ringGo = new GameObject("BallisticTrajectoryRing");
            ringGo.transform.SetParent(null, false);
            _ring = ringGo.AddComponent<LineRenderer>();
            _ring.useWorldSpace = true;
            _ring.loop = true;
            _ring.positionCount = RingSegments;
            _ring.startWidth = 0.06f;
            _ring.endWidth = 0.06f;
            _ring.shadowCastingMode = ShadowCastingMode.Off;
            _ring.receiveShadows = false;
            _ring.numCapVertices = 2;

            if (shader != null)
                _ring.material = new Material(shader);

            _ring.startColor = RingColor;
            _ring.endColor = RingColor;
            _ring.enabled = false;
        }

        private void Update()
        {
            if (ShouldKeepAlive != null && !ShouldKeepAlive())
            {
                Destroy(this);
                return;
            }

            bool active =
                IsActive != null
                && IsActive()
                && GetOrigin != null
                && GetVelocity != null;

            if (!active)
            {
                _line.enabled = false;
                _ring.enabled = false;
                return;
            }

            Vector3 pos = GetOrigin();
            Vector3 vel = GetVelocity();
            if (vel.sqrMagnitude < 0.0001f)
            {
                _line.enabled = false;
                _ring.enabled = false;
                return;
            }

            _line.startColor = ArcStartColor;
            _line.endColor = ArcEndColor;
            _line.enabled = true;

            // Terrain + hittables — same mask sticky grenades use so the arc stops
            // on ground, walls, players, and vehicles.
            int mask = ItemHelper.GroundLayerMask | GameManager.LayerSettings.GunHittablesMask;

            _positions[0] = pos;
            int count = 1;
            Vector3 landingPoint = pos;
            Vector3 landingNormal = Vector3.up;
            bool hitSomething = false;

            for (int i = 1; i <= ArcSteps; i++)
            {
                Vector3 next = pos + vel * ArcTimeStep;
                vel += Physics.gravity * ArcTimeStep;

                if (
                    Physics.Linecast(
                        pos,
                        next,
                        out RaycastHit hit,
                        mask,
                        QueryTriggerInteraction.Ignore
                    )
                )
                {
                    _positions[i] = hit.point;
                    count = i + 1;
                    landingPoint = hit.point;
                    landingNormal = hit.normal;
                    hitSomething = true;
                    break;
                }

                pos = next;
                _positions[i] = pos;
                count = i + 1;
                landingPoint = pos;
            }

            _line.positionCount = count;
            _line.SetPositions(_positions);

            float ringRadius = RingRadius != null ? RingRadius() : 0.55f;
            bool showRing = hitSomething && ringRadius > 0f;
            _ring.enabled = showRing;
            if (showRing)
            {
                _ring.startColor = RingColor;
                _ring.endColor = RingColor;
                UpdateRing(landingPoint, landingNormal, ringRadius);
            }
        }

        private void UpdateRing(Vector3 center, Vector3 normal, float radius)
        {
            Vector3 axisA = Vector3.Cross(normal, Vector3.forward);
            if (axisA.sqrMagnitude < 0.01f)
                axisA = Vector3.Cross(normal, Vector3.right);
            axisA.Normalize();
            Vector3 axisB = Vector3.Cross(axisA, normal).normalized;

            Vector3 origin = center + normal * RingSurfaceOffset;
            for (int i = 0; i < RingSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / RingSegments;
                _ringPositions[i] =
                    origin + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * radius;
            }

            _ring.SetPositions(_ringPositions);
        }

        private void OnDestroy()
        {
            if (_line != null)
                Destroy(_line);
            if (_ring != null)
                Destroy(_ring.gameObject);
        }
    }
}
