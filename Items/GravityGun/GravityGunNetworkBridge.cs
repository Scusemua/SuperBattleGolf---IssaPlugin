using System.Collections;
using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// The Gravity Gun fires an electric tether at a locked-on opponent.
    /// While the session is active, the wielder's aim direction is forwarded to
    /// the target at ~20 Hz so the target's spring coroutine pulls them toward
    /// <c>wielderPos + aimDir * tetherRadius</c>, creating a "ball on a string"
    /// orbit effect.  The target flies off at their accumulated velocity when the
    /// wielder releases or the timer expires.
    ///
    /// Global one-at-a-time lock via GlobalSessionLock&lt;GravityGunNetworkBridge&gt;
    /// (same pattern as AC130/Donut).
    /// </summary>
    public class GravityGunNetworkBridge : NetworkBridgeBase
    {
        // =====================================================================
        //  Server-side per-instance state
        // =====================================================================

        private bool _serverSessionActive;
        private NetworkConnectionToClient _targetConn;
        private PlayerInfo _targetInfo;
        private Coroutine _serverTimeout;
        private int _wielderGravityGunSlot = -1; // cached at session start; used by ServerEndSession

        // Set when the target is an unoccupied golf cart — server applies force directly.
        private bool _targetIsEmptyCart;
        private GameObject _targetCartGo;

        // =====================================================================
        //  Owning-client per-instance state
        // =====================================================================

        /// True while the local player has an active Gravity Gun session as wielder.
        public bool LocalSessionActive { get; private set; }

        private float _sendTimer;

        // =====================================================================
        //  Static shared state (safe: only one session at a time via lock)
        // =====================================================================

        // VFX
        private static LineRenderer s_tetherLine;

        // Session identity (used for line renderer transforms and wielder lookup)
        private static uint s_wielderNetId;
        private static uint s_targetNetId;

        // Local-only VFX parented to the target for the duration of the session.
        private static GameObject s_tetherVfxInstance;

        // Target-client spring coroutine state
        private static bool s_tetherActive;
        private static Vector3 s_wielderPos;
        private static Vector3 s_aimDir;
        private static float s_tetherRadius;
        private static Coroutine s_tetherCoroutine;
        private static PlayerMovement s_tetherMovement;

        // =====================================================================
        //  Unity lifecycle
        // =====================================================================

        private void Update()
        {
            // Update the tether line for the wielder AND the target.
            if (isOwned && (LocalSessionActive || s_tetherActive))
                UpdateTetherLine();

            // All remaining logic is wielder-only.
            if (!isOwned || !LocalSessionActive)
                return;

            // ── Rate-limited aim tick to server (20 Hz) ───────────────────────
            _sendTimer -= Time.deltaTime;
            if (_sendTimer <= 0f)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    NetworkClient.Send(
                        new GravityGunAimTickMessage
                        {
                            WielderPos = transform.position,
                            AimDir = cam.transform.forward,
                        }
                    );
                }
                _sendTimer = ModConfig.GravityGun.InputSendInterval.Value;
            }
        }

        // =====================================================================
        //  Client — initiating the lock-on
        // =====================================================================

        /// Called from GravityGunItemDefinition.OnUse → GetComponent<GravityGunNetworkBridge>().
        public void ClientUse()
        {
            if (!isOwned)
                return;

            // Delegate target selection to GravityGunOverlay, which already performs
            // the player + cart cone scan every frame for the lock-on reticle.
            var bestTarget = GravityGunOverlay.Instance?.BestTargetIdentity;

            if (bestTarget == null)
            {
                IssaPluginPlugin.Log.LogInfo("[GravityGun] ClientUse: no valid target in cone.");
                return;
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[GravityGun] ClientUse: locking onto netId={bestTarget.netId}."
            );
            NetworkClient.Send(new GravityGunLockOnMessage { TargetNetId = bestTarget.netId });
        }

        // =====================================================================
        //  Server — handling lock-on request
        // =====================================================================

        public void ServerHandleLockOn(NetworkConnectionToClient conn, GravityGunLockOnMessage msg)
        {
            if (!isServer)
                return;

            // ── 1. Acquire global session lock ────────────────────────────────
            if (!GlobalSessionLock<GravityGunNetworkBridge>.TryAcquire(this))
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[GravityGun] Server: session busy — rejecting lock-on."
                );
                connectionToClient?.Send(new GravityGunBusyMessage());
                return;
            }

            // ── 2. Validate wielder has Gravity Gun equipped ──────────────────────
            var inventory = GetComponent<PlayerInventory>();
            if (
                inventory == null
                || inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.GravityGunItemType
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[GravityGun] Server: wielder does not have Gravity Gun equipped."
                );
                GlobalSessionLock<GravityGunNetworkBridge>.Release();
                return;
            }

            // ── 3. Cache wielder's slot index before any state can change it ─────
            _wielderGravityGunSlot =
                (!inventory.isLocalPlayer && NetworkServer.active)
                    ? inventory.PlayerInfo.NetworkedEquippedItemIndex
                    : inventory.EquippedItemIndex;

            // ── 4. Validate target — player or golf cart ──────────────────────
            if (!NetworkServer.spawned.TryGetValue(msg.TargetNetId, out var targetIdentity))
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[GravityGun] Server: target netId={msg.TargetNetId} not found."
                );
                GlobalSessionLock<GravityGunNetworkBridge>.Release();
                _wielderGravityGunSlot = -1;
                return;
            }

            // Resolve the target: player directly, or cart → driver (if occupied).
            var targetInfo = targetIdentity.GetComponentInParent<PlayerInfo>();
            var targetCart = targetIdentity.GetComponent<GolfCartInfo>();
            uint broadcastTargetNetId = msg.TargetNetId;
            NetworkConnectionToClient resolvedConn;

            if (targetInfo != null)
            {
                // Direct player target.
                resolvedConn = targetIdentity.connectionToClient;
            }
            else if (targetCart != null)
            {
                if (targetCart.TryGetDriver(out var driver) && driver != null)
                {
                    // Occupied cart → redirect to the driver (existing TetherCoroutine handles
                    // force-to-cart naturally since the driver is seated).
                    targetInfo = driver;
                    resolvedConn = driver.netIdentity.connectionToClient;
                    broadcastTargetNetId = driver.netIdentity.netId;
                    IssaPluginPlugin.Log.LogInfo(
                        "[GravityGun] Server: cart target has driver — redirecting to driver."
                    );
                }
                else
                {
                    // Empty cart — server will apply spring force directly each aim tick.
                    resolvedConn = null;
                    IssaPluginPlugin.Log.LogInfo(
                        "[GravityGun] Server: empty cart target — server will apply force."
                    );
                }
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[GravityGun] Server: target is neither a player nor a golf cart."
                );
                GlobalSessionLock<GravityGunNetworkBridge>.Release();
                _wielderGravityGunSlot = -1;
                return;
            }

            // ── 4d. Electromagnetic shield check — reject grab if target is shielded ──
            if (targetInfo != null && targetInfo.IsElectromagnetShieldActive)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[GravityGun] Server: target has electromagnetic shield — rejecting lock-on."
                );
                GlobalSessionLock<GravityGunNetworkBridge>.Release();
                _wielderGravityGunSlot = -1;
                targetInfo.PlayElectromagnetShieldHitForAllClients(
                    (targetInfo.transform.position - transform.position).normalized
                );
                return;
            }

            // ── 5. Store server session state ─────────────────────────────────
            _serverSessionActive = true;
            _targetConn = resolvedConn;
            _targetInfo = targetInfo; // null for empty carts
            _targetIsEmptyCart = targetCart != null && resolvedConn == null;
            _targetCartGo = _targetIsEmptyCart ? targetCart.gameObject : null;

            var wielderNetId = GetComponent<NetworkIdentity>().netId;

            IssaPluginPlugin.Log.LogInfo(
                $"[GravityGun] Server: session started. "
                    + $"wielder={wielderNetId} target={broadcastTargetNetId}"
                    + (_targetIsEmptyCart ? " (empty cart)" : "")
            );

            // ── 6. Broadcast connection to all clients ────────────────────────
            NetworkServer.SendToAll(
                new GravityGunConnectedMessage
                {
                    WielderNetId = wielderNetId,
                    TargetNetId = broadcastTargetNetId,
                    TetherRadius = ModConfig.GravityGun.TetherRadius.Value,
                    Duration = ModConfig.GravityGun.TetherDuration.Value,
                }
            );

            // ── 7. Start server timeout coroutine ─────────────────────────────
            _serverTimeout = StartCoroutine(ServerTimeoutCoroutine());
        }

        // =====================================================================
        //  Server — aim tick forwarding
        // =====================================================================

        public void ServerHandleAimTick(
            NetworkConnectionToClient conn,
            GravityGunAimTickMessage msg
        )
        {
            if (!isServer || !_serverSessionActive)
                return;

            if (_targetIsEmptyCart && _targetCartGo != null)
            {
                // Empty cart: server holds authority, apply spring force directly.
                var rb = _targetCartGo.GetComponent<Rigidbody>();
                if (rb != null && msg.AimDir.sqrMagnitude > 0.0001f)
                {
                    float tetherRadius = ModConfig.GravityGun.TetherRadius.Value;
                    Vector3 desiredPos = msg.WielderPos + msg.AimDir.normalized * tetherRadius;
                    Vector3 toDesired = desiredPos - rb.position;
                    float dist = toDesired.magnitude;

                    if (dist > 0.05f)
                    {
                        Vector3 dir = toDesired / dist;
                        float targetSpeed = dist * ModConfig.GravityGun.SpringForce.Value;
                        float currentSpeed = Vector3.Dot(rb.linearVelocity, dir);
                        float deficit = Mathf.Min(
                            targetSpeed - currentSpeed,
                            ModConfig.GravityGun.MaxPullSpeed.Value
                        );
                        if (deficit > 0f)
                            rb.AddForce(dir * deficit, ForceMode.VelocityChange);
                    }
                }
            }
            else
            {
                // Player target (or occupied cart resolved to driver): forward tick.
                _targetConn?.Send(
                    new GravityGunTetherTickMessage
                    {
                        WielderPos = msg.WielderPos,
                        AimDir = msg.AimDir,
                    }
                );
            }
        }

        // =====================================================================
        //  Server — manual release
        // =====================================================================

        public void ServerHandleRelease(NetworkConnectionToClient conn)
        {
            if (!isServer || !_serverSessionActive)
                return;

            IssaPluginPlugin.Log.LogInfo("[GravityGun] Server: wielder released tether.");
            ServerEndSession();
        }

        // =====================================================================
        //  Server — session lifecycle
        // =====================================================================

        private IEnumerator ServerTimeoutCoroutine()
        {
            yield return new WaitForSeconds(ModConfig.GravityGun.TetherDuration.Value);

            if (!_serverSessionActive)
                yield break; // already ended by manual release

            IssaPluginPlugin.Log.LogInfo("[GravityGun] Server: session timed out.");
            ServerEndSession();
        }

        private void ServerEndSession()
        {
            if (!_serverSessionActive)
                return;

            _serverSessionActive = false;

            // Consume using the slot cached at session start — EquippedItemIndex
            // may have shifted by the time the tether ends (e.g. player pressed the
            // release key, which the base game may also process as an item-use input).
            if (_wielderGravityGunSlot >= 0)
            {
                var inventory = GetComponent<PlayerInventory>();
                if (inventory != null)
                {
                    ItemHelper.SetCurrentItemUse(inventory, ItemUseType.Regular);
                    ItemHelper.DecrementAndRemove(inventory, _wielderGravityGunSlot);
                    ItemHelper.SetCurrentItemUse(inventory, ItemUseType.None);
                }
            }
            _wielderGravityGunSlot = -1;

            var wielderNetId = GetComponent<NetworkIdentity>().netId;
            NetworkServer.SendToAll(
                new GravityGunDisconnectedMessage { WielderNetId = wielderNetId }
            );

            GlobalSessionLock<GravityGunNetworkBridge>.Release();

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            _targetConn = null;
            _targetInfo = null;
            _targetIsEmptyCart = false;
            _targetCartGo = null;
        }

        // =====================================================================
        //  Client — static message handlers (all clients)
        // =====================================================================

        /// Called on every client when the tether connects.
        public static void HandleGravityGunConnected(GravityGunConnectedMessage msg)
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

            // ── Destroy any stale VFX instance from a previous session ────────
            if (s_tetherVfxInstance != null)
            {
                Object.Destroy(s_tetherVfxInstance);
                s_tetherVfxInstance = null;
            }

            // ── Create new tether line renderer (local-only, all clients) ──────
            var lineGo = CreateTetherLine();

            // ── Store static session identity ─────────────────────────────────
            s_wielderNetId = msg.WielderNetId;
            s_targetNetId = msg.TargetNetId;

            // ── Spawn tether VFX parented to the target (all clients) ─────────
            if (AssetLoader.GravityGunTetherVfxPrefab != null)
            {
                var targetTransform = GetTransformByNetId(msg.TargetNetId);
                if (targetTransform != null)
                {
                    s_tetherVfxInstance = Object.Instantiate(
                        AssetLoader.GravityGunTetherVfxPrefab,
                        targetTransform.position,
                        Quaternion.identity,
                        targetTransform
                    );
                }
            }

            uint localNetId = localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u;

            // ── Wielder client: start instance session ────────────────────────
            if (localNetId == msg.WielderNetId)
            {
                var bridge = localInfo.GetComponent<GravityGunNetworkBridge>();
                if (bridge != null)
                {
                    bridge.OnLocalSessionStarted();
                }
            }

            // ── Target client: start spring coroutine + initial stagger ───────
            if (localNetId == msg.TargetNetId)
            {
                var movement = localInfo.Movement;
                if (movement == null)
                    return;

                s_tetherActive = true;
                s_tetherRadius = msg.TetherRadius;
                s_wielderPos = localInfo.transform.position; // initial approximation
                s_aimDir = Vector3.zero;
                s_tetherMovement = movement;
                s_tetherCoroutine = movement.StartCoroutine(TetherCoroutine(msg.Duration));

                // Stagger the target for gameplay feel and kill attribution.
                PlayerInfo wielderInfo = null;
                var localNetIdentity = localInfo.GetComponent<NetworkIdentity>();
                foreach (var entry in NetworkClient.spawned)
                {
                    if (entry.Key == msg.WielderNetId)
                    {
                        wielderInfo = entry.Value?.GetComponentInParent<PlayerInfo>();
                        break;
                    }
                }

                if (wielderInfo != null)
                {
                    var useId = new ItemUseId(
                        wielderInfo.PlayerId.Guid,
                        GravityGunItem.NextUseIndex(),
                        ItemType.RocketLauncher,
                        false
                    );
                    bool _;
                    movement.TryKnockOut(
                        wielderInfo,
                        KnockoutType.Rocket,
                        false,
                        movement.transform.InverseTransformPoint(wielderInfo.transform.position),
                        Vector3.Distance(
                            localInfo.transform.position,
                            wielderInfo.transform.position
                        ),
                        Vector3.zero, // no extra velocity on connect
                        ElectromagnetShieldHitBlockType.FullyBlocked,
                        useId,
                        false,
                        true,
                        out _,
                        out _
                    );
                }

                IssaPluginPlugin.Log.LogInfo(
                    "[GravityGun] Client: tether coroutine started (local player is target)."
                );
            }
        }

        /// Called on the target client only when the server forwards an aim tick.
        public static void HandleGravityGunTetherTick(GravityGunTetherTickMessage msg)
        {
            s_wielderPos = msg.WielderPos;
            s_aimDir = msg.AimDir;
        }

        /// Called on every client when the tether ends (release or timeout).
        public static void HandleGravityGunDisconnected(GravityGunDisconnectedMessage msg)
        {
            // ── Stop the target's spring coroutine (null-guarded) ─────────────
            if (s_tetherCoroutine != null && s_tetherMovement != null)
                s_tetherMovement.StopCoroutine(s_tetherCoroutine);
            s_tetherCoroutine = null;
            s_tetherMovement = null;
            s_tetherActive = false;

            // ── Destroy the tether line renderer ─────────────────────────────
            if (s_tetherLine != null)
            {
                Object.Destroy(s_tetherLine.gameObject);
                s_tetherLine = null;
            }

            // ── Destroy the tether VFX ────────────────────────────────────────
            if (s_tetherVfxInstance != null)
            {
                Object.Destroy(s_tetherVfxInstance);
                s_tetherVfxInstance = null;
            }

            // ── Reset wielder's instance session state ────────────────────────
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo != null)
            {
                uint localNetId = localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u;
                if (localNetId == msg.WielderNetId)
                    localInfo.GetComponent<GravityGunNetworkBridge>()?.OnLocalSessionEnded();
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[GravityGun] Client: session disconnected (wielderNetId={msg.WielderNetId})."
            );
        }

        /// Called only on the wielder's client when the session lock is held by another player.
        public static void HandleGravityGunBusy(GravityGunBusyMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo(
                "[GravityGun] Session busy — another Gravity Gun is already active."
            );
            GravityGunOverlay.Instance?.ShowBusy();
        }

        // =====================================================================
        //  Target client — tether spring coroutine
        // =====================================================================

        private static IEnumerator TetherCoroutine(float maxDuration)
        {
            float elapsed = 0f;

            while (elapsed < maxDuration && s_tetherActive)
            {
                // Re-fetch each iteration for safety (matches BlackHoleGrenade pattern).
                var localInfo = GameManager.LocalPlayerInfo;
                if (localInfo == null)
                    yield break;

                // Golf cart handling: if the target is in a cart, apply force to the
                // cart Rigidbody (cart driver has client authority over it).
                // Same logic as BlackHoleGrenade's TetherCoroutine.
                var seat = localInfo.ActiveGolfCartSeat;
                bool inCart = seat.IsValid();
                Rigidbody rb =
                    inCart && seat.golfCart != null
                        ? seat.golfCart.AsEntity.Rigidbody
                        : localInfo.GetComponentInParent<Rigidbody>();

                if (rb != null && s_aimDir.sqrMagnitude > 0.0001f)
                {
                    Vector3 desiredPos = s_wielderPos + s_aimDir.normalized * s_tetherRadius;
                    Vector3 toDesired = desiredPos - rb.position;
                    float dist = toDesired.magnitude;

                    if (dist > 0.05f)
                    {
                        Vector3 dir = toDesired / dist;

                        // Proportional spring: the further from desired, the stronger the pull.
                        float springForce = ModConfig.GravityGun.SpringForce.Value;
                        float maxPullSpeed = ModConfig.GravityGun.MaxPullSpeed.Value;

                        float targetSpeed = dist * springForce;
                        float currentSpeed = Vector3.Dot(rb.linearVelocity, dir);
                        float deficit = Mathf.Min(targetSpeed - currentSpeed, maxPullSpeed);

                        if (deficit > 0f)
                            rb.AddForce(dir * deficit, ForceMode.VelocityChange);
                    }
                }

                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            // Coroutine exits naturally when maxDuration elapses or s_tetherActive is cleared.
            // GravityGunDisconnectedMessage from the server will arrive shortly after expiry
            // and call HandleGravityGunDisconnected for final cleanup.
            // Do NOT set s_tetherActive = false here — wait for the server message.
        }

        // =====================================================================
        //  VFX update — line renderer
        // =====================================================================

        private static GameObject CreateTetherLine()
        {
            var lineGo = new GameObject("GravityGunTetherLine");
            s_tetherLine = lineGo.AddComponent<LineRenderer>();
            s_tetherLine.positionCount = 2;
            s_tetherLine.startWidth = 0.10f;
            s_tetherLine.endWidth = 0.06f;
            s_tetherLine.material = new Material(Shader.Find("Sprites/Default"));
            s_tetherLine.startColor = new Color(0.3f, 0.8f, 1f, 0.9f); // electric blue
            s_tetherLine.endColor = new Color(1f, 1f, 0.5f, 0.5f); // yellow-white
            s_tetherLine.useWorldSpace = true;
            Object.DontDestroyOnLoad(lineGo);

            return lineGo;
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

        /// Returns the transform for any networked object (player or golf cart) by netId.
        private static Transform GetTransformByNetId(uint netId)
        {
            if (netId == 0u)
                return null;

            // Check local player first (most common case for wielder/target).
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

            // Fallback: any spawned networked object (covers golf carts).
            if (NetworkClient.spawned.TryGetValue(netId, out var identity) && identity != null)
                return identity.transform;

            return null;
        }

        // =====================================================================
        //  Private instance helpers
        // =====================================================================

        private void OnLocalSessionStarted()
        {
            LocalSessionActive = true;
            _sendTimer = 0f; // send first tick immediately
        }

        private void OnLocalSessionEnded()
        {
            LocalSessionActive = false;
            _sendTimer = 0f;
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
            // Stop coroutine before clearing movement reference.
            if (s_tetherCoroutine != null && s_tetherMovement != null)
                s_tetherMovement.StopCoroutine(s_tetherCoroutine);

            if (s_tetherLine != null)
            {
                Object.Destroy(s_tetherLine.gameObject);
                s_tetherLine = null;
            }

            if (s_tetherVfxInstance != null)
            {
                Object.Destroy(s_tetherVfxInstance);
                s_tetherVfxInstance = null;
            }

            // Reset ALL static state to prevent stale data across holes.
            s_tetherCoroutine = null;
            s_tetherMovement = null;
            s_tetherActive = false;
            s_wielderNetId = 0;
            s_targetNetId = 0;
            s_wielderPos = Vector3.zero;
            s_aimDir = Vector3.zero;
            s_tetherRadius = 0f;

            // Reset owning-client instance state.
            OnLocalSessionEnded();
        }
    }
}
