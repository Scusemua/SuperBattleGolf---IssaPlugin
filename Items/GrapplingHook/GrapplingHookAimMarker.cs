using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace IssaPlugin.Items
{
    /// Ring on the surface under the crosshair. Green when a left click will attach.
    /// Added from OnEquip and removed when the hook is no longer in hand.
    public class GrapplingHookAimMarker : MonoBehaviour
    {
        private const int RingSegments = 32;
        private const float RingRadius = 0.7f;
        private const float SurfaceOffset = 0.06f;

        private static readonly Color ValidColor = new Color(0.2f, 1f, 0.35f, 0.95f);
        private static readonly Color InvalidColor = new Color(1f, 0.28f, 0.22f, 0.75f);

        private PlayerInventory _inventory;
        private LineRenderer _ring;
        private Material _ringMaterial;
        private readonly Vector3[] _ringPositions = new Vector3[RingSegments];

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();

            var ringObject = new GameObject("GrappleAimRing");
            _ring = ringObject.AddComponent<LineRenderer>();
            _ring.useWorldSpace = true;
            _ring.loop = true;
            _ring.positionCount = RingSegments;
            _ring.startWidth = 0.07f;
            _ring.endWidth = 0.07f;
            _ring.shadowCastingMode = ShadowCastingMode.Off;
            _ring.receiveShadows = false;
            _ring.numCapVertices = 2;
            _ring.enabled = false;

            var shader =
                Shader.Find("Sprites/Default")
                ?? Shader.Find("UI/Default")
                ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                _ringMaterial = new Material(shader);
                _ring.sharedMaterial = _ringMaterial;
            }
        }

        private void Update()
        {
            if (
                _inventory == null
                || _inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.GrapplingHookItemType
            )
            {
                Destroy(this);
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed)
                GrapplingHookItem.ClearFireLatch();

            GrapplingHookItem.AimSample aim = GrapplingHookItem.EvaluateAim(_inventory);
            if (!aim.Hit || _ring == null)
            {
                if (_ring != null)
                    _ring.enabled = false;
                return;
            }

            Color color = aim.Valid ? ValidColor : InvalidColor;
            _ring.enabled = true;
            _ring.startColor = color;
            _ring.endColor = color;
            PlaceRing(aim.Point, aim.Normal);
        }

        private void PlaceRing(Vector3 center, Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.0001f)
                normal = Vector3.up;
            normal.Normalize();

            Vector3 axisA = Vector3.Cross(normal, Vector3.forward);
            if (axisA.sqrMagnitude < 0.01f)
                axisA = Vector3.Cross(normal, Vector3.right);
            axisA.Normalize();
            Vector3 axisB = Vector3.Cross(axisA, normal).normalized;

            Vector3 origin = center + normal * SurfaceOffset;
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
            if (_ringMaterial != null)
                Destroy(_ringMaterial);
            if (_ring != null)
                Destroy(_ring.gameObject);
        }
    }
}
