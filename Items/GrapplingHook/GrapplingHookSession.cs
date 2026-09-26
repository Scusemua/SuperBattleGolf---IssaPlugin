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
    /// </summary>
    internal static class GrapplingHookSession
    {
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

        private static bool _hasUndo;
        private static bool _undoAttached;
        private static int _undoToken;
        private static Vector3 _undoAnchor;
        private static float _undoLength;
        private static float _undoRopeLimit;
        private static Vector3 _undoVelocity;

        public static void Attach(Vector3 anchor, float length, Vector3 velocity)
        {
            _undoAttached = IsAttached;
            _undoToken = Token;
            _undoAnchor = _anchor;
            _undoLength = _ropeLength;
            _undoRopeLimit = _ropeLimit;
            _undoVelocity = _savedVelocity;
            _hasUndo = true;

            Token++;
            IsAttached = true;
            _isLaunching = false;
            Reel = ReelDirection.None;
            _anchor = anchor;
            _ropeLength = Mathf.Max(0.5f, length);
            _ropeLimit = _ropeLength;
            _savedVelocity = velocity;
            // A teleport between swings must not look like a discontinuity on the
            // first step of the new rope.
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
            if (_undoAttached)
            {
                IsAttached = true;
                _isLaunching = false;
                _anchor = _undoAnchor;
                _ropeLength = _undoLength;
                _ropeLimit = _undoRopeLimit;
                _savedVelocity = _undoVelocity;
                GrapplingHookNetworkBridge.ShowLocalRope(_anchor, _undoToken);
                return;
            }

            StopLocal();
            GrapplingHookNetworkBridge.HideLocalRope();
        }

        /// Detach and keep the current swing velocity for the release grace period.
        public static void Release()
        {
            if (!IsAttached)
                return;

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
            if (_hasLastPosition && (position - _lastPosition).sqrMagnitude > 625f)
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

            bool airborne = !movement.IsGrounded;
            Vector3 horizontal = airborne
                ? new Vector3(_savedVelocity.x, 0f, _savedVelocity.z)
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

            velocity = new Vector3(horizontal.x, gameVelocity.y, horizontal.z);

            if (distance > _ropeLength && distance > 0.001f)
            {
                correctedPosition = _anchor + outward * _ropeLength;
                correctPosition = (correctedPosition - position).sqrMagnitude > 0.000001f;
                float outwardSpeed = Vector3.Dot(velocity, outward);
                if (outwardSpeed > 0f)
                    velocity -= outward * outwardSpeed;
            }
            else if (!airborne)
            {
                _savedVelocity = gameVelocity;
                _lastPosition = position;
                _hasLastPosition = true;
                return false;
            }

            _savedVelocity = velocity;
            _lastPosition = correctPosition ? correctedPosition : position;
            _hasLastPosition = true;
            return true;
        }

        private static void StopLocal()
        {
            IsAttached = false;
            _isLaunching = false;
            Reel = ReelDirection.None;
            _hasUndo = false;
            _hasLastPosition = false;
        }
    }
}
