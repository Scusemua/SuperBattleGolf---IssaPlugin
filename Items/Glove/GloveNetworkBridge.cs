using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Network;
using IssaPlugin.Overlays;
using IssaPlugin.Patches;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Per-player Glove session. Server owns pickup / timeout / release decisions.
    /// While held, the simulating peer (and visual peers) snap the ball to the glove
    /// world pose each LateUpdate using a kinematic Rigidbody — no Transform parenting.
    /// </summary>
    public class GloveNetworkBridge : NetworkBridgeBase
    {
        private static readonly FieldInfo IsInHoleField = AccessTools.Field(
            typeof(GolfBall),
            "isInHole"
        );

        private static readonly PropertyInfo ServerLastStrokePositionProp = AccessTools.Property(
            typeof(GolfBall),
            "ServerLastStrokePosition"
        );

        private static readonly MethodInfo OnPlayerHitOwnBallMethod = AccessTools.Method(
            typeof(PlayerGolfer),
            "OnPlayerHitOwnBall"
        );

        private static readonly Dictionary<uint, ActiveHold> ServerActiveHolds = new();

        private struct ActiveHold
        {
            public uint SessionId;
            public float EndTime;
            public float Duration;
        }

        // ── Server session ────────────────────────────────────────────────
        private bool _serverHolding;
        private uint _serverSessionId;
        private float _serverEndTime;
        private float _serverDuration;
        private uint _nextSessionId = 1;
        /// <summary>Inventory slot that holds the Glove for this session (consumed on release).</summary>
        private int _wielderSlot = -1;

        private bool _savedDetectCollisions;
        private bool _hasPhysicsSnapshot;
        private readonly List<(Collider a, Collider b)> _ignoredPairs = new();
        private readonly List<(Collider col, bool wasEnabled)> _disabledBallColliders = new();

        // ── Replicated hold flag (all peers) ──────────────────────────────
        /// <summary>True while this player is carrying their ball (server + clients).</summary>
        public bool IsHolding { get; private set; }

        public uint CurrentSessionId { get; private set; }
        public float HoldDuration { get; private set; }
        public float HoldStartTime { get; private set; }

        // ── Local charge (owner client only) ──────────────────────────────
        public bool IsCharging { get; private set; }
        public float Charge01 { get; private set; }
        private float _chargeStartTime;

        /// <summary>
        /// False until LMB has been released at least once after hold start.
        /// Prevents the pickup click (or a held LMB) from immediately charging/throwing.
        /// </summary>
        private bool _throwInputArmed;

        // ================================================================
        //  Client — pickup / charge / throw input
        // ================================================================

        public void ClientRequestPickup()
        {
            if (!isOwned || IsHolding)
                return;

            var inventory = CachedInventory;
            if (inventory == null)
                return;

            int slot = inventory.EquippedItemIndex;
            if (slot < 0)
                return;

            NetworkClient.Send(new GlovePickupRequestMessage { EquippedSlotIndex = slot });
        }

        private void Update()
        {
            if (!isOwned || !IsHolding)
            {
                if (IsCharging)
                    CancelLocalCharge();
                return;
            }

            TickThrowInput();
        }

        private void TickThrowInput()
        {
            var input = GetComponent<PlayerInfo>()?.Input;
            // Reboundable aim / charge bindings (same sources as golf swing + custom guns).
            bool aiming = input?.IsHoldingAimSwing ?? false;
            bool fireHeld = input?.IsHoldingChargeSwing ?? false;

            if (!aiming)
            {
                if (IsCharging)
                    CancelLocalCharge();
                return;
            }

            // Wait for LMB release after pickup before charge/throw can begin.
            if (!fireHeld)
            {
                if (IsCharging)
                {
                    float charge = Charge01;
                    CancelLocalCharge();

                    var cam = Camera.main;
                    Vector3 aim = cam != null ? cam.transform.forward : transform.forward;

                    NetworkClient.Send(
                        new GloveThrowRequestMessage
                        {
                            SessionId = CurrentSessionId,
                            AimDirection = aim,
                            Charge01 = charge,
                        }
                    );
                }

                _throwInputArmed = true;
                return;
            }

            if (!_throwInputArmed)
                return;

            if (!IsCharging)
            {
                IsCharging = true;
                _chargeStartTime = Time.time;
                Charge01 = 0f;
            }

            float duration = Mathf.Max(0.05f, ModConfig.Glove.ChargeDuration.Value);
            Charge01 = Mathf.Clamp01((Time.time - _chargeStartTime) / duration);
        }

        private void CancelLocalCharge()
        {
            IsCharging = false;
            Charge01 = 0f;
        }

        private void FixedUpdate()
        {
            if (_serverHolding && NetworkServer.active)
                ServerTickHold();
        }

        private void LateUpdate()
        {
            if (IsHolding)
                TickAttachBall();
        }

        // ================================================================
        //  Server — pickup
        // ================================================================

        public void ServerHandlePickupRequest(int equippedSlotIndex)
        {
            if (!NetworkServer.active || _serverHolding)
                return;

            var inventory = CachedInventory;
            var info = GetComponent<PlayerInfo>();
            var movement = info?.Movement;

            if (inventory == null || info == null || movement == null)
                return;

            if (
                ItemRegistry.GetItemTypeAtSlot(inventory, equippedSlotIndex)
                != ItemRegistry.GloveItemType
            )
            {
                IssaPluginPlugin.Log.LogWarning("[Glove] Pickup rejected: Glove not in slot.");
                return;
            }

            if (!ServerCanBeginHold(info, movement, out var ball, out string rejectReason))
            {
                IssaPluginPlugin.Log.LogDebug($"[Glove] Pickup rejected: {rejectReason}");
                return;
            }

            // Keep the Glove equipped for the hold; consume on throw / timeout / KO / unequip.
            _wielderSlot = equippedSlotIndex;

            _serverSessionId = _nextSessionId++;
            if (_nextSessionId == 0)
                _nextSessionId = 1;

            _serverDuration = Mathf.Max(0.1f, ModConfig.Glove.HoldDuration.Value);
            _serverEndTime = Time.time + _serverDuration;
            _serverHolding = true;

            ServerActiveHolds[netId] = new ActiveHold
            {
                SessionId = _serverSessionId,
                EndTime = _serverEndTime,
                Duration = _serverDuration,
            };

            // Apply on the server peer first so dedicated servers (no local client
            // handler) still capture the ball when they simulate it. Listen-host
            // SendToAll will also invoke HandleHoldStarted, which no-ops capture
            // when the session is already active.
            ApplyClientHoldState(_serverSessionId, _serverDuration, _serverDuration);
            if (ball.AsEntity == null || ball.AsEntity.IsSimulatingRigidbody())
                CaptureAndPrepareBall(ball, info);

            NetworkServer.SendToAll(
                new GloveHoldStartedMessage
                {
                    HolderNetId = netId,
                    SessionId = _serverSessionId,
                    Duration = _serverDuration,
                    TimeRemaining = _serverDuration,
                }
            );

            ItemWarningBroadcaster.Broadcast(
                info.PlayerId?.PlayerName ?? "Player",
                ItemRegistry.GloveItemType,
                "Glove",
                trackedNetId: netId,
                senderNetId: netId
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[Glove] Hold started session={_serverSessionId} for {_serverDuration:F1}s."
            );
        }

        private bool ServerCanBeginHold(
            PlayerInfo info,
            PlayerMovement movement,
            out GolfBall ball,
            out string reason
        )
        {
            ball = null;
            reason = null;

            if (!ServerHolderAllowed(info, movement, out reason, requireOnFoot: true))
                return false;

            ball = info.AsGolfer?.OwnBall;
            if (ball == null)
            {
                reason = "no OwnBall";
                return false;
            }

            if (!ServerBallAllowed(ball, out reason))
                return false;

            float radius = ModConfig.Glove.PickupRadius.Value;
            if ((ball.transform.position - transform.position).sqrMagnitude > radius * radius)
            {
                reason = "out of pickup radius";
                return false;
            }

            return true;
        }

        private static bool ServerHolderAllowed(
            PlayerInfo info,
            PlayerMovement movement,
            out string reason,
            bool requireOnFoot
        )
        {
            reason = null;

            if (movement == null)
            {
                reason = "no movement";
                return false;
            }

            if (movement.IsKnockedOutOrRecovering)
            {
                reason = "knocked out";
                return false;
            }

            if (movement.IsRespawningOrDrowning)
            {
                reason = "respawning/drowning";
                return false;
            }

            if (info.AsHittable != null && info.AsHittable.FrozenState == FrozenState.Frozen)
            {
                reason = "frozen";
                return false;
            }

            if (requireOnFoot)
            {
                if (info.ActiveGolfCartSeat.IsValid())
                {
                    reason = "in golf cart";
                    return false;
                }

                if (!movement.IsGrounded)
                {
                    reason = "not grounded";
                    return false;
                }
            }

            return true;
        }

        private static bool ServerBallAllowed(GolfBall ball, out string reason)
        {
            reason = null;

            if (ball.IsHidden)
            {
                reason = "ball hidden";
                return false;
            }

            if (ball.OutOfBoundsReturnState != BallOutOfBoundsReturnState.None)
            {
                reason = "ball returning from OOB";
                return false;
            }

            if (IsInHoleField != null && (bool)IsInHoleField.GetValue(ball))
            {
                reason = "ball in hole";
                return false;
            }

            return true;
        }

        // ================================================================
        //  Server — throw / tick / release
        // ================================================================

        public void ServerHandleThrowRequest(uint sessionId, Vector3 aimDirection, float charge01)
        {
            if (!NetworkServer.active || !_serverHolding)
                return;
            if (sessionId != _serverSessionId)
                return;
            if (!IsFinite(aimDirection) || aimDirection.sqrMagnitude < 0.0001f)
                return;

            // Never trust client charge / NaNs — Clamp01 also maps NaN → 0.
            charge01 = Mathf.Clamp01(charge01);

            var info = GetComponent<PlayerInfo>();
            var movement = info?.Movement;
            // Throws are allowed in a golf cart / mid-air (e.g. after a teleport);
            // only KO / drown / freeze still block.
            if (!ServerHolderAllowed(info, movement, out _, requireOnFoot: false))
                return;

            var ball = info?.AsGolfer?.OwnBall;
            if (ball == null || !ServerBallAllowed(ball, out _))
            {
                ServerRelease(GloveReleaseReason.Cleanup, Vector3.zero);
                return;
            }

            float spinachMult = GloveThrowMath.GetSpinachThrowSpeedMultiplier(
                GetComponent<SpinachNetworkBridge>()?.ServerIsBuffActive == true
            );
            float minSpeed = ModConfig.Glove.MinimumThrowSpeed.Value * spinachMult;
            float maxSpeed = ModConfig.Glove.MaximumThrowSpeed.Value * spinachMult;
            Vector3 velocity = GloveThrowMath.ComputeThrowVelocity(
                aimDirection,
                charge01,
                minSpeed,
                maxSpeed,
                ModConfig.Glove.ThrowUpwardBias.Value
            );

            velocity = Vector3.ClampMagnitude(velocity, Mathf.Max(maxSpeed, 0.01f));
            ServerRelease(GloveReleaseReason.Throw, velocity, spinachMult);
        }

        private void ServerTickHold()
        {
            var info = GetComponent<PlayerInfo>();
            var movement = info?.Movement;
            var ball = info?.AsGolfer?.OwnBall;
            var inventory = CachedInventory;

            if (ball == null)
            {
                ServerRelease(GloveReleaseReason.Cleanup, Vector3.zero);
                return;
            }

            // Switched away from the Glove or dropped it — drop the ball and consume.
            if (!ServerStillWieldingGlove(inventory))
            {
                ServerRelease(GloveReleaseReason.Interrupt, Vector3.zero);
                return;
            }

            if (
                movement != null
                && (movement.IsKnockedOutOrRecovering || movement.IsRespawningOrDrowning)
            )
            {
                Vector3 eject = GloveThrowMath.ComputeKnockoutEjectVelocity(
                    ModConfig.Glove.KnockoutEjectSpeed.Value,
                    GloveThrowMath.KnockoutUpwardBias
                );
                ServerRelease(GloveReleaseReason.Knockout, eject);
                return;
            }

            if (
                info?.AsHittable != null
                && info.AsHittable.FrozenState == FrozenState.Frozen
            )
            {
                ServerRelease(GloveReleaseReason.Interrupt, Vector3.zero);
                return;
            }

            // Golf carts are allowed while holding — the ball keeps following the
            // player (seat) until timeout / throw / KO.

            if (!ServerBallAllowed(ball, out _))
            {
                ServerRelease(GloveReleaseReason.Interrupt, Vector3.zero);
                return;
            }

            if (Time.time >= _serverEndTime)
                ServerRelease(GloveReleaseReason.Timeout, Vector3.zero);
        }

        private bool ServerStillWieldingGlove(PlayerInventory inventory)
        {
            if (inventory == null || _wielderSlot < 0)
                return false;

            if (
                ItemRegistry.GetItemTypeAtSlot(inventory, _wielderSlot)
                != ItemRegistry.GloveItemType
            )
                return false;

            // Prefer networked equipped index for remote clients on the server.
            int equipped =
                (!inventory.isLocalPlayer && NetworkServer.active)
                    ? inventory.PlayerInfo.NetworkedEquippedItemIndex
                    : inventory.EquippedItemIndex;

            return equipped == _wielderSlot;
        }

        private void ServerRelease(
            GloveReleaseReason reason,
            Vector3 velocity,
            float powerMultiplier = 1f
        )
        {
            if (!_serverHolding)
                return;

            uint sessionId = _serverSessionId;
            _serverHolding = false;
            ServerActiveHolds.Remove(netId);

            var info = GetComponent<PlayerInfo>();
            var ball = info?.AsGolfer?.OwnBall;

            Vector3 releasePos = transform.position;
            bool snapToFeet =
                reason == GloveReleaseReason.Timeout
                || reason == GloveReleaseReason.Interrupt
                || reason == GloveReleaseReason.Cleanup;
            if (snapToFeet)
            {
                releasePos = GetFeetDropPosition();
                velocity = Vector3.zero;
                powerMultiplier = 1f;
            }
            else if (ball != null)
            {
                releasePos = ball.transform.position;
            }

            // Clear IsHolding before restore so TickAttachBall cannot overwrite launch.
            ApplyClientReleaseState(sessionId);

            // Consume the Glove now that the hold ends. Stroke counting skips hole
            // Cleanup so ending a hole mid-hold does not inflate the scorecard.
            ServerConsumeGloveIfPresent();
            if (reason != GloveReleaseReason.Cleanup)
                ServerRegisterGloveStroke(info, ball, releasePos);

            if (
                ball != null
                && (ball.AsEntity == null || ball.AsEntity.IsSimulatingRigidbody())
            )
            {
                RestoreAndLaunchBall(ball, releasePos, velocity, snapToFeet);
                MaybeBeginSpinachFlight(ball, reason, powerMultiplier);
            }
            else
                // Still undo ignore/collider state if we captured on this peer.
                RestoreCollisionStateOnly();

            NetworkServer.SendToAll(
                new GloveReleasedMessage
                {
                    HolderNetId = netId,
                    SessionId = sessionId,
                    Reason = reason,
                    WorldPosition = releasePos,
                    Velocity = velocity,
                    PowerMultiplier = powerMultiplier,
                }
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[Glove] Released session={sessionId} reason={reason} speed={velocity.magnitude:F1} power×{powerMultiplier:F2}."
            );
        }

        private void MaybeBeginSpinachFlight(
            GolfBall ball,
            GloveReleaseReason reason,
            float powerMultiplier
        )
        {
            if (reason != GloveReleaseReason.Throw || powerMultiplier <= 1.01f || ball == null)
                return;
            var rb = ball.Rigidbody ?? ball.AsEntity?.Rigidbody;
            HitWithGolfSwingInternalPatch.BeginBoostedFlight(this, rb, powerMultiplier);
        }

        /// <summary>
        /// Counts one stroke for ending a Glove hold via the same
        /// <see cref="PlayerGolfer.PlayerHitOwnBall"/> path as a real club hit
        /// (CourseManager.OnServerPlayerHitOwnBall), and stamps last-stroke position
        /// at the release point for chip-in / scoring helpers.
        /// </summary>
        private static void ServerRegisterGloveStroke(
            PlayerInfo info,
            GolfBall ball,
            Vector3 releasePos
        )
        {
            if (!NetworkServer.active)
                return;

            var golfer = info?.AsGolfer;
            if (golfer != null && OnPlayerHitOwnBallMethod != null)
                OnPlayerHitOwnBallMethod.Invoke(golfer, null);

            if (ball != null && ServerLastStrokePositionProp != null)
                ServerLastStrokePositionProp.SetValue(ball, releasePos);
        }

        private void ServerConsumeGloveIfPresent()
        {
            int slot = _wielderSlot;
            _wielderSlot = -1;
            if (slot < 0)
                return;

            var inventory = CachedInventory;
            if (
                inventory != null
                && ItemRegistry.GetItemTypeAtSlot(inventory, slot) == ItemRegistry.GloveItemType
            )
                ItemHelper.ConsumeItemAtSlot(inventory, slot);
        }

        private Vector3 GetFeetDropPosition()
        {
            Vector3 pos = transform.position;
            // SampleTerrainY adds a 0.5 marker offset; undo it and sit just above ground.
            float sampled = ItemHelper.SampleTerrainY(pos.x, pos.z, pos.y);
            float groundY = sampled - 0.5f + 0.08f;
            return new Vector3(pos.x, groundY, pos.z);
        }

        // ================================================================
        //  Ball attach / physics snapshot
        // ================================================================

        private void CaptureAndPrepareBall(GolfBall ball, PlayerInfo holder)
        {
            // Re-entrancy / duplicate HoldStarted: restore previous capture first.
            if (_hasPhysicsSnapshot || _ignoredPairs.Count > 0 || _disabledBallColliders.Count > 0)
                RestoreCollisionStateOnly();

            var rb = ball.Rigidbody ?? ball.AsEntity?.Rigidbody;
            if (rb != null)
            {
                _savedDetectCollisions = rb.detectCollisions;
                _hasPhysicsSnapshot = true;
                // Kinematic hold — position is written in TickAttachBall. Do not write
                // velocities while kinematic (Unity warns and ignores them).
                rb.isKinematic = true;
                rb.detectCollisions = true;
            }
            else
            {
                _hasPhysicsSnapshot = false;
            }

            DisableBallHittability(ball);
            IgnoreCollisionsWithHolder(ball, holder, ignore: true);
            TickAttachBall();
        }

        private void RestoreAndLaunchBall(
            GolfBall ball,
            Vector3 worldPosition,
            Vector3 velocity,
            bool snapToFeet
        )
        {
            IgnoreCollisionsWithHolder(ball, GetComponent<PlayerInfo>(), ignore: false);
            RestoreBallHittability();

            var rb = ball.Rigidbody ?? ball.AsEntity?.Rigidbody;
            if (rb == null)
            {
                _hasPhysicsSnapshot = false;
                return;
            }

            if (snapToFeet || velocity.sqrMagnitude < 0.0001f)
            {
                rb.position = worldPosition;
                ball.transform.position = worldPosition;
            }

            _hasPhysicsSnapshot = false;

            // Non-kinematic before writing velocities (avoids Unity kinematic warnings).
            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.linearVelocity = velocity;
            rb.angularVelocity = GloveThrowMath.ComputeThrowAngularVelocity(velocity);
        }

        /// <summary>
        /// Restores ignore-pairs and collider enables without moving the ball.
        /// Used when this peer is not the simulator, or on hole cleanup.
        /// </summary>
        private void RestoreCollisionStateOnly()
        {
            var ball = GetComponent<PlayerInfo>()?.AsGolfer?.OwnBall;
            IgnoreCollisionsWithHolder(ball, GetComponent<PlayerInfo>(), ignore: false);
            RestoreBallHittability();

            var rb = ball?.Rigidbody ?? ball?.AsEntity?.Rigidbody;
            if (rb != null)
            {
                rb.isKinematic = false;
                if (_hasPhysicsSnapshot)
                    rb.detectCollisions = _savedDetectCollisions;
                else
                    rb.detectCollisions = true;
            }

            _hasPhysicsSnapshot = false;
        }

        /// <summary>
        /// Snaps the ball to the glove / hand world pose. Simulating peers drive the
        /// kinematic Rigidbody; others update the Transform for local visuals.
        /// </summary>
        private void TickAttachBall()
        {
            var info = GetComponent<PlayerInfo>();
            var ball = info?.AsGolfer?.OwnBall;
            if (ball == null || info == null)
                return;

            Vector3 target = GloveThrowMath.GetHeldWorldPosition(info, TryGetGloveModel(info));
            var entity = ball.AsEntity;
            var rb = ball.Rigidbody ?? entity?.Rigidbody;
            bool simulating = entity == null || entity.IsSimulatingRigidbody();

            if (simulating && rb != null)
            {
                if (!rb.isKinematic)
                    rb.isKinematic = true;
                // Position/rotation only — never write velocities on a kinematic body.
                rb.position = target;
                rb.rotation = Quaternion.identity;
                ball.transform.SetPositionAndRotation(target, Quaternion.identity);
            }
            else
            {
                ball.transform.SetPositionAndRotation(target, Quaternion.identity);
            }
        }

        private Transform TryGetGloveModel(PlayerInfo holder)
        {
            var inventory = holder?.Inventory ?? CachedInventory;
            if (
                inventory != null
                && LocalPlayerUpdateEquipmentSwitchers.TryGetCustomHeldModel(
                    inventory,
                    out var modelTf
                )
            )
                return modelTf;
            return null;
        }

        private void DisableBallHittability(GolfBall ball)
        {
            _disabledBallColliders.Clear();
            if (ball == null)
                return;

            foreach (var col in ball.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || col.isTrigger)
                    continue;
                _disabledBallColliders.Add((col, col.enabled));
                col.enabled = false;
            }
        }

        private void RestoreBallHittability()
        {
            foreach (var (col, wasEnabled) in _disabledBallColliders)
            {
                if (col != null)
                    col.enabled = wasEnabled;
            }
            _disabledBallColliders.Clear();
        }

        private void IgnoreCollisionsWithHolder(GolfBall ball, PlayerInfo holder, bool ignore)
        {
            if (!ignore)
            {
                foreach (var (a, b) in _ignoredPairs)
                {
                    if (a != null && b != null)
                        Physics.IgnoreCollision(a, b, false);
                }
                _ignoredPairs.Clear();
                return;
            }

            if (ball == null || holder == null)
                return;

            _ignoredPairs.Clear();
            var ballCols = ball.GetComponentsInChildren<Collider>(true);
            var holderCols = holder.GetComponentsInChildren<Collider>(true);
            foreach (var bc in ballCols)
            {
                if (bc == null || bc.isTrigger)
                    continue;
                foreach (var hc in holderCols)
                {
                    if (hc == null || hc.isTrigger)
                        continue;
                    Physics.IgnoreCollision(bc, hc, true);
                    _ignoredPairs.Add((bc, hc));
                }
            }
        }

        // ================================================================
        //  Client message handlers
        // ================================================================

        public static void HandleHoldStarted(GloveHoldStartedMessage msg)
        {
            if (!TryGetBridge(msg.HolderNetId, out var bridge))
                return;

            bool alreadyThisSession =
                bridge.IsHolding && bridge.CurrentSessionId == msg.SessionId;

            if (!alreadyThisSession)
            {
                bridge.ApplyClientHoldState(msg.SessionId, msg.Duration, msg.TimeRemaining);

                var ball = bridge.GetComponent<PlayerInfo>()?.AsGolfer?.OwnBall;
                var holder = bridge.GetComponent<PlayerInfo>();
                if (ball != null && holder != null)
                {
                    // Simulating peer: kinematic capture + snap. Others: disable
                    // hittability; LateUpdate TickAttachBall keeps visuals on-glove.
                    if (
                        !bridge._hasPhysicsSnapshot
                        && (ball.AsEntity == null || ball.AsEntity.IsSimulatingRigidbody())
                    )
                        bridge.CaptureAndPrepareBall(ball, holder);
                    else if (bridge._disabledBallColliders.Count == 0)
                    {
                        bridge.DisableBallHittability(ball);
                        bridge.IgnoreCollisionsWithHolder(ball, holder, ignore: true);
                    }
                }
            }

            GloveHoldIndicatorOverlay.Instance?.Show(msg.HolderNetId);
        }

        public static void HandleReleased(GloveReleasedMessage msg)
        {
            if (!TryGetBridge(msg.HolderNetId, out var bridge))
            {
                GloveHoldIndicatorOverlay.Instance?.Hide(msg.HolderNetId);
                return;
            }

            // Ignore stale releases from a previous session (listen-host already
            // cleared state in ServerRelease before SendToAll).
            if (bridge.CurrentSessionId != msg.SessionId)
            {
                GloveHoldIndicatorOverlay.Instance?.Hide(msg.HolderNetId);
                return;
            }

            // Clear holding before restoring physics so TickAttachBall
            // cannot overwrite the launch on this frame.
            bridge.ApplyClientReleaseState(msg.SessionId);

            var ball = bridge.GetComponent<PlayerInfo>()?.AsGolfer?.OwnBall;
            if (ball != null && bridge._hasPhysicsSnapshot)
            {
                var entity = ball.AsEntity;
                if (entity == null || entity.IsSimulatingRigidbody())
                {
                    bool snap =
                        msg.Reason == GloveReleaseReason.Timeout
                        || msg.Reason == GloveReleaseReason.Interrupt
                        || msg.Reason == GloveReleaseReason.Cleanup;
                    bridge.RestoreAndLaunchBall(ball, msg.WorldPosition, msg.Velocity, snap);
                    bridge.MaybeBeginSpinachFlight(ball, msg.Reason, msg.PowerMultiplier);
                }
                else
                {
                    bridge.RestoreCollisionStateOnly();
                }
            }
            else
            {
                bridge.RestoreCollisionStateOnly();
            }

            GloveHoldIndicatorOverlay.Instance?.Hide(msg.HolderNetId);
        }

        public static void HandleActiveHolds(GloveActiveHoldsMessage msg)
        {
            if (msg.Holds == null)
                return;
            foreach (var hold in msg.Holds)
                HandleHoldStarted(hold);
        }

        public static void ServerSyncActiveHoldsTo(NetworkConnectionToClient conn)
        {
            if (!NetworkServer.active || conn == null)
                return;

            var list = new List<GloveHoldStartedMessage>(ServerActiveHolds.Count);
            float now = Time.time;
            foreach (var kv in ServerActiveHolds)
            {
                float remaining = Mathf.Max(0f, kv.Value.EndTime - now);
                if (remaining <= 0f)
                    continue;
                list.Add(
                    new GloveHoldStartedMessage
                    {
                        HolderNetId = kv.Key,
                        SessionId = kv.Value.SessionId,
                        Duration = kv.Value.Duration,
                        TimeRemaining = remaining,
                    }
                );
            }

            if (list.Count == 0)
                return;

            conn.Send(new GloveActiveHoldsMessage { Holds = list.ToArray() });
        }

        private void ApplyClientHoldState(uint sessionId, float duration, float timeRemaining)
        {
            IsHolding = true;
            CurrentSessionId = sessionId;
            HoldDuration = duration;
            HoldStartTime = Time.time - Mathf.Max(0f, duration - timeRemaining);
            CancelLocalCharge();
            // Pickup uses LMB — require a full release before charge/throw can start.
            _throwInputArmed = false;

            if (isOwned)
                GloveOverlay.Instance?.SetHolding(true, timeRemaining);
        }

        private void ApplyClientReleaseState(uint sessionId)
        {
            // Session-scoped: never let a stale release clear a newer hold.
            if (CurrentSessionId != sessionId)
                return;

            IsHolding = false;
            CurrentSessionId = 0;
            CancelLocalCharge();
            _throwInputArmed = false;

            if (isOwned)
                GloveOverlay.Instance?.ForceClose();
        }

        private static bool TryGetBridge(uint holderNetId, out GloveNetworkBridge bridge)
        {
            bridge = null;
            var dict = NetworkServer.active ? NetworkServer.spawned : NetworkClient.spawned;
            if (!dict.TryGetValue(holderNetId, out var identity) || identity == null)
                return false;
            bridge = identity.GetComponent<GloveNetworkBridge>();
            return bridge != null;
        }

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x)
            && !float.IsNaN(v.y)
            && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x)
            && !float.IsInfinity(v.y)
            && !float.IsInfinity(v.z);

        // ================================================================
        //  Cleanup
        // ================================================================

        public override void ServerHoleCleanup()
        {
            if (_serverHolding)
                ServerRelease(GloveReleaseReason.Cleanup, Vector3.zero);
        }

        public override void ClientHoleCleanup()
        {
            CancelLocalCharge();
            _throwInputArmed = false;

            // Pure clients may run hole cleanup before Released arrives — always
            // restore physics/colliders if we still hold state.
            if (
                IsHolding
                || _hasPhysicsSnapshot
                || _ignoredPairs.Count > 0
                || _disabledBallColliders.Count > 0
            )
            {
                var ball = GetComponent<PlayerInfo>()?.AsGolfer?.OwnBall;
                if (ball != null && _hasPhysicsSnapshot)
                {
                    var entity = ball.AsEntity;
                    if (entity == null || entity.IsSimulatingRigidbody())
                        RestoreAndLaunchBall(ball, GetFeetDropPosition(), Vector3.zero, snapToFeet: true);
                    else
                        RestoreCollisionStateOnly();
                }
                else
                {
                    RestoreCollisionStateOnly();
                }

                if (IsHolding)
                    ApplyClientReleaseState(CurrentSessionId);
            }

            GloveHoldIndicatorOverlay.Instance?.ClearAll();
            if (isOwned)
                GloveOverlay.Instance?.ForceClose();
        }

        public override void OnStopServer()
        {
            if (_serverHolding)
                ServerRelease(GloveReleaseReason.Cleanup, Vector3.zero);
        }

        public override void OnStopClient()
        {
            if (_hasPhysicsSnapshot || _disabledBallColliders.Count > 0 || _ignoredPairs.Count > 0)
                RestoreCollisionStateOnly();
            IsHolding = false;
            CurrentSessionId = 0;
            CancelLocalCharge();
            if (isOwned)
                GloveOverlay.Instance?.ForceClose();
        }
    }
}
