using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // =========================================================================
    //  Shared spring physics / VFX / explosion logic for ALL rocket-tether
    //  sessions, regardless of which item created them.
    //
    //  Both RocketTetherNetworkBridge and RocketTetherGrenadeNetworkBridge
    //  route their RocketVictimConnected/Disconnected messages here instead of
    //  maintaining their own duplicate state.
    //
    //  Key design choices:
    //  • Dictionary keyed by victimNetId — supports N concurrent sessions.
    //  • TetherSessionState is a class (reference type) so dict lookups return
    //    a live reference rather than a value copy.
    //  • ForceCoroutine is started on the victim's PlayerMovement MonoBehaviour
    //    so Unity owns the coroutine lifetime; StopCoroutine is always valid.
    //  • UpdateAll() iterates a snapshot array to prevent InvalidOperationException
    //    if a session ends inside the loop.
    // =========================================================================

    /// <summary>
    /// Parameters that control the spring force applied to a tethered victim.
    /// Passed through RocketVictimConnectedMessage so each item can tune
    /// them independently.
    /// </summary>
    internal struct TetherSpringParams
    {
        public float SpringForce;
        public float MaxPullSpeed;
        public float NaturalLength;
    }

    /// <summary>
    /// All client-side state for one active tether session.
    /// Must be a class — it is stored in a Dictionary and fields are mutated
    /// after insertion (e.g. ForceCoroutine is set after StartCoroutine).
    /// </summary>
    internal sealed class TetherSessionState
    {
        public uint WielderNetId;
        public Vector3 RocketStartPos;
        public float RocketSpeed;
        public float StartTime;
        public float Duration;
        public TetherSpringParams SpringParams;

        // Visual objects (all clients)
        public LineRenderer TetherLine;
        public GameObject RocketVfxInstance;

        // Spring coroutine — non-null only on the victim's own client.
        public Coroutine ForceCoroutine;
        public PlayerMovement ForceMovement;
    }

    /// <summary>
    /// Static helper that manages all active tether sessions on the local client.
    /// </summary>
    public static class RocketTetherClientLogic
    {
        private static readonly Dictionary<uint, TetherSessionState> s_activeSessions =
            new Dictionary<uint, TetherSessionState>();

        // =====================================================================
        //  NetworkClient message handlers (registered once in NetworkManagerPatches)
        // =====================================================================

        public static void HandleVictimConnected(RocketVictimConnectedMessage msg)
        {
            BeginVictimSession(
                msg.VictimNetId,
                msg.WielderNetId,
                msg.RocketStartPos,
                msg.RocketSpeed,
                msg.Duration,
                new TetherSpringParams
                {
                    SpringForce   = msg.SpringForce,
                    MaxPullSpeed  = msg.MaxPullSpeed,
                    NaturalLength = msg.NaturalLength,
                }
            );
        }

        public static void HandleVictimDisconnected(RocketVictimDisconnectedMessage msg)
        {
            EndVictimSession(
                msg.VictimNetId,
                msg.ExplosionPosition,
                msg.ExplosionForce,
                msg.ExplosionRadius
            );
        }

        // =====================================================================
        //  Public API
        // =====================================================================

        internal static bool TryGetSessionForVictim(uint victimNetId, out TetherSessionState state) =>
            s_activeSessions.TryGetValue(victimNetId, out state);

        /// <summary>
        /// Called every frame from the local player's RocketTetherNetworkBridge.Update().
        /// Advances all active rocket VFX positions and tether lines.
        /// Uses a snapshot array to guard against modification-during-iteration.
        /// </summary>
        public static void UpdateAll()
        {
            if (s_activeSessions.Count == 0)
                return;

            // Snapshot keys to prevent InvalidOperationException if EndVictimSession
            // is somehow triggered during iteration (e.g. via a message in the same frame).
            var sessions = s_activeSessions.ToArray();

            foreach (var kvp in sessions)
            {
                var state = kvp.Value;
                float elapsed = Time.time - state.StartTime;
                Vector3 rocketPos =
                    state.RocketStartPos + Vector3.up * state.RocketSpeed * elapsed;

                if (state.RocketVfxInstance != null)
                    state.RocketVfxInstance.transform.SetPositionAndRotation(
                        rocketPos,
                        Quaternion.identity
                    );

                UpdateTetherLine(kvp.Key, state, rocketPos);
            }
        }

        /// <summary>
        /// Called from ClientHoleCleanup — destroys all VFX and stops all
        /// coroutines immediately without sending any network messages.
        /// </summary>
        public static void ClearAll()
        {
            foreach (var kvp in s_activeSessions.ToArray())
                EndVictimSessionInternal(kvp.Key);
        }

        // =====================================================================
        //  Session lifecycle
        // =====================================================================

        private static void BeginVictimSession(
            uint victimNetId,
            uint wielderNetId,
            Vector3 rocketStartPos,
            float rocketSpeed,
            float duration,
            TetherSpringParams spring
        )
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null)
                return;

            // Remove any stale entry for this victim (defensive).
            EndVictimSessionInternal(victimNetId);

            var state = new TetherSessionState
            {
                WielderNetId   = wielderNetId,
                RocketStartPos = rocketStartPos,
                RocketSpeed    = rocketSpeed,
                StartTime      = Time.time,
                Duration       = duration,
                SpringParams   = spring,
            };

            // ── Rocket VFX ────────────────────────────────────────────────────
            if (AssetLoader.RocketTetherRocketPrefab != null)
            {
                state.RocketVfxInstance = Object.Instantiate(
                    AssetLoader.RocketTetherRocketPrefab,
                    rocketStartPos,
                    Quaternion.identity
                );
                foreach (var rb in state.RocketVfxInstance.GetComponentsInChildren<Rigidbody>())
                {
                    rb.isKinematic = true;
                    rb.useGravity  = false;
                }
                Object.DontDestroyOnLoad(state.RocketVfxInstance);
            }

            // ── Tether line ───────────────────────────────────────────────────
            state.TetherLine = CreateTetherLine();

            // ── Spring force coroutine (victim's client only) ─────────────────
            uint localNetId = localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u;
            if (localNetId == victimNetId)
            {
                var movement = localInfo.Movement;
                if (movement != null)
                {
                    state.ForceMovement = movement;
                    state.ForceCoroutine = movement.StartCoroutine(
                        ForceCoroutine(victimNetId, duration)
                    );
                    IssaPluginPlugin.Log.LogInfo(
                        $"[RocketTetherClientLogic] Tether started — local player is victim (netId={victimNetId})."
                    );
                }
            }

            s_activeSessions[victimNetId] = state;
        }

        private static void EndVictimSession(
            uint victimNetId,
            Vector3 explosionPos,
            float explosionForce,
            float explosionRadius
        )
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo != null)
            {
                // Retrieve session before removing (needed for WielderNetId).
                s_activeSessions.TryGetValue(victimNetId, out var state);

                uint localNetId = localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u;

                // ── Knockout (victim's client only) ───────────────────────────
                if (state != null && localNetId == victimNetId)
                {
                    var wielderTransform = GetTransformByNetId(state.WielderNetId);
                    var wielderInfo      = wielderTransform?.GetComponentInParent<PlayerInfo>();
                    if (wielderInfo != null)
                    {
                        var useId = new ItemUseId(
                            wielderInfo.PlayerId.Guid,
                            RocketTetherItem.NextUseIndex(),
                            ItemType.RocketLauncher
                        );
                        bool _;
                        localInfo.Movement.TryKnockOut(
                            wielderInfo,
                            KnockoutType.Rocket,
                            false,
                            localInfo.Movement.transform.InverseTransformPoint(
                                wielderInfo.transform.position
                            ),
                            Vector3.Distance(
                                localInfo.transform.position,
                                wielderInfo.transform.position
                            ),
                            Vector3.zero,
                            true,
                            useId,
                            false,
                            true,
                            out _
                        );
                    }
                }

                // ── Explosion force (all clients, if within radius) ───────────
                var seat = localInfo.ActiveGolfCartSeat;
                Rigidbody rb =
                    seat.IsValid() && seat.golfCart != null
                        ? seat.golfCart.AsEntity.Rigidbody
                        : localInfo.GetComponentInParent<Rigidbody>();

                if (rb != null)
                {
                    float dist = Vector3.Distance(rb.position, explosionPos);
                    if (dist < explosionRadius)
                        rb.AddExplosionForce(
                            explosionForce,
                            explosionPos,
                            explosionRadius,
                            0.5f,
                            ForceMode.VelocityChange
                        );
                }
            }

            // ── Explosion VFX (all clients) ───────────────────────────────────
            if (AssetLoader.JavelinExplosionVfxPrefab != null)
            {
                var vfx = Object.Instantiate(
                    AssetLoader.JavelinExplosionVfxPrefab,
                    explosionPos,
                    Quaternion.identity
                );
                Object.Destroy(vfx, 3f);
            }
            else
            {
                VfxManager.PlayPooledVfxLocalOnly(
                    VfxType.RocketLauncherRocketExplosion,
                    explosionPos,
                    Quaternion.identity,
                    Vector3.one
                );
            }

            // ── Camera shake (all clients, position-attenuated) ───────────────
            // Each victim's rocket explodes at a distinct position, so N victims
            // produce N shakes — consistent with N real explosions occurring nearby.
            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                explosionPos
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[RocketTetherClientLogic] Rocket exploded at {explosionPos} (victim={victimNetId})."
            );

            EndVictimSessionInternal(victimNetId);
        }

        private static void EndVictimSessionInternal(uint victimNetId)
        {
            if (!s_activeSessions.TryGetValue(victimNetId, out var state))
                return;

            s_activeSessions.Remove(victimNetId);

            if (state.ForceCoroutine != null && state.ForceMovement != null)
                state.ForceMovement.StopCoroutine(state.ForceCoroutine);

            if (state.TetherLine != null)
                Object.Destroy(state.TetherLine.gameObject);

            if (state.RocketVfxInstance != null)
                Object.Destroy(state.RocketVfxInstance);
        }

        // =====================================================================
        //  Spring force coroutine (runs on victim's PlayerMovement)
        // =====================================================================

        private static IEnumerator ForceCoroutine(uint victimNetId, float maxDuration)
        {
            float elapsed = 0f;

            while (elapsed < maxDuration && s_activeSessions.ContainsKey(victimNetId))
            {
                var localInfo = GameManager.LocalPlayerInfo;
                if (localInfo == null)
                    yield break;

                if (!s_activeSessions.TryGetValue(victimNetId, out var state))
                    yield break;

                // Apply force to cart Rigidbody if seated (same pattern as gravity gun).
                var seat = localInfo.ActiveGolfCartSeat;
                Rigidbody rb =
                    seat.IsValid() && seat.golfCart != null
                        ? seat.golfCart.AsEntity.Rigidbody
                        : localInfo.GetComponentInParent<Rigidbody>();

                if (rb != null)
                {
                    // Deterministic rocket position — no network messages needed.
                    Vector3 rocketPos =
                        state.RocketStartPos + Vector3.up * state.RocketSpeed * elapsed;

                    Vector3 toRocket = rocketPos - rb.position;
                    float   dist     = toRocket.magnitude;

                    if (dist > state.SpringParams.NaturalLength && dist > 0.05f)
                    {
                        Vector3 dir         = toRocket / dist;
                        float   stretch     = dist - state.SpringParams.NaturalLength;
                        float   targetSpeed = stretch * state.SpringParams.SpringForce;
                        float   currentComp = Vector3.Dot(rb.linearVelocity, dir);
                        float   deficit     = Mathf.Min(
                            targetSpeed - currentComp,
                            state.SpringParams.MaxPullSpeed
                        );

                        if (deficit > 0f)
                            rb.AddForce(dir * deficit, ForceMode.VelocityChange);
                    }
                }

                elapsed += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }
            // Do NOT call EndVictimSessionInternal here — wait for the
            // RocketVictimDisconnectedMessage from the server.
        }

        // =====================================================================
        //  VFX helpers
        // =====================================================================

        private static LineRenderer CreateTetherLine()
        {
            var lineGo = new GameObject("RocketTetherLine");
            var lr     = lineGo.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth    = 0.12f;
            lr.endWidth      = 0.06f;
            lr.material      = new Material(Shader.Find("Sprites/Default"));
            lr.startColor    = new Color(1f, 0.55f, 0.1f, 0.9f); // orange (target end)
            lr.endColor      = new Color(1f, 0.2f,  0.1f, 0.9f); // red (rocket end)
            lr.useWorldSpace = true;
            Object.DontDestroyOnLoad(lineGo);
            return lr;
        }

        private static void UpdateTetherLine(
            uint victimNetId,
            TetherSessionState state,
            Vector3 rocketPos
        )
        {
            if (state.TetherLine == null)
                return;

            Transform targetT = GetTransformByNetId(victimNetId);
            if (targetT != null)
            {
                state.TetherLine.SetPosition(0, targetT.position + Vector3.up * 1f);
                state.TetherLine.SetPosition(1, rocketPos);
            }
        }

        // =====================================================================
        //  Utilities
        // =====================================================================

        /// <summary>
        /// Returns the transform for any networked player by netId.
        /// Checks local player first, then remote players, then the full spawned dict.
        /// </summary>
        internal static Transform GetTransformByNetId(uint netId)
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
                    if (p == null) continue;
                    var pNetId = p.GetComponent<NetworkIdentity>()?.netId ?? 0u;
                    if (pNetId == netId)
                        return p.transform;
                }
            }

            if (NetworkClient.spawned.TryGetValue(netId, out var identity) && identity != null)
                return identity.transform;

            return null;
        }
    }
}
