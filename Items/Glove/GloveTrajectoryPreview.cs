using UnityEngine;
using IssaPlugin.Patches;

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
            // Match GolfBall.ApplyLinearDamping → Hittable.ApplyAirDamping so the
            // preview does not overestimate range on flatter throws. Spinach club
            // hits scale drag by 1/N²; mirror that when the buff is active.
            _preview.LinearAirDragFactor = () =>
            {
                float k =
                    GameManager.GolfBallSettings != null
                        ? GameManager.GolfBallSettings.LinearAirDragFactor
                        : 0f;
                float n = GloveThrowMath.GetSpinachThrowSpeedMultiplier(SpinachBehaviour.IsActive);
                if (n > 1.01f)
                    k /= n * n;
                return k;
            };
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

        private Vector3 GetThrowOrigin()
        {
            // Prefer the live ball pose once it is parented under the glove.
            var ball = _inventory?.PlayerInfo?.AsGolfer?.OwnBall;
            if (_bridge != null && _bridge.IsHolding && ball != null)
                return ball.transform.position;

            var info = _inventory?.PlayerInfo;
            Transform gloveModel = null;
            if (
                _inventory != null
                && LocalPlayerUpdateEquipmentSwitchers.TryGetCustomHeldModel(
                    _inventory,
                    out var modelTf
                )
            )
                gloveModel = modelTf;

            return GloveThrowMath.GetHeldWorldPosition(info, gloveModel);
        }

        private Vector3 GetThrowVelocity()
        {
            var cam = Camera.main;
            Vector3 aim = cam != null ? cam.transform.forward : transform.forward;
            float charge01 = _bridge != null && _bridge.IsCharging ? _bridge.Charge01 : 0f;
            float spinachMult = GloveThrowMath.GetSpinachThrowSpeedMultiplier(
                SpinachBehaviour.IsActive
            );
            return GloveThrowMath.ComputeThrowVelocity(
                aim,
                charge01,
                ModConfig.Glove.MinimumThrowSpeed.Value * spinachMult,
                ModConfig.Glove.MaximumThrowSpeed.Value * spinachMult,
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
