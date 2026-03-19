using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace IssaPlugin.Items
{
    /// Client-only MonoBehaviour (local player only) that draws a parabolic arc
    /// preview each frame showing where the sticky grenade will land.
    ///
    /// Uses the same origin/direction/speed math as StickyGrenadeNetworkBridge.ClientThrow
    /// so the preview exactly matches the actual throw.  The arc terminates early
    /// when a linecast hits terrain or static geometry (GroundLayerMask).
    ///
    /// Added by UpdateEquipmentSwitchersPatch when StickyGrenade is equipped.
    /// Self-destructs when a different item is equipped.
    public class StickyGrenadeTrajectoryPreview : MonoBehaviour
    {
        private const int ArcSteps = 40;
        private const float ArcTimeStep = 0.08f; // seconds per simulation step (~3.2 s total)

        private const int RingSegments = 32;
        private const float RingRadius = 0.55f; // matches StickyGrenadeStickRadius default
        private const float RingSurfaceOffset = 0.06f; // lift off surface to avoid z-fighting

        private LineRenderer _line;
        private LineRenderer _ring;
        private PlayerInventory _inventory;
        private readonly Vector3[] _positions = new Vector3[ArcSteps + 1];
        private readonly Vector3[] _ringPositions = new Vector3[RingSegments];

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();

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

            _line.startColor = new Color(1f, 0.85f, 0f, 0.9f); // bright yellow
            _line.endColor = new Color(1f, 0.40f, 0f, 0.1f); // faded orange

            // Landing ring — a looping circle drawn at the projected impact point.
            var ringGo = new GameObject("StickyGrenadeRing");
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

            var ringColor = new Color(1f, 0.85f, 0f, 0.85f);
            _ring.startColor = ringColor;
            _ring.endColor = ringColor;
            _ring.enabled = false;
        }

        private void Update()
        {
            if (
                _inventory == null
                || _inventory.GetEffectivelyEquippedItem(true)
                    != StickyGrenadeItem.StickyGrenadeItemType
            )
            {
                Destroy(this);
                return;
            }

            if (!(Mouse.current?.rightButton.isPressed ?? false))
            {
                _line.enabled = false;
                _ring.enabled = false;
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                _line.enabled = false;
                _ring.enabled = false;
                return;
            }

            _line.enabled = true;

            // Mirror ClientThrow exactly so the preview matches the real throw.
            Vector3 forward = cam.transform.forward;
            Vector3 pos = cam.transform.position + forward * 1.2f + Vector3.up * 0.3f;
            Vector3 vel =
                (forward + Vector3.up * Configuration.StickyGrenadeLobAngle.Value).normalized
                * Configuration.StickyGrenadeThrowSpeed.Value;

            // Combine both masks so the arc stops on terrain, walls, players,
            // and vehicles — mirroring the two-phase check in StickyGrenadeBehaviour.
            int stickMask =
                ItemHelper.GroundLayerMask | GameManager.LayerSettings.GunHittablesMask;

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
                        stickMask,
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

            // Update the landing ring on any stickable surface.
            _ring.enabled = hitSomething;
            if (hitSomething)
                UpdateRing(landingPoint, landingNormal);
        }

        private void UpdateRing(Vector3 center, Vector3 normal)
        {
            // Build two axes perpendicular to the surface normal so the ring
            // lies flush against the terrain regardless of slope.
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
                    origin + (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)) * RingRadius;
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
