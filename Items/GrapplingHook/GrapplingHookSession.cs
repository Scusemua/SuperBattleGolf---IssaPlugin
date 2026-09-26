using UnityEngine;

namespace IssaPlugin.Items
{
    internal enum ReelDirection
    {
        None,
        In,
        Out,
    }

    /// <summary>
    /// Local grappling-hook simulation. One player owns this machine's swing.
    ///
    /// PlayerMovement keeps existing velocity and then adds input acceleration
    /// sized to cancel horizontal drag. Replacing the horizontal component while
    /// airborne keeps swing momentum without also inheriting that uncapped
    /// acceleration. Vertical velocity is left as the game wrote it, so gravity
    /// still runs. The rope only removes velocity aimed away from the anchor.
    /// A moving anchor's speed is included while the rope is tight. It is kept
    /// apart from the swing so the next step does not add it again.
    /// </summary>
    internal static class GrapplingHookSession
    {
        private const float TeleportDistanceSqr = 625f;

        public static bool IsAttached { get; private set; }

        /// While attached: pay rope in, pay it back out toward the length at
        /// attach, or hold still. Both mouse buttons at once hold still.
        public static ReelDirection Reel { get; set; }

        public static bool OwnsSimulation => IsAttached || _isLaunching;
        public static int Token { get; private set; }

        private static bool _isLaunching;
        private static Vector3 _anchor;

        private static float _ropeLength;
        private static float _ropeLimit;
        private static Vector3 _savedVelocity;
        private static float _launchUntil;
        private static Vector3 _lastPosition;
        private static bool _hasLastPosition;

        // Tight rope: saved velocity is the swing, and _anchorCarry is the
        // anchor speed added on top. Slack folds that speed into the swing and
        // clears both. _pendingCarry stays set until the first tight step, so
        // that step can pick up a moving anchor without treating the speed you
        // already had as the anchor's.
        private static bool _hasPrevAnchor;
        private static Vector3 _prevAnchor;
        private static bool _savedIsRelative;
        private static bool _pendingCarry;
        private static Vector3 _anchorCarry;

        private static bool _hasUndo;

        public static void Attach(Vector3 anchor, float length, Vector3 velocity)
        {
            _hasUndo = true;

            Token++;
            IsAttached = true;
            _isLaunching = false;
            Reel = ReelDirection.None;
            _anchor = anchor;
            _prevAnchor = anchor;
            _hasPrevAnchor = true;
            _ropeLength = Mathf.Max(0.5f, length);
            _ropeLimit = _ropeLength;
            _savedVelocity = velocity;
            _savedIsRelative = false;
            _pendingCarry = true;
            _anchorCarry = Vector3.zero;
            // A teleport between swings must not look like a discontinuity on
            // the first step of the new rope.
            _hasLastPosition = false;
        }

        public static void Confirm(int token)
        {
            if (token == Token)
                _hasUndo = false;
        }

        public static void Reject(int token)
        {
            if (token != Token || !_hasUndo)
                return;

            _hasUndo = false;
            StopLocal();
            GrapplingHookNetworkBridge.HideLocalRope();
        }

        /// Detach and keep the current swing velocity for the release grace period.
        public static void Release()
        {
            if (!IsAttached)
                return;

            // Saved speed is relative to the anchor while the rope is tight.
            // Put the anchor's speed back before the launch grace reads it.
            if (_savedIsRelative)
            {
                _savedVelocity += _anchorCarry;
                _savedIsRelative = false;
            }

            _anchorCarry = Vector3.zero;
            _pendingCarry = false;
            IsAttached = false;
            Reel = ReelDirection.None;
            _isLaunching = true;
            _hasUndo = false;
            _launchUntil = Time.time + Mathf.Max(0f, ModConfig.GrapplingHook.ReleaseGrace.Value);
            GrapplingHookNetworkBridge.ReleaseLocalRope();
        }

        /// Drop the rope without preserving swing velocity. Used for knockout,
        /// teleport, cart, and hole cleanup.
        public static void ForceStop(bool notifyServer)
        {
            if (!IsAttached && !_isLaunching)
                return;

            // A release already told the server. Knockout during the launch
            // grace period must not send that message again.
            bool tellServer = notifyServer && IsAttached;
            StopLocal();
            if (tellServer)
                GrapplingHookNetworkBridge.ReleaseLocalRope();
            else
                GrapplingHookNetworkBridge.HideLocalRope();
        }

