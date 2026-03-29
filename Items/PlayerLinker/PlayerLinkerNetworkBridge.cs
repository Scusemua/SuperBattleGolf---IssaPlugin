using System.Collections;
using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// The Player Linker tethers two players together with a symmetric spring:
    /// both players send their positions to the server at ~20 Hz; the server
    /// relays each player's position to the other; each client's spring coroutine
    /// pulls toward the received position each FixedUpdate tick.
    ///
    /// Force propagation is fully emergent — speed boosts, explosions, and golf
    /// cart acceleration all increase one player's positional drift, which causes
    /// a stronger spring force on the other player automatically.
    ///
    /// Global one-at-a-time lock via GlobalSessionLock&lt;PlayerLinkerNetworkBridge&gt;.
    /// Session state lives only on the wielder's bridge; the target's bridge has none.
    /// </summary>
    public class PlayerLinkerNetworkBridge : NetworkBridgeBase
    {
        // =====================================================================
        //  Server-side per-instance state (wielder's bridge only)
        // =====================================================================

        private bool _serverSessionActive;
        private NetworkConnectionToClient _wielderConn;
        private NetworkConnectionToClient _targetConn;
        private PlayerInfo _wielderInfo;
        private PlayerInfo _targetInfo;
        private Coroutine _serverTimeout;
        private int _wielderLinkerSlot = -1; // cached at session start; used by ServerEndSession

        // =====================================================================
        //  Owning-client per-instance state
        // =====================================================================

        private float _sendTimer;

        // =====================================================================
        //  Static shared state (safe: one session at a time via GlobalSessionLock)
        // =====================================================================

        private static bool s_tetherActive;
        private static uint s_wielderNetId;
        private static uint s_targetNetId;
        private static Vector3 s_otherPlayerPos; // other tethered player's last known position
        private static float s_tetherDuration;
        private static float s_tetherStartTime;

        // Force coroutine — runs on ONE of the player's MonoBehaviours
        private static Coroutine s_forceCoroutine;
        private static PlayerMovement s_forceMovement;

        // Visual tether line (local-only, all clients)
        private static LineRenderer s_tetherLine;

        // =====================================================================
        //  Public static accessors (read by PlayerLinkerOverlay)
        // =====================================================================

        public static bool TetherActive => s_tetherActive;
        public static uint WielderNetId => s_wielderNetId;
        public static uint TargetNetId => s_targetNetId;
        public static float TetherDuration => s_tetherDuration;
        public static float TetherStartTime => s_tetherStartTime;

        // =====================================================================
        //  Unity lifecycle
        // =====================================================================

        private void Update()
        {
            // Update the line renderer for all clients while a tether is active.
            if (s_tetherLine != null)
                UpdateTetherLine();

            // Remaining logic: tick sending for this client's owned bridge only.
            if (!isOwned || !s_tetherActive)
                return;

            _sendTimer -= Time.deltaTime;
            if (_sendTimer <= 0f)
            {
                NetworkClient.Send(new PlayerLinkerTickMessage { Position = transform.position });
                _sendTimer = Configuration.PlayerLinkerInputSendInterval.Value;
            }
        }

        // =====================================================================
        //  Client — initiating the link
        // =====================================================================

        /// Called from PlayerLinkerItemDefinition.OnUse → GetComponent<PlayerLinkerNetworkBridge>().
        public void ClientUse()
        {
            if (!isOwned)
                return;

            var bestTarget = PlayerLinkerOverlay.Instance?.BestTargetIdentity;

            if (bestTarget == null)
            {
                IssaPluginPlugin.Log.LogInfo("[PlayerLinker] ClientUse: no valid target in cone.");
                return;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[PlayerLinker] ClientUse: locking onto netId={bestTarget.netId}."
            );
            NetworkClient.Send(new PlayerLinkerLockOnMessage { TargetNetId = bestTarget.netId });
        }

        // =====================================================================
        //  Server — handling lock-on request
        // =====================================================================

        public void ServerHandleLockOn(
            NetworkConnectionToClient conn,
            PlayerLinkerLockOnMessage msg
        )
        {
            if (!isServer)
                return;

            // ── 1. Acquire global session lock ────────────────────────────────
            if (!GlobalSessionLock<PlayerLinkerNetworkBridge>.TryAcquire(this))
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[PlayerLinker] Server: session busy — rejecting lock-on."
                );
                conn.Send(new PlayerLinkerBusyMessage());
                return;
            }

            // ── 2. Validate wielder has Player Linker equipped ────────────────
            var inventory = GetComponent<PlayerInventory>();
            if (
                inventory == null
                || inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.PlayerLinkerItemType
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[PlayerLinker] Server: wielder does not have Player Linker equipped."
                );
                GlobalSessionLock<PlayerLinkerNetworkBridge>.Release();
                return;
            }

            // ── 3. Cache wielder's slot index before any state can change it ──
            _wielderLinkerSlot =
                (!inventory.isLocalPlayer && NetworkServer.active)
                    ? inventory.PlayerInfo.NetworkedEquippedItemIndex
                    : inventory.EquippedItemIndex;

            // ── 4. Validate target — players only ─────────────────────────────
            if (!NetworkServer.spawned.TryGetValue(msg.TargetNetId, out var targetIdentity))
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[PlayerLinker] Server: target netId={msg.TargetNetId} not found."
                );
                GlobalSessionLock<PlayerLinkerNetworkBridge>.Release();
                _wielderLinkerSlot = -1;
                return;
            }

            var targetInfo = targetIdentity.GetComponentInParent<PlayerInfo>();
            if (targetInfo == null)
            {
                IssaPluginPlugin.Log.LogWarning("[PlayerLinker] Server: target is not a player.");
                GlobalSessionLock<PlayerLinkerNetworkBridge>.Release();
                _wielderLinkerSlot = -1;
                return;
            }

            // Prevent self-linking.
            var wielderNetIdentity = GetComponent<NetworkIdentity>();
            if (targetIdentity.netId == wielderNetIdentity.netId)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[PlayerLinker] Server: wielder tried to link to themselves."
                );
                GlobalSessionLock<PlayerLinkerNetworkBridge>.Release();
                _wielderLinkerSlot = -1;
                return;
            }

            // ── 5. Store server session state ─────────────────────────────────
            _serverSessionActive = true;
            _wielderConn = conn;
            _targetConn = targetIdentity.connectionToClient;
            _wielderInfo = GetComponent<PlayerInfo>();
            _targetInfo = targetInfo;

            IssaPluginPlugin.Log.LogInfo(
                $"[PlayerLinker] Server: session started. "
                    + $"wielder={wielderNetIdentity.netId} target={targetIdentity.netId}"
            );

            // ── 6. Broadcast connection to all clients ────────────────────────
            NetworkServer.SendToAll(
                new PlayerLinkerConnectedMessage
                {
                    WielderNetId = wielderNetIdentity.netId,
                    TargetNetId = targetIdentity.netId,
                    Duration = Configuration.PlayerLinkerTetherDuration.Value,
                }
            );

            // ── 7. Start server timeout coroutine ─────────────────────────────
            _serverTimeout = StartCoroutine(ServerTimeoutCoroutine());
        }

        // =====================================================================
        //  Server — position tick relay
        //
        //  IMPORTANT: This method is called via GlobalSessionLock.Holder, NOT via
        //  GetBridge(conn). Using GetBridge(conn) would route the TARGET's ticks
        //  to the target's bridge (which has no session state). The Holder is always
        //  the wielder's bridge, regardless of which player sent the tick.
        // =====================================================================

        public void ServerHandleTick(NetworkConnectionToClient conn, PlayerLinkerTickMessage msg)
        {
            if (!isServer || !_serverSessionActive)
                return;

            if (conn == _wielderConn)
            {
                // Wielder's position → relay to target
                _targetConn?.Send(new PlayerLinkerRelayMessage { OtherPosition = msg.Position });
            }
            else if (conn == _targetConn)
            {
                // Target's position → relay to wielder
                _wielderConn?.Send(new PlayerLinkerRelayMessage { OtherPosition = msg.Position });
            }
        }

        // =====================================================================
        //  Server — session lifecycle
        // =====================================================================

        private IEnumerator ServerTimeoutCoroutine()
        {
            yield return new WaitForSeconds(Configuration.PlayerLinkerTetherDuration.Value);

            if (!_serverSessionActive)
                yield break;

            IssaPluginPlugin.Log.LogInfo("[PlayerLinker] Server: session timed out.");
            ServerEndSession();
        }

        private void ServerEndSession()
        {
            if (!_serverSessionActive)
                return;

            _serverSessionActive = false;

            // Consume item via the slot index cached at session start.
            if (_wielderLinkerSlot >= 0)
            {
                var inventory = GetComponent<PlayerInventory>();
                if (inventory != null)
                {
                    ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);
                    ItemHelper.DecrementAndRemove(inventory, _wielderLinkerSlot);
                    ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                }
            }
            _wielderLinkerSlot = -1;

            NetworkServer.SendToAll(new PlayerLinkerDisconnectedMessage());

            GlobalSessionLock<PlayerLinkerNetworkBridge>.Release();

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            _wielderConn = null;
            _targetConn = null;
            _wielderInfo = null;
            _targetInfo = null;
        }

        // =====================================================================
        //  Client — static message handlers (all clients)
        // =====================================================================

        /// Called on every client when the tether connects.
        public static void HandlePlayerLinkerConnected(PlayerLinkerConnectedMessage msg)
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null)
                return;

            // ── Destroy any stale line renderer from a previous session ────────
            if (s_tetherLine != null)
            {
                Object.Destroy(s_tetherLine.gameObject);
                s_tetherLine = null;
            }

            // ── Store session identity and timing ─────────────────────────────
            s_wielderNetId = msg.WielderNetId;
            s_targetNetId = msg.TargetNetId;
            s_tetherDuration = msg.Duration;
            s_tetherStartTime = Time.time;
            s_tetherActive = true;

            // ── Create tether line renderer (all clients) ─────────────────────
            CreateTetherLine();

            uint localNetId = localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u;
            bool isLocalPlayerTethered =
                localNetId == msg.WielderNetId || localNetId == msg.TargetNetId;

            if (!isLocalPlayerTethered)
                return;

            // ── Determine the other player's netId ────────────────────────────
            uint otherNetId = localNetId == msg.WielderNetId ? msg.TargetNetId : msg.WielderNetId;

            // ── Initialize s_otherPlayerPos from current transform ────────────
            // This prevents a zero-vector force spike on the first coroutine frame
            // before any relay message has arrived.
            var otherTransform = GetTransformByNetId(otherNetId);
            s_otherPlayerPos =
                otherTransform != null ? otherTransform.position : localInfo.transform.position; // safe fallback: no force on frame 1

            // ── Start the spring force coroutine ─────────────────────────────
            var movement = localInfo.Movement;
            if (movement == null)
                return;

            s_forceMovement = movement;
            s_forceCoroutine = movement.StartCoroutine(ForceCoroutine(msg.Duration));

            // ── Reset this bridge's send timer so the first tick fires immediately ──
            localInfo.GetComponent<PlayerLinkerNetworkBridge>()._sendTimer = 0f;

            IssaPluginPlugin.Log.LogInfo(
                $"[PlayerLinker] Client: tether started (local player is "
                    + (localNetId == msg.WielderNetId ? "wielder" : "target")
                    + ")."
            );
        }

        /// Called on the targeted client only when the server relays the other player's position.
        public static void HandlePlayerLinkerRelay(PlayerLinkerRelayMessage msg)
        {
            s_otherPlayerPos = msg.OtherPosition;
        }

        /// Called on every client when the tether ends (timeout).
        public static void HandlePlayerLinkerDisconnected(PlayerLinkerDisconnectedMessage msg)
        {
            ClearClientState();
            IssaPluginPlugin.Log.LogInfo("[PlayerLinker] Client: tether disconnected.");
        }

        /// Called only on the wielder's client when the session lock is held by another player.
        public static void HandlePlayerLinkerBusy(PlayerLinkerBusyMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo(
                "[PlayerLinker] Session busy — another Player Linker is already active."
            );
            PlayerLinkerOverlay.Instance?.ShowBusy();
        }

        // =====================================================================
        //  Spring force coroutine (runs on each tethered player's client)
        // =====================================================================

        private static IEnumerator ForceCoroutine(float maxDuration)
        {
            float elapsed = 0f;

            while (elapsed < maxDuration && s_tetherActive)
            {
                // Re-fetch each iteration for safety (matches BlackHoleGrenade/GravityGun pattern).
                var localInfo = GameManager.LocalPlayerInfo;
                if (localInfo == null)
                    yield break;

                // Golf cart: apply force to cart Rigidbody if seated, otherwise to player.
                // (Same logic as GravityGunNetworkBridge.TetherCoroutine lines 558-563.)
                var seat = localInfo.ActiveGolfCartSeat;
                bool inCart = seat.IsValid();
                Rigidbody rb =
                    inCart && seat.golfCart != null
                        ? seat.golfCart.AsEntity.Rigidbody
                        : localInfo.GetComponentInParent<Rigidbody>();

                if (rb != null)
                {
                    Vector3 toOther = s_otherPlayerPos - rb.position;
                    float dist = toOther.magnitude;
                    float naturalLength = Configuration.PlayerLinkerNaturalLength.Value;

                    if (dist > naturalLength && dist > 0.05f)
                    {
                        Vector3 dir = toOther / dist;
                        float stretch = dist - naturalLength;
                        float targetSpeed = stretch * Configuration.PlayerLinkerSpringForce.Value;
                        float currentComp = Vector3.Dot(rb.linearVelocity, dir);
                        float deficit = Mathf.Min(
                            targetSpeed - currentComp,
                            Configuration.PlayerLinkerMaxPullSpeed.Value
                        );

                        if (deficit > 0f)
                            rb.AddForce(dir * deficit, ForceMode.VelocityChange);
                    }
                }

                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            // Do NOT clear s_tetherActive here — wait for PlayerLinkerDisconnectedMessage
            // from the server so all clients clean up consistently. (GravityGun pattern.)
        }

        // =====================================================================
        //  VFX — tether line renderer
        // =====================================================================

        private static void CreateTetherLine()
        {
            var lineGo = new GameObject("PlayerLinkerTetherLine");
            s_tetherLine = lineGo.AddComponent<LineRenderer>();
            s_tetherLine.positionCount = 2;
            s_tetherLine.startWidth = 0.12f;
            s_tetherLine.endWidth = 0.12f;
            s_tetherLine.material = new Material(Shader.Find("Sprites/Default"));
            s_tetherLine.startColor = new Color(1f, 0.55f, 0.1f, 0.9f); // orange
            s_tetherLine.endColor = new Color(1f, 0.2f, 0.1f, 0.9f); // red-orange
            s_tetherLine.useWorldSpace = true;
            Object.DontDestroyOnLoad(lineGo);
        }

        private void UpdateTetherLine()
        {
            if (s_tetherLine == null)
                return;

            Transform wielderT = GetTransformByNetId(s_wielderNetId);
            Transform targetT = GetTransformByNetId(s_targetNetId);

            if (wielderT != null && targetT != null)
            {
                s_tetherLine.SetPosition(0, wielderT.position + Vector3.up * 1f);
                s_tetherLine.SetPosition(1, targetT.position + Vector3.up * 1f);
            }
        }

        /// Returns the transform for any networked player by netId.
        private static Transform GetTransformByNetId(uint netId)
        {
            if (netId == 0u)
                return null;

            // Check local player first.
            var local = GameManager.LocalPlayerInfo;
            if (local != null)
            {
                var localNetId = local.GetComponent<NetworkIdentity>()?.netId ?? 0u;
                if (localNetId == netId)
                    return local.transform;
            }

            // Check remote players list.
            var remotePlayers = GameManager.RemotePlayers;
            if (remotePlayers != null)
            {
                foreach (var p in remotePlayers)
                {
                    if (p == null)
                        continue;
                    var pNetId = p.GetComponent<NetworkIdentity>()?.netId ?? 0u;
                    if (pNetId == netId)
                        return p.transform;
                }
            }

            // Fallback: any spawned networked object.
            if (NetworkClient.spawned.TryGetValue(netId, out var identity) && identity != null)
                return identity.transform;

            return null;
        }

        // =====================================================================
        //  Hole cleanup
        // =====================================================================

        public override void ServerHoleCleanup()
        {
            if (_serverSessionActive)
                ServerEndSession();

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }
        }

        public override void ClientHoleCleanup()
        {
            ClearClientState();

            // Reset this owned bridge's send timer.
            _sendTimer = 0f;
        }

        // =====================================================================
        //  Shared cleanup helper
        // =====================================================================

        private static void ClearClientState()
        {
            // Stop the spring coroutine before clearing the movement reference.
            if (s_forceCoroutine != null && s_forceMovement != null)
                s_forceMovement.StopCoroutine(s_forceCoroutine);

            if (s_tetherLine != null)
            {
                Object.Destroy(s_tetherLine.gameObject);
                s_tetherLine = null;
            }

            s_forceCoroutine = null;
            s_forceMovement = null;
            s_tetherActive = false;
            s_wielderNetId = 0;
            s_targetNetId = 0;
            s_otherPlayerPos = Vector3.zero;
            s_tetherDuration = 0f;
            s_tetherStartTime = 0f;
        }
    }
}
