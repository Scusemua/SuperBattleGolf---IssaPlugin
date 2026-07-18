using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// Client-only MonoBehaviour added to the local player when the Javelin is
    /// equipped.  Each Update it refreshes the raycast lock-on position and
    /// moves/shows/hides the ground-target indicator accordingly.
    ///
    /// Lifecycle:
    ///   Added by LocalPlayerUpdateEquipmentSwitchers when the local player equips Javelin.
    ///   Self-destructs (and destroys the indicator object) once Javelin is unequipped.
    ///
    /// Indicator visibility rules:
    ///   Aiming (HasLock, not yet fired)   → indicator follows the crosshair each frame.
    ///   In-flight (IsWaitingForDetonation) → indicator stays pinned at the fired position.
    ///   After detonation / no lock        → indicator hidden.
    public class JavelinLockOnIndicator : MonoBehaviour
    {
        private JavelinNetworkBridge _bridge;
        private PlayerInventory _inventory;
        private GameObject _indicator;

        private void Awake()
        {
            _bridge = GetComponent<JavelinNetworkBridge>();
            _inventory = GetComponent<PlayerInventory>();

            if (AssetLoader.JavelinTargetIndicatorPrefab != null)
            {
                _indicator = Object.Instantiate(AssetLoader.JavelinTargetIndicatorPrefab);
                _indicator.SetActive(false);
            }
        }

        private void Update()
        {
            // Self-destruct when the Javelin is no longer equipped.
            if (
                _inventory == null
                || _bridge == null
                || _inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.JavelinItemType
            )
            {
                Destroy(this);
                return;
            }

            bool aiming = Mouse.current?.rightButton.isPressed ?? false;

            // While the rocket is in-flight, freeze the indicator at the fired position.
            if (!_bridge.IsWaitingForDetonation)
                _bridge.ClientUpdateLockOn();

            if (_indicator == null)
                return;

            // Show while right-click is held (searching for a lock), or while the
            // missile is already in-flight so the player can see the target.
            bool show = _bridge.HasLock && (aiming || _bridge.IsWaitingForDetonation);
            _indicator.SetActive(show);

            if (show)
                _indicator.transform.position = _bridge.ClientLockedTarget + Vector3.up * 0.05f;
        }

        private void OnDestroy()
        {
            if (_indicator != null)
                Object.Destroy(_indicator);
        }
    }
}
