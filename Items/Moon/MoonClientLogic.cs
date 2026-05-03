using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    // =========================================================================
    //  Client-side session state and physics for an active Moon session.
    //
    //  Phasing (driven by elapsed time since the begin message was received):
    //    Approach [0, ApproachDuration)              — Moon lerps from MoonSpawnPos
    //                                                  toward MoonImpactPos; warning
    //                                                  UI shown; no forces applied.
    //    Suck     [ApproachDuration, TotalDuration)  — Moon locked at MoonImpactPos;
    //                                                  local player knocked out once,
    //                                                  then pulled upward every tick.
    //
    //  StartTime is captured on the client when the begin message arrives (Time.time),
    //  not sent over the network. All clients animate in lock-step because the approach
    //  and suck durations are fixed and the message arrives within a single frame.
    //
    //  Only the local player's client runs ForceCoroutine. All clients animate the
    //  moon VFX via UpdateAll(), called once per frame from MoonNetworkBridge.Update().
    // =========================================================================

    internal sealed class MoonSessionState
    {
        public uint WielderNetId;
        public Vector3 MoonSpawnPos;
        public Vector3 MoonImpactPos;
        public float ApproachDuration;
        public float SuckDuration;
        public float StartTime; // Time.time when HandleMoonBegin was called
        public GameObject MoonVfxInstance;

        // Physics coroutine — non-null only on the local player's client
        public Coroutine ForceCoroutine;
        public PlayerMovement ForceMovement;
        public float InitialScale;
        public float FinalScale;
        public float TotalDuration => ApproachDuration + SuckDuration;
    }

    public static class MoonClientLogic
    {
        // Keyed by wielder netId. At most one entry due to GlobalSessionLock.
        private static readonly Dictionary<uint, MoonSessionState> s_sessions =
            new Dictionary<uint, MoonSessionState>();

        // ── NetworkClient message handlers ────────────────────────────────────

        public static void HandleMoonBegin(MoonBeginMessage msg)
        {
            BeginSession(msg);
        }

        public static void HandleMoonEnd(MoonEndMessage msg)
        {
            EndSession(msg.WielderNetId, msg.ExplosionPos);
        }

        // ── Per-frame update (called from MoonNetworkBridge.Update on isOwned bridge) ──

        public static void UpdateAll()
        {
            if (s_sessions.Count == 0)
                return;

            foreach (var kvp in s_sessions)
            {
                var state = kvp.Value;
                float elapsed = Time.time - state.StartTime;

                Vector3 moonPos;
                if (elapsed < state.ApproachDuration)
                {
                    float t =
                        state.ApproachDuration > 0f
                            ? Mathf.Clamp01(elapsed / state.ApproachDuration)
                            : 1f;
                    moonPos = Vector3.Lerp(state.MoonSpawnPos, state.MoonImpactPos, t);
                }
                else
                {
                    moonPos = state.MoonImpactPos;
                }

                if (state.MoonVfxInstance != null)
                {
                    state.MoonVfxInstance.transform.position = moonPos;

                    // Scale grows as the moon approaches so it looks larger overhead.
                    float totalDist = Vector3.Distance(state.MoonSpawnPos, state.MoonImpactPos);
                    float curDist = Vector3.Distance(moonPos, state.MoonImpactPos);
                    float progress = totalDist > 0f ? 1f - (curDist / totalDist) : 1f;
                    float scale = Mathf.Lerp(state.InitialScale, state.FinalScale, progress);
                    state.MoonVfxInstance.transform.localScale = Vector3.one * scale;
                }
            }
        }

        /// Called from MoonNetworkBridge.OnGUI() (local player only).
        public static void DrawWarningGui()
        {
            if (s_sessions.Count == 0)
                return;

            foreach (var kvp in s_sessions)
            {
                var state = kvp.Value;
                float elapsed = Time.time - state.StartTime;

                if (elapsed >= state.ApproachDuration)
                    return; // only warn during approach

                float remaining = state.ApproachDuration - elapsed;

                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                };

                var oldColor = GUI.color;
                GUI.color = new Color(1f, 0.3f, 0.1f, 0.9f);

                float w = Screen.width * 0.6f;
                float h = 80f;
                float x = (Screen.width - w) * 0.5f;
                float y = Screen.height * 0.15f;

                GUI.Label(new Rect(x, y, w, h), $"THE MOON IS FALLING\n{remaining:F0}s", style);
                GUI.color = oldColor;
                return;
            }
        }

        /// Destroys all active sessions. Called by MoonNetworkBridge.ClientHoleCleanup().
        public static void ClearAll()
        {
            foreach (var kvp in new List<uint>(s_sessions.Keys))
                EndSessionInternal(kvp);
        }

        // ── Session lifecycle ─────────────────────────────────────────────────

        private static void BeginSession(MoonBeginMessage msg)
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null)
                return;

            EndSessionInternal(msg.WielderNetId);

            var state = new MoonSessionState
            {
                WielderNetId = msg.WielderNetId,
                MoonSpawnPos = msg.MoonSpawnPos,
                MoonImpactPos = msg.MoonImpactPos,
                ApproachDuration = msg.ApproachDuration,
                SuckDuration = msg.SuckDuration,
                StartTime = Time.time,
                InitialScale = msg.InitialScale,
                FinalScale = msg.FinalScale,
            };

            if (AssetLoader.MoonVfxPrefab != null)
            {
                state.MoonVfxInstance = Object.Instantiate(
                    AssetLoader.MoonVfxPrefab,
                    msg.MoonSpawnPos,
                    Quaternion.identity
                );
                foreach (var rb in state.MoonVfxInstance.GetComponentsInChildren<Rigidbody>())
                {
                    rb.isKinematic = true;
                    rb.useGravity = false;
                }
                Object.DontDestroyOnLoad(state.MoonVfxInstance);
                IssaPluginPlugin.Log.LogInfo(
                    $"[MoonClientLogic] MoonVfxInstance created at {msg.MoonSpawnPos}, scale={state.MoonVfxInstance.transform.localScale}"
                );
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[MoonClientLogic] AssetLoader.MoonVfxPrefab is NULL — moon VFX will not appear."
                );
            }

            s_sessions[msg.WielderNetId] = state;

            var movement = localInfo.Movement;
            if (movement != null)
            {
                state.ForceMovement = movement;
                state.ForceCoroutine = movement.StartCoroutine(ForceCoroutine(msg.WielderNetId));
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[MoonClientLogic] Session started. wielder={msg.WielderNetId} spawnPos={msg.MoonSpawnPos} impactPos={msg.MoonImpactPos}"
            );
        }

        private static void EndSession(uint wielderNetId, Vector3 explosionPos)
        {
            if (!s_sessions.TryGetValue(wielderNetId, out var state))
                return;

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
                    rb.useGravity = true;

                    bool localIsWielder =
                        (localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u) == wielderNetId;
                    if (!localIsWielder || ModConfig.Moon.PullAffectsWielder.Value)
                    {
                        float explosionForce = ModConfig.Moon.ExplosionForce.Value;
                        float explosionRadius = ModConfig.Moon.ExplosionRadius.Value;
                        rb.AddExplosionForce(
                            explosionForce,
                            explosionPos,
                            explosionRadius,
                            1f,
                            ForceMode.VelocityChange
                        );
                    }
                }
            }

            VfxManager.PlayPooledVfxLocalOnly(
                VfxType.RocketLauncherRocketExplosion,
                explosionPos,
                Quaternion.identity,
                Vector3.one * 10f
            );
            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                explosionPos
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[MoonClientLogic] Moon explosion at {explosionPos} (wielder={wielderNetId})."
            );

            EndSessionInternal(wielderNetId);
        }

        private static void EndSessionInternal(uint wielderNetId)
        {
            if (!s_sessions.TryGetValue(wielderNetId, out var state))
                return;

            s_sessions.Remove(wielderNetId);

            if (state.ForceCoroutine != null && state.ForceMovement != null)
                state.ForceMovement.StopCoroutine(state.ForceCoroutine);

            if (state.MoonVfxInstance != null)
                Object.Destroy(state.MoonVfxInstance);
        }

        // ── Pull force coroutine (local player's client only) ─────────────────
        //
        //  Spins through the approach phase with no effect, then:
        //    — knocks out the local player once on suck-phase entry
        //    — applies upward pull force toward MoonImpactPos every fixed tick

        private static IEnumerator ForceCoroutine(uint wielderNetId)
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null)
                yield break;

            uint localNetId =
                localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u;
            bool localIsWielder = localNetId == wielderNetId;

            Rigidbody lastRb = null;
            bool knockoutApplied = false;

            while (s_sessions.ContainsKey(wielderNetId))
            {
                if (!s_sessions.TryGetValue(wielderNetId, out var state))
                    yield break;

                float elapsed = Time.time - state.StartTime;

                if (elapsed < state.ApproachDuration)
                {
                    yield return new WaitForFixedUpdate();
                    continue;
                }

                // Suck phase — skip forces entirely if this player is the wielder and
                // the config says not to pull the wielder.
                if (localIsWielder && !ModConfig.Moon.PullAffectsWielder.Value)
                {
                    yield return new WaitForFixedUpdate();
                    continue;
                }

                var seat = localInfo.ActiveGolfCartSeat;
                Rigidbody rb =
                    seat.IsValid() && seat.golfCart != null
                        ? seat.golfCart.AsEntity.Rigidbody
                        : localInfo.GetComponentInParent<Rigidbody>();

                if (lastRb != null && lastRb != rb)
                    lastRb.useGravity = true;
                lastRb = rb;

                if (rb != null)
                {
                    if (!knockoutApplied)
                    {
                        knockoutApplied = true;
                        ApplyMoonKnockout(localInfo, state);
                    }

                    rb.useGravity = false;
                    rb.angularVelocity = Vector3.zero;

                    Vector3 playerPos = rb.position;
                    Vector3 direction = (state.MoonImpactPos - playerPos).normalized;
                    float distance = Vector3.Distance(playerPos, state.MoonImpactPos);

                    float maxDist = ModConfig.Moon.PullRadius.Value;
                    float t = Mathf.Clamp01(1f - distance / maxDist);
                    t *= t;
                    float pullForce = ModConfig.Moon.PullForce.Value;
                    float force = Mathf.Lerp(pullForce * 0.3f, pullForce, t);

                    rb.AddForce(direction * force, ForceMode.Acceleration);

                    // Cap velocity toward the moon so players don't overshoot
                    float towardSpeed = Vector3.Dot(rb.linearVelocity, direction);
                    float maxSpeed = ModConfig.Moon.MaxPullSpeed.Value;
                    if (towardSpeed > maxSpeed)
                        rb.linearVelocity -= direction * (towardSpeed - maxSpeed);
                }

                yield return new WaitForFixedUpdate();
            }

            if (lastRb != null)
                lastRb.useGravity = true;
        }

        private static void ApplyMoonKnockout(PlayerInfo localInfo, MoonSessionState state)
        {
            var wielderTransform = GetTransformByNetId(state.WielderNetId);
            var wielderInfo = wielderTransform?.GetComponentInParent<PlayerInfo>();
            if (wielderInfo == null)
                return;

            var useId = new ItemUseId(
                wielderInfo.PlayerId.Guid,
                MoonItem.NextUseIndex(),
                ItemType.RocketLauncher
            );
            bool _;
            localInfo.Movement.TryKnockOut(
                wielderInfo,
                KnockoutType.Rocket,
                false,
                localInfo.Movement.transform.InverseTransformPoint(state.MoonImpactPos),
                Vector3.Distance(localInfo.transform.position, state.MoonImpactPos),
                Vector3.zero,
                true,
                useId,
                false,
                true,
                out _
            );
        }

        // ── Utilities ─────────────────────────────────────────────────────────

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
    }
}
