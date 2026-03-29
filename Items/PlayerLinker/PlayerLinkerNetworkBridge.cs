using System.Collections;
using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// The Player Linker fires a rocket that spawns above a target player and
    /// launches straight up at a configurable speed.  The target is tethered to
    /// the rocket by a spring force for the duration of the flight.  When the
    /// timer expires the server triggers a physics explosion at the rocket's final
    /// position and broadcasts the event so every client can apply local force and
    /// destroy their VFX.
    ///
    /// Because the rocket travels at constant velocity upward, clients do NOT need
    /// per-tick position updates.  They compute the rocket's world position as:
    ///   rocketPos = s_rocketStartPos + Vector3.up * s_rocketSpeed * elapsed
    /// This eliminates the Tick/Relay message pair entirely.
    ///
    /// Global one-at-a-time lock via GlobalSessionLock&lt;PlayerLinkerNetworkBridge&gt;.
    /// All session state lives on the wielder's bridge; the target's bridge has none.
    /// </summary>
    public class PlayerLinkerNetworkBridge : NetworkBridgeBase
    {
        // =====================================================================
        //  Server-side per-instance state (wielder's bridge only)
        // =====================================================================

        private bool _serverSessionActive;
        private NetworkConnectionToClient _wielderConn;
        private PlayerInfo _targetInfo;
        private Coroutine _serverTimeout;
        private int _wielderLinkerSlot = -1; // cached at session start; used by ServerEndSession

        // Rocket tracked purely by math on the server (no spawned object needed).
        private Vector3 _serverRocketStartPos;
        private float _serverRocketSpeed;

        // =====================================================================
        //  Static shared state (safe: one session at a time via GlobalSessionLock)
        // =====================================================================

        private static bool s_tetherActive;
        private static uint s_targetNetId;

        // Deterministic rocket state — set once on HandleConnected, never updated.
        private static Vector3 s_rocketStartPos;
        private static float s_rocketSpeed;
        private static float s_rocketStartTime;
        private static float s_rocketDuration;

        // Force coroutine (target client only)
        private static Coroutine s_forceCoroutine;
        private static PlayerMovement s_forceMovement;

        // Visual objects (all clients)
        private static LineRenderer s_tetherLine;
        private static GameObject s_rocketVfxInstance;

        // =====================================================================
        //  Public static accessors (read by PlayerLinkerOverlay)
        // =====================================================================

        public static bool TetherActive => s_tetherActive;
        public static uint TargetNetId => s_targetNetId;
        public static float TetherDuration => s_rocketDuration;
        public static float TetherStartTime => s_rocketStartTime;

        // =====================================================================
        //  Unity lifecycle
        // =====================================================================

        private void Update()
        {
            // Only the local player's bridge drives per-frame visual updates.
            if (!isOwned || !s_tetherActive)
                return;

            // Move the rocket VFX to its current computed position, locking rotation.
            if (s_rocketVfxInstance != null)
            {
                float elapsed = Time.time - s_rocketStartTime;
                Vector3 rocketPos = s_rocketStartPos + Vector3.up * s_rocketSpeed * elapsed;
                s_rocketVfxInstance.transform.SetPositionAndRotation(
                    rocketPos,
                    Quaternion.identity
                );
            }

            // Update tether line: target feet → rocket.
            if (s_tetherLine != null)
                UpdateTetherLine();
        }

        // =====================================================================
        //  Client — initiating the lock-on
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
                $"[PlayerLinker] ClientUse: targeting netId={bestTarget.netId}."
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

            // ── 4. Validate target — players only, no self-link ───────────────
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

            var wielderNetId = GetComponent<NetworkIdentity>().netId;

            // ── 5. Compute rocket spawn position (just above target's head) ───
            float rocketSpeed = Configuration.PlayerLinkerRocketSpeed.Value;
            float duration = Configuration.PlayerLinkerTetherDuration.Value;
            Vector3 rocketStart = targetInfo.transform.position + Vector3.up * 1.5f;

            // ── 6. Store server session state ─────────────────────────────────
            _serverSessionActive = true;
            _wielderConn = conn;
            _targetInfo = targetInfo;
            _serverRocketStartPos = rocketStart;
            _serverRocketSpeed = rocketSpeed;

            IssaPluginPlugin.Log.LogInfo(
                $"[PlayerLinker] Server: session started. "
                    + $"wielder={wielderNetId} target={targetIdentity.netId}"
            );

            // ── 7. Broadcast connection to all clients ────────────────────────
            NetworkServer.SendToAll(
                new PlayerLinkerConnectedMessage
                {
                    TargetNetId = targetIdentity.netId,
                    RocketStartPos = rocketStart,
                    RocketSpeed = rocketSpeed,
                    Duration = duration,
                }
            );

            // ── 8. Start server timeout / explosion coroutine ─────────────────
            _serverTimeout = StartCoroutine(ServerRocketCoroutine(duration));
        }

        // =====================================================================
        //  Server — rocket flight and explosion
        // =====================================================================

        private IEnumerator ServerRocketCoroutine(float duration)
        {
            yield return new WaitForSeconds(duration);

            if (!_serverSessionActive)
                yield break;

            IssaPluginPlugin.Log.LogInfo("[PlayerLinker] Server: rocket exploding.");

            // Final rocket position — same deterministic formula as clients.
            Vector3 explosionPos =
                _serverRocketStartPos + Vector3.up * _serverRocketSpeed * duration;

            float explosionForce = Configuration.PlayerLinkerExplosionForce.Value;
            float explosionRadius = Configuration.PlayerLinkerExplosionRadius.Value;

            // Apply explosion to non-player Rigidbodies (golf balls, empty carts, etc.)
            // Player physics are client-authoritative; clients handle their own forces via
            // the PlayerLinkerDisconnectedMessage.
            var colliders = Physics.OverlapSphere(explosionPos, explosionRadius);
            foreach (var col in colliders)
            {
                var rb = col.attachedRigidbody;
                if (rb == null || rb.isKinematic)
                    continue;

                // Skip anything with a PlayerInfo — clients own player physics.
                if (col.GetComponentInParent<PlayerInfo>() != null)
                    continue;

                rb.AddExplosionForce(
                    explosionForce,
                    explosionPos,
                    explosionRadius,
                    0.5f,
                    ForceMode.VelocityChange
                );
            }

            ServerEndSession(explosionPos, explosionForce, explosionRadius);
        }

        // =====================================================================
        //  Server — session lifecycle
        // =====================================================================

        private void ServerEndSession(
            Vector3 explosionPos,
            float explosionForce,
            float explosionRadius
        )
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

            NetworkServer.SendToAll(
                new PlayerLinkerDisconnectedMessage
                {
                    ExplosionPosition = explosionPos,
                    ExplosionForce = explosionForce,
                    ExplosionRadius = explosionRadius,
                }
            );

            GlobalSessionLock<PlayerLinkerNetworkBridge>.Release();

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            _wielderConn = null;
            _targetInfo = null;
        }

        // =====================================================================
        //  Client — static message handlers (all clients)
        // =====================================================================

        /// Called on every client when the rocket attaches to the target.
        public static void HandlePlayerLinkerConnected(PlayerLinkerConnectedMessage msg)
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null)
                return;

            // ── Destroy stale VFX from a previous session ─────────────────────
            CleanupVfx();

            // ── Store deterministic rocket state ──────────────────────────────
            s_targetNetId = msg.TargetNetId;
            s_rocketStartPos = msg.RocketStartPos;
            s_rocketSpeed = msg.RocketSpeed;
            s_rocketDuration = msg.Duration;
            s_rocketStartTime = Time.time;
            s_tetherActive = true;

            // ── Create tether line (target → rocket) ──────────────────────────
            CreateTetherLine();

            // ── Spawn rocket VFX at start position ────────────────────────────
            if (AssetLoader.PlayerLinkerRocketPrefab != null)
            {
                s_rocketVfxInstance = Object.Instantiate(
                    AssetLoader.PlayerLinkerRocketPrefab,
                    msg.RocketStartPos,
                    Quaternion.identity
                );
                // Make all Rigidbodies kinematic — we drive position/rotation manually.
                foreach (var rb in s_rocketVfxInstance.GetComponentsInChildren<Rigidbody>())
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
                Object.DontDestroyOnLoad(s_rocketVfxInstance);
            }

            // ── If local player is the target, start spring force coroutine ───
            uint localNetId = localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u;
            if (localNetId != msg.TargetNetId)
                return;

            var movement = localInfo.Movement;
            if (movement == null)
                return;

            s_forceMovement = movement;
            s_forceCoroutine = movement.StartCoroutine(ForceCoroutine(msg.Duration));

            IssaPluginPlugin.Log.LogInfo(
                "[PlayerLinker] Client: tether started (local player is target)."
            );
        }

        /// Called on every client when the rocket explodes.
        public static void HandlePlayerLinkerDisconnected(PlayerLinkerDisconnectedMessage msg)
        {
            // Apply explosion force to the local player if within radius.
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo != null)
            {
                var seat = localInfo.ActiveGolfCartSeat;
                Rigidbody rb =
                    seat.IsValid() && seat.golfCart != null
                        ? seat.golfCart.AsEntity.Rigidbody
                        : localInfo.GetComponentInParent<Rigidbody>();

                if (rb != null)
                {
                    float dist = Vector3.Distance(rb.position, msg.ExplosionPosition);
                    if (dist < msg.ExplosionRadius)
                    {
                        rb.AddExplosionForce(
                            msg.ExplosionForce,
                            msg.ExplosionPosition,
                            msg.ExplosionRadius,
                            0.5f,
                            ForceMode.VelocityChange
                        );
                    }
                }
            }

            ClearClientState();
            IssaPluginPlugin.Log.LogInfo(
                $"[PlayerLinker] Client: rocket exploded at {msg.ExplosionPosition}."
            );
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
        //  Spring force coroutine (target client only)
        // =====================================================================

        private static IEnumerator ForceCoroutine(float maxDuration)
        {
            float elapsed = 0f;

            while (elapsed < maxDuration && s_tetherActive)
            {
                var localInfo = GameManager.LocalPlayerInfo;
                if (localInfo == null)
                    yield break;

                // Apply force to cart Rigidbody if seated (GravityGun / BlackHoleGrenade pattern).
                var seat = localInfo.ActiveGolfCartSeat;
                Rigidbody rb =
                    seat.IsValid() && seat.golfCart != null
                        ? seat.golfCart.AsEntity.Rigidbody
                        : localInfo.GetComponentInParent<Rigidbody>();

                if (rb != null)
                {
                    // Deterministic rocket position — no network messages needed.
                    Vector3 rocketPos = s_rocketStartPos + Vector3.up * s_rocketSpeed * elapsed;

                    Vector3 toRocket = rocketPos - rb.position;
                    float dist = toRocket.magnitude;
                    float natural = Configuration.PlayerLinkerNaturalLength.Value;

                    if (dist > natural && dist > 0.05f)
                    {
                        Vector3 dir = toRocket / dist;
                        float stretch = dist - natural;
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
            // Do NOT clear s_tetherActive here — wait for PlayerLinkerDisconnectedMessage.
        }

        // =====================================================================
        //  VFX — tether line and rocket model
        // =====================================================================

        private static void CreateTetherLine()
        {
            var lineGo = new GameObject("PlayerLinkerTetherLine");
            s_tetherLine = lineGo.AddComponent<LineRenderer>();
            s_tetherLine.positionCount = 2;
            s_tetherLine.startWidth = 0.12f;
            s_tetherLine.endWidth = 0.06f;
            s_tetherLine.material = new Material(Shader.Find("Sprites/Default"));
            s_tetherLine.startColor = new Color(1f, 0.55f, 0.1f, 0.9f); // orange (target end)
            s_tetherLine.endColor = new Color(1f, 0.2f, 0.1f, 0.9f); // red (rocket end)
            s_tetherLine.useWorldSpace = true;
            Object.DontDestroyOnLoad(lineGo);
        }

        private void UpdateTetherLine()
        {
            if (s_tetherLine == null)
                return;

            Transform targetT = GetTransformByNetId(s_targetNetId);
            float elapsed = Time.time - s_rocketStartTime;
            Vector3 rocketPos = s_rocketStartPos + Vector3.up * s_rocketSpeed * elapsed;

            if (targetT != null)
            {
                s_tetherLine.SetPosition(0, targetT.position + Vector3.up * 1f);
                s_tetherLine.SetPosition(1, rocketPos);
            }
        }

        /// Returns the transform for any networked player by netId.
        private static Transform GetTransformByNetId(uint netId)
        {
            if (netId == 0u)
                return null;

            var local = GameManager.LocalPlayerInfo;
            if (local != null)
            {
                var localNetId = local.GetComponent<NetworkIdentity>()?.netId ?? 0u;
                if (localNetId == netId)
                    return local.transform;
            }

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
            {
                // Compute final rocket pos for the cleanup explosion.
                float elapsed = Time.time - s_rocketStartTime; // approximate
                Vector3 explosionPos =
                    _serverRocketStartPos + Vector3.up * _serverRocketSpeed * elapsed;
                ServerEndSession(
                    explosionPos,
                    Configuration.PlayerLinkerExplosionForce.Value,
                    Configuration.PlayerLinkerExplosionRadius.Value
                );
            }

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }
        }

        public override void ClientHoleCleanup()
        {
            ClearClientState();
        }

        // =====================================================================
        //  Shared cleanup helpers
        // =====================================================================

        private static void CleanupVfx()
        {
            if (s_tetherLine != null)
            {
                Object.Destroy(s_tetherLine.gameObject);
                s_tetherLine = null;
            }
            if (s_rocketVfxInstance != null)
            {
                Object.Destroy(s_rocketVfxInstance);
                s_rocketVfxInstance = null;
            }
        }

        private static void ClearClientState()
        {
            // Stop the spring coroutine before clearing the movement reference.
            if (s_forceCoroutine != null && s_forceMovement != null)
                s_forceMovement.StopCoroutine(s_forceCoroutine);

            CleanupVfx();

            s_forceCoroutine = null;
            s_forceMovement = null;
            s_tetherActive = false;
            s_targetNetId = 0;
            s_rocketStartPos = Vector3.zero;
            s_rocketSpeed = 0f;
            s_rocketStartTime = 0f;
            s_rocketDuration = 0f;
        }
    }
}
