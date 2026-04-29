using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // =========================================================================
    //  Client-side session state and physics for all active UFO abductions.
    //
    //  Phasing (driven by elapsed time since StartTime):
    //    Approach  [0, ApproachDuration)           — UFO flies to hover position; no victim force.
    //    Abduction [ApproachDuration, +AbductionDuration) — UFO hovers; victim pulled toward HoverPos.
    //    Ascent    [+AbductionDuration, totalDuration)   — UFO ascends to ExplosionPos; victim dragged.
    //
    //  Only the victim's own client runs ForceCoroutine. All clients animate the
    //  UFO VFX and beam using UpdateAll(), called once per frame.
    // =========================================================================

    internal sealed class UfoAbductionSessionState
    {
        public uint WielderNetId;
        public Vector3 UfoSpawnPos;
        public Vector3 HoverPos;
        public Vector3 ExplosionPos;
        public float ApproachDuration;
        public float AbductionDuration;
        public float AscentDuration;
        public float StartTime;
        public float SpringForce;
        public float MaxPullSpeed;
        public float NaturalLength;

        // VFX — all clients
        public GameObject UfoVfxInstance;
        public LineRenderer BeamLine;

        // Physics coroutine — non-null only on the victim's own client
        public Coroutine ForceCoroutine;
        public PlayerMovement ForceMovement;

        public float TotalDuration => ApproachDuration + AbductionDuration + AscentDuration;

        public Vector3 GetUfoPosition(float elapsed)
        {
            if (elapsed < ApproachDuration)
            {
                float t = ApproachDuration > 0f ? elapsed / ApproachDuration : 1f;
                return Vector3.Lerp(UfoSpawnPos, HoverPos, t);
            }

            float abductionElapsed = elapsed - ApproachDuration;
            if (abductionElapsed < AbductionDuration)
                return HoverPos;

            float ascentElapsed = abductionElapsed - AbductionDuration;
            float ascentT =
                AscentDuration > 0f ? Mathf.Clamp01(ascentElapsed / AscentDuration) : 1f;
            return Vector3.Lerp(HoverPos, ExplosionPos, ascentT);
        }
    }

    public static class UfoAbductionClientLogic
    {
        private static readonly Dictionary<uint, UfoAbductionSessionState> s_sessions =
            new Dictionary<uint, UfoAbductionSessionState>();

        // ── NetworkClient message handlers ────────────────────────────────────

        public static void HandleBegin(UfoAbductionBeginMessage msg)
        {
            BeginSession(msg);
        }

        public static void HandleEnd(UfoAbductionEndMessage msg)
        {
            EndSession(msg.VictimNetId, msg.ExplosionPos, msg.ExplosionForce, msg.ExplosionRadius);
        }

        // ── Public API ────────────────────────────────────────────────────────

        internal static bool TryGetSession(uint victimNetId, out UfoAbductionSessionState state) =>
            s_sessions.TryGetValue(victimNetId, out state);

        /// Returns the first active session where the local player is the wielder.
        /// Used by the overlay to show the wielder confirmation state after firing.
        internal static bool TryGetSessionForWielder(
            uint wielderNetId,
            out UfoAbductionSessionState state
        )
        {
            foreach (var kvp in s_sessions)
            {
                if (kvp.Value.WielderNetId == wielderNetId)
                {
                    state = kvp.Value;
                    return true;
                }
            }
            state = null;
            return false;
        }

        /// Called every frame from the local player's UfoAbductionNetworkBridge.Update().
        public static void UpdateAll()
        {
            if (s_sessions.Count == 0)
                return;

            var sessions = s_sessions.ToArray();
            foreach (var kvp in sessions)
            {
                var state = kvp.Value;
                float elapsed = Time.time - state.StartTime;
                Vector3 ufoPos = state.GetUfoPosition(elapsed);

                if (state.UfoVfxInstance != null)
                    state.UfoVfxInstance.transform.position = ufoPos;

                UpdateBeamLine(kvp.Key, state, ufoPos, elapsed);
            }
        }

        /// Immediately destroys all VFX and stops all coroutines (hole transition cleanup).
        public static void ClearAll()
        {
            foreach (var kvp in s_sessions.ToArray())
                EndSessionInternal(kvp.Key);
        }

        // ── Session lifecycle ─────────────────────────────────────────────────

        private static void BeginSession(UfoAbductionBeginMessage msg)
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null)
                return;

            EndSessionInternal(msg.VictimNetId);

            var state = new UfoAbductionSessionState
            {
                WielderNetId = msg.WielderNetId,
                UfoSpawnPos = msg.UfoSpawnPos,
                HoverPos = msg.HoverPos,
                ExplosionPos = msg.ExplosionPos,
                ApproachDuration = msg.ApproachDuration,
                AbductionDuration = msg.AbductionDuration,
                AscentDuration = msg.AscentDuration,
                StartTime = Time.time,
                SpringForce = msg.SpringForce,
                MaxPullSpeed = msg.MaxPullSpeed,
                NaturalLength = msg.NaturalLength,
            };

            // UFO VFX (null-safe — prefab may not exist yet)
            if (AssetLoader.UfoAbductionUfoPrefab != null)
            {
                state.UfoVfxInstance = Object.Instantiate(
                    AssetLoader.UfoAbductionUfoPrefab,
                    msg.UfoSpawnPos,
                    Quaternion.identity
                );
                foreach (var rb in state.UfoVfxInstance.GetComponentsInChildren<Rigidbody>())
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
                Object.DontDestroyOnLoad(state.UfoVfxInstance);
            }

            state.BeamLine = CreateBeamLine();

            s_sessions[msg.VictimNetId] = state;

            // Spring coroutine runs only on the victim's own client
            uint localNetId = localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u;
            if (localNetId == msg.VictimNetId)
            {
                var movement = localInfo.Movement;
                if (movement != null)
                {
                    state.ForceMovement = movement;
                    state.ForceCoroutine = movement.StartCoroutine(ForceCoroutine(msg.VictimNetId));
                    IssaPluginPlugin.Log.LogInfo(
                        $"[UfoAbductionClientLogic] Session started — local player is victim (netId={msg.VictimNetId})."
                    );
                }
            }
        }

        private static void EndSession(
            uint victimNetId,
            Vector3 explosionPos,
            float explosionForce,
            float explosionRadius
        )
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo != null)
            {
                s_sessions.TryGetValue(victimNetId, out var state);
                uint localNetId = localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u;

                // Knockout on victim's client only
                if (state != null && localNetId == victimNetId)
                {
                    var wielderTransform = GetTransformByNetId(state.WielderNetId);
                    var wielderInfo = wielderTransform?.GetComponentInParent<PlayerInfo>();
                    if (wielderInfo != null)
                    {
                        var useId = new ItemUseId(
                            wielderInfo.PlayerId.Guid,
                            UfoAbductionItem.NextUseIndex(),
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

                // Explosion force on all nearby players (applied on their own client)
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

            // Explosion VFX (all clients)
            VfxManager.PlayPooledVfxLocalOnly(
                VfxType.RocketLauncherRocketExplosion,
                explosionPos,
                Quaternion.identity,
                Vector3.one * 2f
            );

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                explosionPos
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[UfoAbductionClientLogic] Explosion at {explosionPos} (victim={victimNetId})."
            );

            EndSessionInternal(victimNetId);
        }

        private static void EndSessionInternal(uint victimNetId)
        {
            if (!s_sessions.TryGetValue(victimNetId, out var state))
                return;

            s_sessions.Remove(victimNetId);

            if (state.ForceCoroutine != null && state.ForceMovement != null)
                state.ForceMovement.StopCoroutine(state.ForceCoroutine);

            if (state.BeamLine != null)
                Object.Destroy(state.BeamLine.gameObject);

            if (state.UfoVfxInstance != null)
                Object.Destroy(state.UfoVfxInstance);
        }

        // ── Spring force coroutine (victim's client only) ─────────────────────

        private static IEnumerator ForceCoroutine(uint victimNetId)
        {
            while (s_sessions.ContainsKey(victimNetId))
            {
                var localInfo = GameManager.LocalPlayerInfo;
                if (localInfo == null)
                    yield break;

                if (!s_sessions.TryGetValue(victimNetId, out var state))
                    yield break;

                float sessionElapsed = Time.time - state.StartTime;

                // Only apply force during abduction and ascent phases
                if (sessionElapsed >= state.ApproachDuration)
                {
                    Vector3 targetPos = state.GetUfoPosition(sessionElapsed);

                    var seat = localInfo.ActiveGolfCartSeat;
                    Rigidbody rb =
                        seat.IsValid() && seat.golfCart != null
                            ? seat.golfCart.AsEntity.Rigidbody
                            : localInfo.GetComponentInParent<Rigidbody>();

                    if (rb != null)
                    {
                        Vector3 toTarget = targetPos - rb.position;
                        float dist = toTarget.magnitude;

                        if (dist > state.NaturalLength && dist > 0.05f)
                        {
                            Vector3 dir = toTarget / dist;
                            float stretch = dist - state.NaturalLength;
                            float targetSpeed = stretch * state.SpringForce;
                            float currentComp = Vector3.Dot(rb.linearVelocity, dir);
                            float deficit = Mathf.Min(
                                targetSpeed - currentComp,
                                state.MaxPullSpeed
                            );

                            if (deficit > 0f)
                                rb.AddForce(dir * deficit, ForceMode.VelocityChange);
                        }
                    }
                }

                yield return new WaitForFixedUpdate();
            }
        }

        // ── VFX helpers ───────────────────────────────────────────────────────

        private static LineRenderer CreateBeamLine()
        {
            var lineGo = new GameObject("UfoAbductionBeam");
            var lr = lineGo.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = 0.3f;
            lr.endWidth = 0.8f;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = new Color(0.3f, 1f, 0.5f, 0.7f); // green (UFO belly)
            lr.endColor = new Color(0.3f, 1f, 0.5f, 0.15f); // fade at victim end
            lr.useWorldSpace = true;
            Object.DontDestroyOnLoad(lineGo);
            return lr;
        }

        private static void UpdateBeamLine(
            uint victimNetId,
            UfoAbductionSessionState state,
            Vector3 ufoPos,
            float elapsed
        )
        {
            if (state.BeamLine == null)
                return;

            // Show beam only during abduction and ascent phases
            bool beamActive = elapsed >= state.ApproachDuration;
            state.BeamLine.enabled = beamActive;

            if (!beamActive)
                return;

            Transform victimT = GetTransformByNetId(victimNetId);
            if (victimT != null)
            {
                state.BeamLine.SetPosition(0, ufoPos);
                state.BeamLine.SetPosition(1, victimT.position + Vector3.up * 1f);
            }
        }

        // ── Utilities ─────────────────────────────────────────────────────────

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
    }
}
