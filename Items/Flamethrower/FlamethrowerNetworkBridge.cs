using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// ── Flame VFX flow (mirrors JetpackNetworkBridge) ────────────────────────
    ///   1. Local player holds LMB → FlamethrowerItem.FireLoop calls
    ///      ClientNotifyFireStart().
    ///   2. Client sends FlamethrowerFireStartMessage to server.
    ///   3. Server broadcasts FlamethrowerFireBeginMessage(shooterNetId) to all.
    ///   4. Each client instantiates the flame particle prefab and parents it to
    ///      that player's transform.  The LOCAL shooter's instance also gets a
    ///      FlamethrowerHitDetector for trigger-based hit detection.
    ///   5. On release / fuel exhaustion, FireLoop calls ClientNotifyFireStop().
    ///   6. Server broadcasts FlamethrowerFireEndMessage; all clients destroy their
    ///      local flame instance.
    ///
    /// ── Burn state flow (mirrors NukeNetworkBridge per-victim pattern) ───────
    ///   1. FlamethrowerHitDetector.OnTriggerStay identifies a victim and calls
    ///      ClientRequestBurn(victimNetId) — rate-limited to BurnRequestInterval.
    ///   2. Client sends FlamethrowerBurnRequestMessage to server.
    ///   3. Server validates + starts/refreshes a burn timer for the victim.
    ///      Broadcasts FlamethrowerBurnBeginMessage (first hit only; subsequent
    ///      hits silently refresh the server-side timer).
    ///   4. Each client: shows fire VFX on victim. Victim's own client sets
    ///      LocalPlayerIsBurning = true so FlamethrowerMovementPatch injects
    ///      random movement input.
    ///   5. After BurnDuration, server broadcasts FlamethrowerBurnEndMessage.
    ///   6. All clients: destroy victim fire VFX.
    ///      Victim's own client: LocalPlayerIsBurning = false, calls TryKnockOut.
    /// </summary>
    public class FlamethrowerNetworkBridge : NetworkBridgeBase
    {
        // ── Static server state ───────────────────────────────────────────────
        // Tracks active burn timers keyed by victim netId.
        // Value: (bridge that owns the coroutine, the coroutine itself).
        private static readonly Dictionary<
            uint,
            (FlamethrowerNetworkBridge host, Coroutine co)
        > _serverActiveBurns = new();

        // netId of the player who started each burn (used to attribute the knockout).
        private static readonly Dictionary<uint, uint> _serverBurnShooters = new();

        // ── Static client state ───────────────────────────────────────────────
        /// <summary>True while the local player is currently on fire.</summary>
        public static bool LocalPlayerIsBurning { get; private set; }

        // ── Per-instance server state ─────────────────────────────────────────
        private bool _serverFiring;

        // ── Per-instance client state ─────────────────────────────────────────
        private GameObject _flameParticles; // shown on all clients while this player fires
        private GameObject _victimFireParticles; // shown on all clients while this player burns
        private FlamethrowerHitDetector _hitDetector; // non-null only on the local shooter's instance
        private Camera _camera;
        private bool _wasAimedIn;

        // ================================================================
        //  Client → Server helpers (called by FlamethrowerItem.FireLoop)
        // ================================================================

        public void ClientNotifyFireStart() =>
            NetworkClient.Send(new FlamethrowerFireStartMessage());

        public void ClientNotifyFireStop()
        {
            _hitDetector?.ResetCooldowns();
            NetworkClient.Send(new FlamethrowerFireStopMessage());
        }

        /// <summary>
        /// Called by FlamethrowerHitDetector when the flame collider touches a player.
        /// Rate-limiting is handled by the detector itself.
        /// </summary>
        public void ClientRequestBurn(uint victimNetId) =>
            NetworkClient.Send(new FlamethrowerBurnRequestMessage { VictimNetId = victimNetId });

        // ================================================================
        //  Server handlers — registered in NetworkManagerPatches
        // ================================================================

        public void ServerHandleFireStart()
        {
            if (!isServer)
                return;
            if (_serverFiring)
                return;

            _serverFiring = true;
            NetworkServer.SendToAll(new FlamethrowerFireBeginMessage { ShooterNetId = netId });
        }

        public void ServerHandleFireStop()
        {
            if (!isServer)
                return;
            if (!_serverFiring)
                return;

            _serverFiring = false;
            NetworkServer.SendToAll(new FlamethrowerFireEndMessage { ShooterNetId = netId });
        }

        public void ServerHandleBurnRequest(NetworkConnectionToClient conn, uint victimNetId)
        {
            if (!isServer)
                return;

            // Validate the victim still exists.
            if (!NetworkServer.spawned.TryGetValue(victimNetId, out var victimIdentity))
                return;

            var victimBridge = victimIdentity.GetComponent<FlamethrowerNetworkBridge>();
            if (victimBridge == null)
                return;

            if (_serverActiveBurns.TryGetValue(victimNetId, out var existing))
            {
                // Victim is already burning — refresh the timer.
                if (existing.host != null && existing.co != null)
                    existing.host.StopCoroutine(existing.co);
            }
            else
            {
                // First hit — tell all clients the victim started burning.
                NetworkServer.SendToAll(
                    new FlamethrowerBurnBeginMessage { VictimNetId = victimNetId }
                );
            }

            // Record or update which player triggered this burn.
            _serverBurnShooters[victimNetId] = netId;

            float duration = ModConfig.Flamethrower.BurnDuration.Value;
            var co = victimBridge.StartCoroutine(ServerBurnTimeout(victimNetId, duration));
            _serverActiveBurns[victimNetId] = (victimBridge, co);
        }

        // ================================================================
        //  Client handlers (static) — registered in NetworkManagerPatches
        // ================================================================

        public static void HandleFireBegin(FlamethrowerFireBeginMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.ShooterNetId, out var identity))
                return;
            identity
                .GetComponent<FlamethrowerNetworkBridge>()
                ?.ClientShowFlame(isLocalShooter: identity.isLocalPlayer);
        }

        public static void HandleFireEnd(FlamethrowerFireEndMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.ShooterNetId, out var identity))
                return;
            identity.GetComponent<FlamethrowerNetworkBridge>()?.ClientHideFlame();
        }

        public static void HandleBurnBegin(FlamethrowerBurnBeginMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.VictimNetId, out var identity))
                return;
            var bridge = identity.GetComponent<FlamethrowerNetworkBridge>();
            if (bridge == null)
                return;

            bridge.ClientShowVictimFire();

            if (identity.isLocalPlayer)
                LocalPlayerIsBurning = true;
        }

        public static void HandleBurnEnd(FlamethrowerBurnEndMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.VictimNetId, out var identity))
                return;
            var bridge = identity.GetComponent<FlamethrowerNetworkBridge>();
            if (bridge == null)
                return;

            bridge.ClientHideVictimFire();

            if (!identity.isLocalPlayer)
                return;

            LocalPlayerIsBurning = false;

            // Apply knockout — same pattern as NukeNetworkBridge.
            var movement = identity.GetComponent<PlayerMovement>();
            if (movement == null)
                return;

            // Resolve the shooter for attribution (may be null if they left).
            PlayerInfo shooterInfo = null;
            if (
                msg.ShooterNetId != 0
                && NetworkClient.spawned.TryGetValue(msg.ShooterNetId, out var shooterIdentity)
            )
                shooterInfo = shooterIdentity.GetComponent<PlayerInfo>();

            movement.TryKnockOut(
                shooterInfo,
                KnockoutType.Rocket,
                isLegSweep: false,
                localOrigin: Vector3.zero,
                distance: 0f,
                incomingVelocityChange: msg.KnockImpulse,
                canBeBlockedByElectromagnetShield: false,
                itemUseId: default,
                fromSpecialState: false,
                canFallbackToUnground: true,
                isNewKnockout: out _
            );
        }

        // ================================================================
        //  Per-client VFX management
        // ================================================================

        /// <summary>
        /// Instantiates the flame particle prefab on this client.
        /// When <paramref name="isLocalShooter"/> is true (this is the shooter's
        /// own machine), a FlamethrowerHitDetector is also added for hit detection.
        /// </summary>
        private void ClientShowFlame(bool isLocalShooter)
        {
            ClientHideFlame();
            if (AssetLoader.FlamethrowerParticlePrefab == null)
                return;

            _flameParticles = Object.Instantiate(AssetLoader.FlamethrowerParticlePrefab);
            _flameParticles.transform.SetParent(transform, false);
            // Offset to roughly chest-height, slightly in front of the player.
            // The exact values should be tuned against the actual prefab dimensions.
            _flameParticles.transform.localPosition = new Vector3(0f, 0.8f, 0.3f);
            _flameParticles.transform.localRotation = Quaternion.identity;
            _flameParticles.SetActive(true);

            if (isLocalShooter)
            {
                // OnTriggerStay only fires on a MonoBehaviour that shares a GameObject
                // with the trigger Collider. The collider may be on a child object in
                // the particle prefab hierarchy, so we find it and attach there.
                // Fall back to the root if no collider child is found.
                var triggerCol = _flameParticles.GetComponentInChildren<Collider>();
                var detectorTarget = triggerCol != null ? triggerCol.gameObject : _flameParticles;
                _hitDetector = detectorTarget.AddComponent<FlamethrowerHitDetector>();
                _hitDetector.Initialize(this, netId);
            }
        }

        private void ClientHideFlame()
        {
            if (_flameParticles == null)
                return;
            Object.Destroy(_flameParticles);
            _flameParticles = null;
            _hitDetector = null;
        }

        private void ClientShowVictimFire()
        {
            ClientHideVictimFire();
            if (AssetLoader.FlamethrowerVictimFirePrefab == null)
                return;

            _victimFireParticles = Object.Instantiate(AssetLoader.FlamethrowerVictimFirePrefab);
            _victimFireParticles.transform.SetParent(transform, false);
            _victimFireParticles.transform.localPosition = Vector3.zero;
            _victimFireParticles.transform.localRotation = Quaternion.identity;
            _victimFireParticles.SetActive(true);
        }

        private void ClientHideVictimFire()
        {
            if (_victimFireParticles == null)
                return;
            Object.Destroy(_victimFireParticles);
            _victimFireParticles = null;
        }

        private void LateUpdate()
        {
            var localInfo = GameManager.LocalPlayerInfo;
            bool flameThrowerEquipped =
                localInfo?.Inventory != null
                && localInfo.Inventory.GetEffectivelyEquippedItem(true)
                    == ItemRegistry.FlamethrowerItemType;

            if (!flameThrowerEquipped)
                return;

            bool aimedIn = Mouse.current?.rightButton.isPressed ?? false;

            if (!aimedIn && _wasAimedIn)
            {
                var rot = localInfo.Movement?.transform.eulerAngles ?? default;
                FixLookRotationAfterScope();
            }
            else if (aimedIn)
            {
                // Play the aim sound once on scope entry.
                if (!_wasAimedIn)
                {
                    var rot = localInfo.Movement?.transform.eulerAngles ?? default;
                    var camRot = Camera.main?.transform.eulerAngles ?? default;
                    localInfo.PlayerAudio.PlayItemAimForAllClients(ItemType.ElephantGun);
                }

                // For some cursed reason, we only need to rotate the flamethrower when aimed-in
                // and NOT actively shooting...
                if (Mouse.current != null && !Mouse.current.leftButton.isPressed)
                    CorrectAimRotation();
            }

            _wasAimedIn = aimedIn;
        }

        private void CorrectAimRotation()
        {
            var li = GameManager.LocalPlayerInfo;
            if (li?.Movement == null)
                return;
            _camera ??= Camera.main;
            var cam = _camera;
            if (cam == null)
                return;
            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f)
                return;
            li.Movement.transform.rotation =
                Quaternion.LookRotation(fwd.normalized) * Quaternion.Euler(0f, 65f, 0f);
        }

        private void FixLookRotationAfterScope()
        {
            if (Camera.main != null)
                GameManager
                    .LocalPlayerInfo
                    ?.Movement
                    ?.transform.rotation = Quaternion.LookRotation(
                        Camera.main.transform.forward.normalized
                    );
        }

        // ================================================================
        //  Server internals
        // ================================================================

        private IEnumerator ServerBurnTimeout(uint victimNetId, float duration)
        {
            yield return new WaitForSeconds(duration);

            _serverActiveBurns.Remove(victimNetId);
            _serverBurnShooters.TryGetValue(victimNetId, out uint shooterNetId);
            _serverBurnShooters.Remove(victimNetId);

            // Compute a small outward knock impulse from the victim's current position
            // so all clients receive the same value.
            Vector3 knockImpulse = Vector3.zero;
            if (NetworkServer.spawned.TryGetValue(victimNetId, out var victimIdentity))
            {
                // Push the victim in the direction they were last facing.
                knockImpulse = victimIdentity.transform.forward * 3f;
            }

            NetworkServer.SendToAll(
                new FlamethrowerBurnEndMessage
                {
                    VictimNetId = victimNetId,
                    ShooterNetId = shooterNetId,
                    KnockImpulse = knockImpulse,
                }
            );
        }

        /// <summary>
        /// Stops all active burn coroutines and clears server state without sending
        /// any network messages. Used for hole transitions and server shutdown — client
        /// VFX and LocalPlayerIsBurning are cleaned up by ClientHoleCleanup on each
        /// client, so no FlamethrowerBurnEndMessage is needed (and sending one would
        /// trigger spurious TryKnockOut calls at hole end).
        /// </summary>
        private static void ServerCancelAllBurns()
        {
            foreach (var (_, (host, co)) in _serverActiveBurns)
                if (host != null && co != null)
                    host.StopCoroutine(co);

            _serverActiveBurns.Clear();
            _serverBurnShooters.Clear();
        }

        // ================================================================
        //  Hole cleanup
        // ================================================================

        public override void ServerHoleCleanup()
        {
            if (_serverFiring)
            {
                _serverFiring = false;
                NetworkServer.SendToAll(new FlamethrowerFireEndMessage { ShooterNetId = netId });
            }

            // Cancel all active burns on the first bridge to run cleanup (subsequent
            // calls are no-ops because the dicts are already empty).
            GlobalServerHoleCleanup();
        }

        public static void GlobalServerHoleCleanup()
        {
            ServerCancelAllBurns();
        }

        public override void ClientHoleCleanup()
        {
            ClientHideFlame();
            ClientHideVictimFire();
            LocalPlayerIsBurning = false;
            if (isLocalPlayer)
                FlamethrowerItem.ResetFiringState();
        }

        public override void OnStopServer()
        {
            // Just clear local state — don't send network messages on shutdown
            // since connections may already be closing (matches JetpackNetworkBridge pattern).
            _serverFiring = false;
            ServerCancelAllBurns();
        }
    }
}
