using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class GrapplingHookItem
    {
        private static bool _holding;

        public static IEnumerator Use(PlayerInventory inventory)
        {
            if (_holding)
                yield break;

            if (Mouse.current == null || !Mouse.current.leftButton.isPressed)
                yield break;

            if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.GrapplingHookItemType)
                yield break;

            _holding = true;
            try
            {
                TryFire(inventory);
                if (!GrapplingHookSession.IsAttached)
                    yield break;

                GrapplingHookSession.IsReeling = true;
                while (
                    Mouse.current != null
                    && Mouse.current.leftButton.isPressed
                    && GrapplingHookSession.IsAttached
                )
                    yield return new WaitForFixedUpdate();
            }
            finally
            {
                GrapplingHookSession.IsReeling = false;
                _holding = false;
            }
        }

        private static void TryFire(PlayerInventory inventory)
        {
            int slot = inventory.EquippedItemIndex;
            if (slot < 0 || slot >= inventory.slots.Count)
                return;
            if (inventory.slots[slot].itemType != ItemRegistry.GrapplingHookItemType)
                return;
            if (inventory.slots[slot].remainingUses <= 0)
                return;

            var movement = inventory.PlayerInfo?.Movement;
            if (
                movement == null
                || !movement.IsVisible
                || movement.IsKnockedOutOrRecovering
                || movement.IsRespawningOrDrowning
                || movement.DivingState != DivingState.None
            )
                return;
            if (inventory.PlayerInfo.ActiveGolfCartSeat.IsValid())
                return;
            if (inventory.PlayerInfo.AsHittable?.FrozenState == FrozenState.Frozen)
                return;

            if (!TryGetAnchor(inventory, movement, out Vector3 anchor, out float length))
                return;

            Vector3 velocity = movement.Velocity;
            GrapplingHookSession.Attach(anchor, length, velocity);
            GrapplingHookNetworkBridge.ShowLocalRope(anchor);
            GrapplingHookNetworkBridge.SendFire(anchor, slot, GrapplingHookSession.Token);
        }

        private static bool TryGetAnchor(
            PlayerInventory inventory,
            PlayerMovement movement,
            out Vector3 anchor,
            out float length
        )
        {
            anchor = default;
            length = 0f;

            Camera cam = Camera.main;
            if (cam == null)
                return false;

            float maxRange = Mathf.Max(1f, ModConfig.GrapplingHook.MaxRange.Value);
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (
                !Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    maxRange,
                    GameManager.LayerSettings.PlayerGroundableMask,
                    QueryTriggerInteraction.Ignore
                )
            )
                return false;

            if (hit.collider != null && hit.collider.transform.IsChildOf(inventory.transform))
                return false;

            // The ray starts at the camera, which can sit well behind the player.
            // Range is from the body, or a third-person shot past the limit still
            // attaches locally and the server then tears it off.
            Vector3 origin = movement.Position;
            length = Vector3.Distance(origin, hit.point);
            if (length < 0.75f || length > maxRange)
                return false;

            anchor = hit.point;
            return true;
        }
    }
}
