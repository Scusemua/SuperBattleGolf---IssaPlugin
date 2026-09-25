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
    /// Per-player Glove / Evil Glove session. Server owns pickup / timeout / release
    /// decisions. While held, the simulating peer (and visual peers) snap the ball to
    /// the glove world pose each LateUpdate using a kinematic Rigidbody — no Transform
    /// parenting. Evil Glove may hold any player's ball (tracked by BallOwnerNetId).
    /// </summary>
    public class GloveNetworkBridge : NetworkBridgeBase
    {
        private static readonly PropertyInfo ServerLastStrokePositionProp = AccessTools.Property(
            typeof(GolfBall),
            "ServerLastStrokePosition"
        );

        private static readonly MethodInfo OnPlayerHitOwnBallMethod = AccessTools.Method(
            typeof(PlayerGolfer),
            "OnPlayerHitOwnBall"
        );

        private static readonly Dictionary<uint, ActiveHold> ServerActiveHolds = new();

        /// <summary>Server: ballOwnerNetId → holderNetId for exclusive holds.</summary>
        private static readonly Dictionary<uint, uint> ServerBusyBalls = new();

        /// <summary>
        /// Client mirror of busy ball owners, updated from HoldStarted / Released so
        /// aim lock-on can skip balls already held without waiting on the server.
        /// </summary>
        private static readonly HashSet<uint> ClientBusyBalls = new();

        private struct ActiveHold
        {
            public uint SessionId;
            public float EndTime;
            public float Duration;
            public uint BallOwnerNetId;
        }

        // ── Server session ────────────────────────────────────────────────
        private bool _serverHolding;
        private uint _serverSessionId;
        private float _serverEndTime;
        private float _serverDuration;
        private uint _nextSessionId = 1;
        /// <summary>Inventory slot that holds the glove for this session (consumed on release).</summary>
        private int _wielderSlot = -1;

        /// <summary>Player netId whose OwnBall is being carried this session.</summary>
        private uint _heldBallOwnerNetId;

        /// <summary>Glove or EvilGlove item type that started this session.</summary>
        private ItemType _sessionItemType;

        private bool _savedDetectCollisions;
        private bool _hasPhysicsSnapshot;
        private readonly List<(Collider a, Collider b)> _ignoredPairs = new();
        private readonly List<(Collider col, bool wasEnabled)> _disabledBallColliders = new();
        private readonly List<(PlayerInfo owner, float angle, float sqDist)> _aimScratch = new();

        // ── Replicated hold flag (all peers) ──────────────────────────────
        /// <summary>True while this player is carrying a ball (server + clients).</summary>
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
        //  Busy map (server + client mirror)
        // ================================================================

        /// <summary>True if <paramref name="ballOwnerNetId"/>'s ball is already held.</summary>
        public static bool IsBallBusy(uint ballOwnerNetId)
        {
            if (ballOwnerNetId == 0)
                return false;
            if (NetworkServer.active)
                return ServerIsBallBusy(ballOwnerNetId);
            return ClientBusyBalls.Contains(ballOwnerNetId);
        }

        private static bool ServerIsBallBusy(uint ballOwnerNetId) =>
            ballOwnerNetId != 0 && ServerBusyBalls.ContainsKey(ballOwnerNetId);

        private static void ServerRegisterBusy(uint ballOwnerNetId, uint holderNetId)
        {
            if (ballOwnerNetId == 0)
                return;
            ServerBusyBalls[ballOwnerNetId] = holderNetId;
        }

        private static void ServerClearBusy(uint ballOwnerNetId)
        {
            if (ballOwnerNetId == 0)
                return;
            ServerBusyBalls.Remove(ballOwnerNetId);
        }

        private GolfBall ResolveHeldBall() =>
            PlayerBallResolver.TryGetOwnBall(_heldBallOwnerNetId);

        /// <summary>Ball currently held, or null.</summary>
        public GolfBall HeldBall => ResolveHeldBall();

        /// <summary>Item type that started this hold (Glove or EvilGlove).</summary>
        public ItemType SessionItemType => _sessionItemType;

        /// <summary>Throw tuning for the active session (Glove vs Evil config).</summary>
        public float ConfigMinimumThrowSpeed => SessionMinimumThrowSpeed;
        public float ConfigMaximumThrowSpeed => SessionMaximumThrowSpeed;
        public float ConfigThrowUpwardBias => SessionThrowUpwardBias;

        private bool SessionIsEvil => PlayerBallResolver.IsEvilGlove(_sessionItemType);

        private float SessionHoldDurationConfig =>
            SessionIsEvil
                ? ModConfig.EvilGlove.HoldDuration.Value
                : ModConfig.Glove.HoldDuration.Value;

        private float SessionChargeDurationConfig =>
            SessionIsEvil
                ? ModConfig.EvilGlove.ChargeDuration.Value
                : ModConfig.Glove.ChargeDuration.Value;

        private float SessionMinimumThrowSpeed =>
            SessionIsEvil
                ? ModConfig.EvilGlove.MinimumThrowSpeed.Value
                : ModConfig.Glove.MinimumThrowSpeed.Value;

        private float SessionMaximumThrowSpeed =>
            SessionIsEvil
                ? ModConfig.EvilGlove.MaximumThrowSpeed.Value
                : ModConfig.Glove.MaximumThrowSpeed.Value;

        private float SessionThrowUpwardBias =>
            SessionIsEvil
                ? ModConfig.EvilGlove.ThrowUpwardBias.Value
                : ModConfig.Glove.ThrowUpwardBias.Value;

        private float SessionKnockoutEjectSpeed =>
            SessionIsEvil
                ? ModConfig.EvilGlove.KnockoutEjectSpeed.Value
                : ModConfig.Glove.KnockoutEjectSpeed.Value;

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

            if (ItemRegistry.GetItemTypeAtSlot(inventory, slot) != ItemRegistry.GloveItemType)
                return;

            NetworkClient.Send(
                new GlovePickupRequestMessage
                {
                    EquippedSlotIndex = slot,
                    BallOwnerNetId = netId,
                    AimDirection = Vector3.zero,
                }
            );
        }

        /// <summary>
        /// Evil Glove: require an aim lock from <see cref="EvilGloveOverlay"/>, then
        /// send pickup with the camera aim direction. Server re-runs cone selection
        /// from the holder's head and does not trust <c>BallOwnerNetId</c>.
        /// No lock → no send (no consume).
        /// </summary>
        public void ClientRequestEvilPickup()
        {
            if (!isOwned || IsHolding)
                return;

            var inventory = CachedInventory;
            if (inventory == null)
                return;

            int slot = inventory.EquippedItemIndex;
            if (slot < 0)
                return;

            if (ItemRegistry.GetItemTypeAtSlot(inventory, slot) != ItemRegistry.EvilGloveItemType)
                return;

            var info = GetComponent<PlayerInfo>();
            var input = info?.Input;
            if (input == null || !input.IsHoldingAimSwing)
                return;

            var overlay = EvilGloveOverlay.Instance;
            uint targetOwnerNetId = overlay != null ? overlay.BestTargetOwnerNetId : 0u;
            if (targetOwnerNetId == 0)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            NetworkClient.Send(
                new GlovePickupRequestMessage
                {
                    EquippedSlotIndex = slot,
                    BallOwnerNetId = targetOwnerNetId,
                    AimDirection = cam.transform.forward,
                }
            );
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

            float duration = Mathf.Max(0.05f, SessionChargeDurationConfig);
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

        public void ServerHandlePickupRequest(
            int equippedSlotIndex,
            uint ballOwnerNetId,
            Vector3 aimDirection
        )
        {
            if (!NetworkServer.active || _serverHolding)
                return;

            var inventory = CachedInventory;
            var info = GetComponent<PlayerInfo>();
            var movement = info?.Movement;

            if (inventory == null || info == null || movement == null)
                return;

            if (equippedSlotIndex < 0)
                return;

            // Require the claimed slot to be the currently equipped one.
            int equippedNow = ServerGetEquippedSlotIndex(inventory, info);
            if (equippedNow != equippedSlotIndex)
            {
                IssaPluginPlugin.Log.LogDebug(
                    "[Glove] Pickup rejected: slot is not currently equipped."
                );
                return;
            }

            ItemType itemType = ItemRegistry.GetItemTypeAtSlot(inventory, equippedSlotIndex);
            if (!PlayerBallResolver.IsGloveLike(itemType))
            {
                IssaPluginPlugin.Log.LogWarning("[Glove] Pickup rejected: glove-like item not in slot.");
                return;
            }

            bool isEvil = PlayerBallResolver.IsEvilGlove(itemType);

            if (!ServerHolderAllowed(info, movement, out string rejectReason, requireOnFoot: true))
            {
                IssaPluginPlugin.Log.LogDebug($"[Glove] Pickup rejected: {rejectReason}");
                return;
            }

            if (!isEvil)
            {
                // Normal Glove always grabs the holder's own ball.
                ballOwnerNetId = netId;
            }
            else
            {
                // Authoritative aim-cone selection. Origin is server-derived at the
                // holder's head so a forged client AimOrigin cannot teleport the cone.
                uint clientClaim = ballOwnerNetId;
                if (!IsFinite(aimDirection) || aimDirection.sqrMagnitude < 0.0001f)
                {
                    IssaPluginPlugin.Log.LogDebug("[EvilGlove] Pickup rejected: invalid aim direction.");
                    return;
                }

                Vector3 aimOrigin = GetServerAimOrigin(info);
                var selected = GolfBallAimTargeting.SelectBallOwner(
                    aimOrigin,
                    aimDirection,
                    ModConfig.EvilGlove.MaxAimAngle.Value,
                    ModConfig.EvilGlove.MaxTargetDistance.Value,
                    _aimScratch,
                    ServerIsBallBusy
                );
                ballOwnerNetId = PlayerBallResolver.GetPlayerNetId(selected);
                if (ballOwnerNetId == 0)
                {
                    IssaPluginPlugin.Log.LogDebug(
                        "[EvilGlove] Pickup rejected: no ball in aim cone."
                    );
                    return;
                }

                if (clientClaim != 0 && clientClaim != ballOwnerNetId)
                {
                    IssaPluginPlugin.Log.LogDebug(
                        $"[EvilGlove] Client lock {clientClaim} ≠ server pick {ballOwnerNetId}; using server."
                    );
                }
            }

            if (ServerIsBallBusy(ballOwnerNetId))
            {
                IssaPluginPlugin.Log.LogDebug(
                    $"[Glove] Pickup rejected: ball owner {ballOwnerNetId} already held."
                );
                return;
            }

            var ball = PlayerBallResolver.TryGetOwnBall(ballOwnerNetId);
            if (ball == null)
            {
                IssaPluginPlugin.Log.LogDebug(
                    $"[Glove] Pickup rejected: no ball for owner {ballOwnerNetId}."
                );
                return;
            }

            if (!ServerBallAllowed(ball, out rejectReason))
            {
                IssaPluginPlugin.Log.LogDebug($"[Glove] Pickup rejected: {rejectReason}");
                return;
            }

            if (!isEvil)
            {
                float radius = ModConfig.Glove.PickupRadius.Value;
                if ((ball.transform.position - transform.position).sqrMagnitude > radius * radius)
                {
                    IssaPluginPlugin.Log.LogDebug("[Glove] Pickup rejected: out of pickup radius.");
                    return;
                }
            }

            // Keep the glove equipped for the hold; consume on throw / timeout / KO / unequip.
            _wielderSlot = equippedSlotIndex;
            _sessionItemType = itemType;
            _heldBallOwnerNetId = ballOwnerNetId;

            _serverSessionId = _nextSessionId++;
            if (_nextSessionId == 0)
                _nextSessionId = 1;

            _serverDuration = Mathf.Max(0.1f, SessionHoldDurationConfig);
            _serverEndTime = Time.time + _serverDuration;
            _serverHolding = true;

            ServerRegisterBusy(ballOwnerNetId, netId);

            ServerActiveHolds[netId] = new ActiveHold
            {
                SessionId = _serverSessionId,
                EndTime = _serverEndTime,
                Duration = _serverDuration,
                BallOwnerNetId = ballOwnerNetId,
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
                    BallOwnerNetId = ballOwnerNetId,
                }
            );

            string displayName = isEvil ? "Evil Glove" : "Glove";
            ItemWarningBroadcaster.Broadcast(
                info.PlayerId?.PlayerName ?? "Player",
                itemType,
                displayName,
                trackedNetId: netId,
                senderNetId: netId
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[{displayName}] Hold started session={_serverSessionId} ballOwner={ballOwnerNetId} for {_serverDuration:F1}s."
            );
        }

        private static bool ServerHolderAllowed(
            PlayerInfo info,
            PlayerMovement movement,
            out string reason,
            bool requireOnFoot
        )
        {
            reason = null;

            if (info == null)
            {
                reason = "no player";
                return false;
            }

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
            if (ball == null)
            {
                reason = "no ball";
                return false;
            }

            // Shared eligibility with GolfBallAimTargeting (hidden / OOB / in-hole).
            if (!GolfBallAimTargeting.IsBallEligible(ball))
            {
                if (ball.IsHidden)
                    reason = "ball hidden";
                else if (ball.OutOfBoundsReturnState != BallOutOfBoundsReturnState.None)
                    reason = "ball returning from OOB";
                else
                    reason = "ball in hole";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Server-side aim origin for Evil Glove cone selection — holder head/eyes,
        /// never a client-supplied point.
        /// </summary>
        private static Vector3 GetServerAimOrigin(PlayerInfo info)
        {
            if (info?.HeadBone != null)
                return info.HeadBone.position;
            // Fallback: approximate eye height above the player root.
            return info != null
                ? info.transform.position + Vector3.up * 1.6f
                : Vector3.zero;
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

            var ball = ResolveHeldBall();
            if (ball == null || !ServerBallAllowed(ball, out _))
            {
                ServerRelease(GloveReleaseReason.Cleanup, Vector3.zero);
                return;
            }

            float spinachMult = GloveThrowMath.GetSpinachThrowSpeedMultiplier(
                GetComponent<SpinachNetworkBridge>()?.ServerIsBuffActive == true
            );
            float minSpeed = SessionMinimumThrowSpeed * spinachMult;
            float maxSpeed = SessionMaximumThrowSpeed * spinachMult;
            if (maxSpeed < minSpeed)
                (minSpeed, maxSpeed) = (maxSpeed, minSpeed);

            Vector3 velocity = GloveThrowMath.ComputeThrowVelocity(
                aimDirection,
                charge01,
                minSpeed,
                maxSpeed,
                SessionThrowUpwardBias
            );

            if (!IsFinite(velocity))
                return;

            velocity = Vector3.ClampMagnitude(velocity, Mathf.Max(maxSpeed, 0.01f));
            ServerRelease(GloveReleaseReason.Throw, velocity, spinachMult);
        }

        private void ServerTickHold()
        {
            var info = GetComponent<PlayerInfo>();
            var movement = info?.Movement;
            var ball = ResolveHeldBall();
            var inventory = CachedInventory;

            if (ball == null)
            {
                ServerRelease(GloveReleaseReason.Cleanup, Vector3.zero);
                return;
            }

            // Switched away from the glove or dropped it — drop the ball and consume.
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
                    SessionKnockoutEjectSpeed,
                    GloveThrowMath.KnockoutUpwardBias
                );
                if (!IsFinite(eject))
                    eject = Vector3.up * SessionKnockoutEjectSpeed;
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
                !PlayerBallResolver.IsGloveLike(
                    ItemRegistry.GetItemTypeAtSlot(inventory, _wielderSlot)
                )
            )
                return false;

            var playerInfo = inventory.PlayerInfo ?? GetComponent<PlayerInfo>();
            return ServerGetEquippedSlotIndex(inventory, playerInfo) == _wielderSlot;
        }

        private static int ServerGetEquippedSlotIndex(PlayerInventory inventory, PlayerInfo info)
        {
            // Prefer networked equipped index for remote clients on the server.
            if (inventory != null && !inventory.isLocalPlayer && NetworkServer.active)
            {
                if (info == null)
                    return -1;
                return info.NetworkedEquippedItemIndex;
            }

            return inventory != null ? inventory.EquippedItemIndex : -1;
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
            uint ballOwnerNetId = _heldBallOwnerNetId;
            ItemType sessionItemType = _sessionItemType;
            var ball = ResolveHeldBall();

            _serverHolding = false;
            ServerActiveHolds.Remove(netId);
            ServerClearBusy(ballOwnerNetId);

            var info = GetComponent<PlayerInfo>();

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

            // Consume the glove now that the hold ends. Stroke counting skips hole
            // Cleanup so ending a hole mid-hold does not inflate the scorecard.
            // Evil Glove never registers a stroke.
            ServerConsumeGloveIfPresent();
            if (
                reason != GloveReleaseReason.Cleanup
                && !PlayerBallResolver.IsEvilGlove(sessionItemType)
            )
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
                RestoreCollisionStateOnly(ball);

            NetworkServer.SendToAll(
                new GloveReleasedMessage
                {
                    HolderNetId = netId,
                    SessionId = sessionId,
                    Reason = reason,
                    WorldPosition = releasePos,
                    Velocity = velocity,
                    PowerMultiplier = powerMultiplier,
                    BallOwnerNetId = ballOwnerNetId,
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
        /// Skipped entirely for Evil Glove sessions.
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
                && PlayerBallResolver.IsGloveLike(ItemRegistry.GetItemTypeAtSlot(inventory, slot))
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
            RestoreBallHittability();

            var rb = ball.Rigidbody ?? ball.AsEntity?.Rigidbody;
            if (rb == null)
            {
                // Still clear holder ignore pairs even without a Rigidbody.
                IgnoreCollisionsWithHolder(ball, GetComponent<PlayerInfo>(), ignore: false);
                _hasPhysicsSnapshot = false;
                return;
            }

            // Always sync to the server release pose so clients match (throw / KO /
            // feet-drop), then clear kinematic before writing velocities.
            rb.position = worldPosition;
            ball.transform.position = worldPosition;
            if (snapToFeet || velocity.sqrMagnitude < 0.0001f)
                ball.transform.rotation = Quaternion.identity;

            _hasPhysicsSnapshot = false;

            rb.isKinematic = false;
            rb.detectCollisions = true;
            rb.linearVelocity = velocity;
            rb.angularVelocity = GloveThrowMath.ComputeThrowAngularVelocity(velocity);

            // Clear holder ignores after launch so the ball does not immediately
            // collide with the player the frame it leaves the hand.
            IgnoreCollisionsWithHolder(ball, GetComponent<PlayerInfo>(), ignore: false);
        }

        /// <summary>
        /// Restores ignore-pairs and collider enables without moving the ball.
        /// Used when this peer is not the simulator, or on hole cleanup.
        /// Pass <paramref name="ball"/> when calling after
        /// <see cref="ApplyClientReleaseState"/> cleared <c>_heldBallOwnerNetId</c>.
        /// </summary>
        private void RestoreCollisionStateOnly(GolfBall ball = null)
        {
            ball ??= ResolveHeldBall();
            RestoreBallHittability();
            IgnoreCollisionsWithHolder(ball, GetComponent<PlayerInfo>(), ignore: false);

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
            var ball = ResolveHeldBall();
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

            if (msg.BallOwnerNetId != 0)
                ClientBusyBalls.Add(msg.BallOwnerNetId);

            bool alreadyThisSession =
                bridge.IsHolding && bridge.CurrentSessionId == msg.SessionId;

            if (!alreadyThisSession)
            {
                bridge._heldBallOwnerNetId = msg.BallOwnerNetId;
                bridge.TryCaptureSessionItemTypeFromInventory();
                bridge.ApplyClientHoldState(msg.SessionId, msg.Duration, msg.TimeRemaining);

                var ball = bridge.ResolveHeldBall();
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
            else
            {
                // Listen-host already applied hold on the server path — still sync owner.
                bridge._heldBallOwnerNetId = msg.BallOwnerNetId;
            }

            GloveHoldIndicatorOverlay.Instance?.Show(msg.HolderNetId);
        }

        public static void HandleReleased(GloveReleasedMessage msg)
        {
            if (!TryGetBridge(msg.HolderNetId, out var bridge))
            {
                // Holder gone — drop any stale busy flag for this ball.
                if (msg.BallOwnerNetId != 0)
                    ClientBusyBalls.Remove(msg.BallOwnerNetId);
                GloveHoldIndicatorOverlay.Instance?.Hide(msg.HolderNetId);
                return;
            }

            // Session mismatch: either listen-host already cleared in ServerRelease
            // (CurrentSessionId == 0), or a newer hold replaced this one.
            if (bridge.CurrentSessionId != msg.SessionId)
            {
                if (bridge.CurrentSessionId == 0)
                {
                    // Already released locally — finish UX only (no second launch).
                    if (msg.BallOwnerNetId != 0)
                        ClientBusyBalls.Remove(msg.BallOwnerNetId);
                    GloveHoldIndicatorOverlay.Instance?.Hide(msg.HolderNetId);
                }
                // else: newer session still holding — leave busy + indicator alone.
                return;
            }

            if (msg.BallOwnerNetId != 0)
                ClientBusyBalls.Remove(msg.BallOwnerNetId);

            // Prefer message owner; fall back to session field before release clears it.
            var ball =
                PlayerBallResolver.TryGetOwnBall(msg.BallOwnerNetId) ?? bridge.ResolveHeldBall();

            // Clear holding before restoring physics so TickAttachBall
            // cannot overwrite the launch on this frame.
            bridge.ApplyClientReleaseState(msg.SessionId);

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
                    bridge.RestoreCollisionStateOnly(ball);
                }
            }
            else
            {
                bridge.RestoreCollisionStateOnly(ball);
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
                        BallOwnerNetId = kv.Value.BallOwnerNetId,
                    }
                );
            }

            if (list.Count == 0)
                return;

            conn.Send(new GloveActiveHoldsMessage { Holds = list.ToArray() });
        }

        private void TryCaptureSessionItemTypeFromInventory()
        {
            var inventory = CachedInventory ?? GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            int equipped = inventory.EquippedItemIndex;
            if (equipped < 0)
                return;

            ItemType type = ItemRegistry.GetItemTypeAtSlot(inventory, equipped);
            if (PlayerBallResolver.IsGloveLike(type))
                _sessionItemType = type;
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
            _heldBallOwnerNetId = 0;
            _sessionItemType = default;
            CancelLocalCharge();
            _throwInputArmed = false;

            if (isOwned)
                GloveOverlay.Instance?.ForceClose();
        }

        private static bool TryGetBridge(uint holderNetId, out GloveNetworkBridge bridge)
        {
            bridge = null;
            // Prefer the client spawn map so pure clients resolve remote holders;
            // fall back to the server map for listen-host / dedicated paths.
            if (
                NetworkClient.active
                && NetworkClient.spawned.TryGetValue(holderNetId, out var clientId)
                && clientId != null
            )
            {
                bridge = clientId.GetComponent<GloveNetworkBridge>();
                if (bridge != null)
                    return true;
            }

            if (
                NetworkServer.active
                && NetworkServer.spawned.TryGetValue(holderNetId, out var serverId)
                && serverId != null
            )
            {
                bridge = serverId.GetComponent<GloveNetworkBridge>();
                return bridge != null;
            }

            return false;
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
                uint ownedBall = _heldBallOwnerNetId;
                var ball = ResolveHeldBall();
                if (ball != null && _hasPhysicsSnapshot)
                {
                    var entity = ball.AsEntity;
                    if (entity == null || entity.IsSimulatingRigidbody())
                        RestoreAndLaunchBall(ball, GetFeetDropPosition(), Vector3.zero, snapToFeet: true);
                    else
                        RestoreCollisionStateOnly(ball);
                }
                else
                {
                    RestoreCollisionStateOnly(ball);
                }

                if (ownedBall != 0)
                    ClientBusyBalls.Remove(ownedBall);

                if (IsHolding)
                    ApplyClientReleaseState(CurrentSessionId);
            }

            GloveHoldIndicatorOverlay.Instance?.ClearAll();
            ClientBusyBalls.Clear();
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
            if (_heldBallOwnerNetId != 0)
                ClientBusyBalls.Remove(_heldBallOwnerNetId);

            if (_hasPhysicsSnapshot || _disabledBallColliders.Count > 0 || _ignoredPairs.Count > 0)
                RestoreCollisionStateOnly();
            IsHolding = false;
            CurrentSessionId = 0;
            _heldBallOwnerNetId = 0;
            _sessionItemType = default;
            CancelLocalCharge();
            if (isOwned)
                GloveOverlay.Instance?.ForceClose();
        }
    }
}
