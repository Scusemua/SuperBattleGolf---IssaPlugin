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

        private LineRenderer _line;
        private PlayerInventory _inventory;
        private readonly Vector3[] _positions = new Vector3[ArcSteps + 1];

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
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                _line.enabled = false;
                return;
            }

            _line.enabled = true;

            // Mirror ClientThrow exactly so the preview matches the real throw.
            Vector3 forward = cam.transform.forward;
            Vector3 pos = cam.transform.position + forward * 1.2f + Vector3.up * 0.3f;
            Vector3 vel =
                (forward + Vector3.up * Configuration.StickyGrenadeLobAngle.Value).normalized
                * Configuration.StickyGrenadeThrowSpeed.Value;

            _positions[0] = pos;
            int count = 1;

            for (int i = 1; i <= ArcSteps; i++)
            {
                Vector3 next = pos + vel * ArcTimeStep;
                vel += Physics.gravity * ArcTimeStep;

                if (
                    Physics.Linecast(
                        pos,
                        next,
                        ItemHelper.GroundLayerMask,
                        QueryTriggerInteraction.Ignore
                    )
                )
                {
                    // Hit terrain — show the approximate landing point and stop.
                    _positions[i] = next;
                    count = i + 1;
                    break;
                }

                pos = next;
                _positions[i] = pos;
                count = i + 1;
            }

            _line.positionCount = count;
            _line.SetPositions(_positions);
        }

        private void OnDestroy()
        {
            if (_line != null)
                Destroy(_line);
        }
    }
}
