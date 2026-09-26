using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class GrapplingHookItem
    {
        private const float MinAttachDistance = 0.75f;

        // TryUseItem can fire again while the button is still held. One click
        // attaches once; the next click is a new press.
        private static bool _firedWhileHeld;
        private static readonly RaycastHit[] AimHits = new RaycastHit[32];

        internal readonly struct AimSample
        {
            public readonly bool Hit;
            public readonly bool Valid;
            public readonly Vector3 Point;
            public readonly Vector3 Normal;

            public AimSample(bool hit, bool valid, Vector3 point, Vector3 normal)
            {
                Hit = hit;
                Valid = valid;
                Point = point;
                Normal = normal;
            }
        }

        public static void ClearFireLatch() => _firedWhileHeld = false;

        public static void OnUse(PlayerInventory inventory)
        {
            if (Mouse.current == null || !Mouse.current.leftButton.isPressed)
                return;
            if (_firedWhileHeld)
                return;

            _firedWhileHeld = true;
            // Once the rope is on, left click pays it in. A new point is a new
            // press after R lets go.
            if (GrapplingHookSession.IsAttached)
                return;

            TryFire(inventory);
        }

        internal static AimSample EvaluateAim(PlayerInventory inventory)
        {
            if (!CanFire(inventory, out PlayerMovement movement))
                return default;

            Camera cam = Camera.main;
            if (cam == null)
                return default;

            float maxRange = Mathf.Max(1f, ModConfig.GrapplingHook.MaxRange.Value);
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            // The camera sits behind the body in third person. Extend the cast by
            // that gap, then accept the hit only if the body is still in range.
            float castRange =
                maxRange
                + Mathf.Min(80f, Vector3.Distance(cam.transform.position, movement.Position));
            int hitCount = Physics.RaycastNonAlloc(
                ray,
                AimHits,
                castRange,
                GameManager.LayerSettings.PlayerGroundableMask,
                QueryTriggerInteraction.Ignore
            );

            var body = inventory.PlayerInfo.Rigidbody;
            bool found = false;
            float bestDistance = float.MaxValue;
            RaycastHit hit = default;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit candidate = AimHits[i];
                Collider col = candidate.collider;
                if (col == null)
                    continue;
                if (col.transform.IsChildOf(inventory.transform))
                    continue;
                if (body != null && col.attachedRigidbody == body)
                    continue;
                if (candidate.distance >= bestDistance)
                    continue;

                bestDistance = candidate.distance;
                hit = candidate;
                found = true;
            }

            if (!found)
                return default;

            // Range is from the body, or a third-person shot past the limit still
            // attaches locally and the server then tears it off.
            float length = Vector3.Distance(movement.Position, hit.point);
            bool valid =
                HasUse(inventory) && length >= MinAttachDistance && length <= maxRange;
            return new AimSample(true, valid, hit.point, hit.normal);
        }

        private static void TryFire(PlayerInventory inventory)
        {
            AimSample aim = EvaluateAim(inventory);
            if (!aim.Valid)
                return;

            int slot = inventory.EquippedItemIndex;
            var movement = inventory.PlayerInfo.Movement;
            float length = Vector3.Distance(movement.Position, aim.Point);
            GrapplingHookSession.Attach(aim.Point, length, movement.Velocity);
            GrapplingHookNetworkBridge.ShowLocalRope(aim.Point);
            GrapplingHookNetworkBridge.SendFire(aim.Point, slot, GrapplingHookSession.Token);
        }

        private static bool CanFire(PlayerInventory inventory, out PlayerMovement movement)
        {
            movement = inventory?.PlayerInfo?.Movement;
            if (
                movement == null
                || !movement.IsVisible
                || movement.IsKnockedOutOrRecovering
                || movement.IsRespawningOrDrowning
                || movement.DivingState != DivingState.None
            )
                return false;
            if (inventory.PlayerInfo.ActiveGolfCartSeat.IsValid())
                return false;
            if (inventory.PlayerInfo.AsHittable?.FrozenState == FrozenState.Frozen)
                return false;
            return true;
        }

        private static bool HasUse(PlayerInventory inventory)
        {
            int slot = inventory.EquippedItemIndex;
            if (slot < 0 || slot >= inventory.slots.Count)
                return false;
            var held = inventory.slots[slot];
            return held.itemType == ItemRegistry.GrapplingHookItemType && held.remainingUses > 0;
        }
    }
}
