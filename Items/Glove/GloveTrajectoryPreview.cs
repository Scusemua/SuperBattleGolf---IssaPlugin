using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Client-only throw-arc preview while the Glove is equipped.
    ///
    /// Shows the ballistic path from the held-ball origin using
    /// <see cref="GloveThrowMath"/> (same formula as the server throw), while the
    /// local player is holding their ball and aiming. Charge fills the arc speed
    /// from min→max; aiming without charging previews minimum throw speed.
    /// </summary>
    public class GloveTrajectoryPreview : MonoBehaviour
    {
        private const float LandingRingRadius = 0.12f;

        private PlayerInventory _inventory;
        private GloveNetworkBridge _bridge;
        private BallisticTrajectoryPreview _preview;

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();
            _bridge = GetComponent<GloveNetworkBridge>();

            _preview = gameObject.AddComponent<BallisticTrajectoryPreview>();
            _preview.ShouldKeepAlive = () =>
                _inventory != null
                && _inventory.GetEffectivelyEquippedItem(true) == ItemRegistry.GloveItemType;
            _preview.IsActive = IsPreviewActive;
            _preview.GetOrigin = GetThrowOrigin;
            _preview.GetVelocity = GetThrowVelocity;
            _preview.RingRadius = () => LandingRingRadius;
        }

        private void Update()
        {
            if (
                _inventory == null
                || _inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.GloveItemType
            )
                Destroy(this);
        }

        private bool IsPreviewActive()
        {
            if (_bridge == null || !_bridge.IsHolding)
                return false;

            var input = _inventory?.PlayerInfo?.Input;
            return input != null && input.IsHoldingAimSwing;
        }

        private Vector3 GetThrowOrigin() =>
            GloveThrowMath.GetHeldWorldPosition(transform);

        private Vector3 GetThrowVelocity()
        {
            var cam = Camera.main;
            Vector3 aim = cam != null ? cam.transform.forward : transform.forward;
            float charge01 = _bridge != null && _bridge.IsCharging ? _bridge.Charge01 : 0f;
            return GloveThrowMath.ComputeThrowVelocity(
                aim,
                charge01,
                ModConfig.Glove.MinimumThrowSpeed.Value,
                ModConfig.Glove.MaximumThrowSpeed.Value,
                ModConfig.Glove.ThrowUpwardBias.Value
            );
        }

        private void OnDestroy()
        {
            if (_preview != null)
                Destroy(_preview);
        }
    }
}