        public static bool TryStep(
            PlayerMovement movement,
            Vector3 gameVelocity,
            Vector3 wish,
            out Vector3 velocity,
            out Vector3 correctedPosition,
            out bool correctPosition
        )
        {
            velocity = gameVelocity;
            correctedPosition = movement.Position;
            correctPosition = false;

            if (
                !movement.IsVisible
                || movement.IsKnockedOutOrRecovering
                || movement.IsRespawningOrDrowning
                || movement.DivingState != DivingState.None
            )
            {
                ForceStop(true);
                return false;
            }

            var info = movement.PlayerInfo;
            if (info != null && info.ActiveGolfCartSeat.IsValid())
            {
                ForceStop(true);
                return false;
            }

            if (info?.AsHittable != null && info.AsHittable.FrozenState == FrozenState.Frozen)
            {
                ForceStop(true);
                return false;
            }

            Vector3 position = movement.Position;
            if (_hasLastPosition && (position - _lastPosition).sqrMagnitude > TeleportDistanceSqr)
            {
                _lastPosition = position;
                ForceStop(true);
                return false;
            }

            if (_isLaunching)
            {
                if (movement.IsGrounded || Time.time >= _launchUntil)
                {
                    _isLaunching = false;
                    _hasLastPosition = false;
                    return false;
                }

                velocity = new Vector3(_savedVelocity.x, gameVelocity.y, _savedVelocity.z);
                _savedVelocity = velocity;
                _lastPosition = position;
                _hasLastPosition = true;
                return true;
            }

            if (!IsAttached)
                return false;

            if (!PrepareAnchor(out Vector3 anchorVelocity))
                return false;

            float dt = Time.fixedDeltaTime;
            float reel = Mathf.Max(0f, ModConfig.GrapplingHook.ReelSpeed.Value) * dt;
            if (Reel == ReelDirection.In)
            {
                float minLength = Mathf.Max(0.5f, ModConfig.GrapplingHook.MinLength.Value);
                if (_ropeLength > minLength)
                    _ropeLength = Mathf.Max(minLength, _ropeLength - reel);
            }
            else if (Reel == ReelDirection.Out && _ropeLength < _ropeLimit)
            {
                _ropeLength = Mathf.Min(_ropeLimit, _ropeLength + reel);
            }

            Vector3 fromAnchor = position - _anchor;
            float distance = fromAnchor.magnitude;
            Vector3 outward = distance > 0.001f ? fromAnchor / distance : Vector3.up;
            bool taut = distance > _ropeLength && distance > 0.001f;
            bool airborne = !movement.IsGrounded;

            // Slack already contains the anchor's speed. Take it back out once
            // before this tight step adds the anchor's current speed.
            bool foldAnchor = airborne && taut && !_savedIsRelative && !_pendingCarry;
            Vector3 swing = _savedVelocity;
            if (foldAnchor)
                swing -= anchorVelocity;

            Vector3 horizontal = airborne
                ? new Vector3(swing.x, 0f, swing.z)
                : new Vector3(gameVelocity.x, 0f, gameVelocity.z);

            if (airborne && wish.sqrMagnitude > 0.01f)
            {
                Vector3 planeNormal = distance > 0.5f ? outward : Vector3.up;
                Vector3 tangent = Vector3.ProjectOnPlane(wish, planeNormal);
                if (tangent.sqrMagnitude > 0.0001f)
                {
                    float steer = Mathf.Max(0f, ModConfig.GrapplingHook.SteerAcceleration.Value);
                    float input = Mathf.Clamp01(wish.magnitude);
                    horizontal += tangent.normalized * steer * input * dt;
                }
            }

            // Gravity is already in the game's vertical speed. While the rope is
            // tight that number also still holds the anchor's vertical speed from
            // the previous step, so take that part off before adding the current one.
            float vertical = gameVelocity.y;
            if (airborne && taut && _savedIsRelative)
                vertical -= _anchorCarry.y;
            else if (foldAnchor)
                vertical -= anchorVelocity.y;

            velocity = new Vector3(horizontal.x, vertical, horizontal.z);

            if (taut)
            {
                correctedPosition = _anchor + outward * _ropeLength;
                correctPosition = (correctedPosition - position).sqrMagnitude > 0.000001f;
                float outwardSpeed = Vector3.Dot(velocity, outward);
                if (outwardSpeed > 0f)
                    velocity -= outward * outwardSpeed;

                // On the ground the game owns walking. Adding the anchor here
                // would stack on top of the speed written last step.
                if (airborne)
                {
                    velocity += anchorVelocity;
                    _savedVelocity = velocity - anchorVelocity;
                    _savedIsRelative = true;
                    _anchorCarry = anchorVelocity;
                    _pendingCarry = false;
                }
                else
                {
                    _savedVelocity = velocity;
                    _savedIsRelative = false;
                    _anchorCarry = Vector3.zero;
                }
            }
            else if (!airborne)
            {
                _savedVelocity = gameVelocity;
                _savedIsRelative = false;
                _anchorCarry = Vector3.zero;
                _lastPosition = position;
                _hasLastPosition = true;
                return false;
            }
            else if (_savedIsRelative)
            {
                velocity.x += _anchorCarry.x;
                velocity.z += _anchorCarry.z;
                _savedVelocity = velocity;
                _savedIsRelative = false;
                _anchorCarry = Vector3.zero;
            }
            else
            {
                _savedVelocity = velocity;
            }

            _lastPosition = correctPosition ? correctedPosition : position;
            _hasLastPosition = true;
            return true;
        }

        // Resolve the anchor, then keep its speed for this step. A missing
        // target or a target that jumped lets go and keeps the swing.
        private static bool PrepareAnchor(out Vector3 anchorVelocity)
        {
            anchorVelocity = Vector3.zero;
            if (
                !GrapplingHookNetworkBridge.TryReadLocalAnchor(out Vector3 world)
                || !IsFinite(world)
            )
            {
                Release();
                return false;
            }

            if (_hasPrevAnchor)
            {
                Vector3 delta = world - _prevAnchor;
                if (delta.sqrMagnitude > TeleportDistanceSqr)
                {
                    Release();
                    return false;
                }

                float step = Time.fixedDeltaTime;
                if (step > 0f)
                    anchorVelocity = delta / step;
            }

            _prevAnchor = world;
            _hasPrevAnchor = true;
            _anchor = world;
            return true;
        }

        private static void StopLocal()
        {
            IsAttached = false;
            _isLaunching = false;
            Reel = ReelDirection.None;
            _hasUndo = false;
            _hasLastPosition = false;
            _hasPrevAnchor = false;
            _savedIsRelative = false;
            _pendingCarry = false;
            _anchorCarry = Vector3.zero;
        }

        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
