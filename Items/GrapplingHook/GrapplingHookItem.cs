using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public static class GrapplingHookItem
    {
        private const float MinAttachDistance = 0.75f;
        internal const float MaxLocalOffset = 100f;

        // TryUseItem can fire again while the button is still held. One click
        // attaches once; the next click is a new press.
        private static bool _firedWhileHeld;

        // The combined ground and hittable masks can fill a short buffer and
        // drop the closest surface.
        private static readonly RaycastHit[] AimHits = new RaycastHit[64];

        internal readonly struct AimSample
        {
            public readonly bool Hit;
            public readonly bool Valid;
            public readonly Vector3 Point;
            public readonly Vector3 Normal;
            public readonly uint TargetNetId;
            public readonly Vector3 LocalPoint;

            public AimSample(
                bool hit,
                bool valid,
                Vector3 point,
                Vector3 normal,
                uint targetNetId,
                Vector3 localPoint
            )
            {
                Hit = hit;
                Valid = valid;
                Point = point;
                Normal = normal;
                TargetNetId = targetNetId;
                LocalPoint = localPoint;
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
            int groundMask = GameManager.LayerSettings.PlayerGroundableMask;
            int hitCount = Physics.RaycastNonAlloc(
                ray,
                AimHits,
                castRange,
                groundMask | GameManager.LayerSettings.GunHittablesMask,
                QueryTriggerInteraction.Ignore
            );

            var body = inventory.PlayerInfo.Rigidbody;
            uint selfId = inventory.GetComponentInParent<NetworkIdentity>()?.netId ?? 0u;
            bool found = false;
            float bestDistance = float.MaxValue;
            RaycastHit hit = default;
            uint bestId = 0;
            Vector3 bestLocal = Vector3.zero;
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

                var hitPlayer = col.GetComponentInParent<PlayerInfo>();
                if (hitPlayer != null && hitPlayer == inventory.PlayerInfo)
                    continue;

                var identity = col.GetComponentInParent<NetworkIdentity>();
                uint targetId = identity != null ? identity.netId : 0u;
                if (targetId != 0 && targetId == selfId)
                    continue;

                Vector3 localPoint = Vector3.zero;
                if (targetId != 0)
                {
                    localPoint = identity.transform.InverseTransformPoint(candidate.point);
                    // Skip a point this client cannot follow, and keep looking down the ray.
                    if (
                        !IsFinite(localPoint)
                        || localPoint.sqrMagnitude > MaxLocalOffset * MaxLocalOffset
                        || !NetworkClient.active
                        || !NetworkClient.spawned.ContainsKey(targetId)
                    )
                        continue;
                }
                else if ((groundMask & (1 << col.gameObject.layer)) == 0)
                    continue;

                if (candidate.distance >= bestDistance)
                    continue;

                bestDistance = candidate.distance;
                hit = candidate;
                bestId = targetId;
                bestLocal = localPoint;
                found = true;
            }

            if (!found)
                return default;

            // Range is from the body, or a third-person shot past the limit still
            // attaches locally and the server then tears it off.
            float length = Vector3.Distance(movement.Position, hit.point);
            bool valid =
                HasUse(inventory) && length >= MinAttachDistance && length <= maxRange;
            return new AimSample(true, valid, hit.point, hit.normal, bestId, bestLocal);
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
            GrapplingHookNetworkBridge.ShowLocalRope(aim.Point, aim.TargetNetId, aim.LocalPoint);
            GrapplingHookNetworkBridge.SendFire(
                aim.Point,
                slot,
                GrapplingHookSession.Token,
                aim.TargetNetId,
                aim.LocalPoint
            );
        }

        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

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
