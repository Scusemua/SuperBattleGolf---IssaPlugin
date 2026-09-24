using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Lob-arc adapter for sticky-style throws (camera origin, fixed speed, RMB aim).
    ///
    /// Owns a <see cref="BallisticTrajectoryPreview"/> and exposes the same config
    /// hooks existing item definitions already set (TargetItemType / ThrowSpeed /
    /// LobAngle / RingRadius). Self-destructs when a different item is equipped.
    /// </summary>
    public class StickyGrenadeTrajectoryPreview : MonoBehaviour
    {
        // Configurable fields — set immediately after AddComponent to override defaults.
        public ItemType TargetItemType;
        public Func<float> ThrowSpeed;
        public Func<float> LobAngle;
        public Func<float> RingRadius;

        private PlayerInventory _inventory;
        private BallisticTrajectoryPreview _preview;

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventory>();

            TargetItemType = ItemRegistry.StickyGrenadeItemType;
            ThrowSpeed = () => ModConfig.StickyGrenade.ThrowSpeed.Value;
            LobAngle = () => ModConfig.StickyGrenade.LobAngle.Value;
            RingRadius = () => 0.55f;

            _preview = gameObject.AddComponent<BallisticTrajectoryPreview>();
            _preview.ShouldKeepAlive = () =>
                _inventory != null
                && _inventory.GetEffectivelyEquippedItem(true) == TargetItemType;
            _preview.IsActive = () => Mouse.current?.rightButton.isPressed ?? false;
            _preview.GetOrigin = GetCameraThrowOrigin;
            _preview.GetVelocity = GetCameraThrowVelocity;
            _preview.RingRadius = () => RingRadius?.Invoke() ?? 0.55f;
        }

        private void Update()
        {
            if (_inventory == null || _inventory.GetEffectivelyEquippedItem(true) != TargetItemType)
                Destroy(this);
        }

        private Vector3 GetCameraThrowOrigin()
        {
            var cam = Camera.main;
            if (cam == null)
                return transform.position;
            Vector3 forward = cam.transform.forward;
            return cam.transform.position + forward * 1.2f + Vector3.up * 0.3f;
        }

        private Vector3 GetCameraThrowVelocity()
        {
            var cam = Camera.main;
            if (cam == null)
                return Vector3.zero;
            Vector3 forward = cam.transform.forward;
            float lob = LobAngle?.Invoke() ?? 0f;
            float speed = ThrowSpeed?.Invoke() ?? 0f;
            return (forward + Vector3.up * lob).normalized * speed;
        }

        private void OnDestroy()
        {
            if (_preview != null)
                Destroy(_preview);
        }
    }
}
