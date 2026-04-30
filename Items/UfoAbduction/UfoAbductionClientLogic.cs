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
    //    Approach  [0, ApproachDuration)            — UFO tracks victim's live position and flies
    //                                                 to directly above them; no victim force.
    //    Abduction [ApproachDuration, +AbductionDuration) — UFO hovers at locked HoverPos;
    //                                                 victim pulled toward it.
    //    Ascent    [+AbductionDuration, totalDuration)    — UFO ascends to ExplosionPos; victim dragged.
    //
    //  HoverPos and ExplosionPos start as server-provided estimates; UpdateAll() locks them
    //  to the victim's actual position the first frame after approach ends.
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

        // Raw height offsets used to recompute HoverPos/ExplosionPos against the
        // victim's actual position once the approach phase completes.
        public float HoverHeight;
        public float AscentHeight;

        // Set to true the first frame after approach ends; locks in HoverPos and
        // ExplosionPos against the victim's real position at that moment.
        public bool HoverLocked;

        // VFX — all clients
        public GameObject UfoVfxInstance;
        public LineRenderer BeamLine;

        // Picture-in-picture — all clients
        public Camera PipCamera;
        public RenderTexture PipRenderTexture;

        // Physics coroutine — non-null only on the victim's own client
        public Coroutine ForceCoroutine;
        public PlayerMovement ForceMovement;

        public float TotalDuration => ApproachDuration + AbductionDuration + AscentDuration;

        // Used for abduction and ascent phases only (HoverPos/ExplosionPos are locked
        // in by UpdateAll at the approach→abduction transition).
        public Vector3 GetUfoPosition(float elapsed)
        {
            float abductionElapsed = elapsed - ApproachDuration;
            if (abductionElapsed < AbductionDuration)
                return HoverPos;

            float ascentElapsed = abductionElapsed - AbductionDuration;
            float ascentT =
                AscentDuration > 0f ? Mathf.Clamp01(ascentElapsed / AscentDuration) : 1f;
            Vector3 basePos = Vector3.Lerp(HoverPos, ExplosionPos, ascentT);

            // Erratic horizontal drift during ascent.  Sine envelope peaks at mid-ascent
            // and returns to zero at the end so the explosion happens at ExplosionPos.
            float envelope = Mathf.Sin(ascentT * Mathf.PI);
            float amplitude = ModConfig.UfoAbduction.AscentDriftAmplitude.Value * envelope;
            float freq = ModConfig.UfoAbduction.AscentDriftFrequency.Value;
            float seed = StartTime * 0.13f;
            float nx = (Mathf.PerlinNoise(ascentElapsed * freq + seed, 17.3f) * 2f - 1f) * amplitude;
            float nz = (Mathf.PerlinNoise(53.7f, ascentElapsed * freq + seed) * 2f - 1f) * amplitude;
            return basePos + new Vector3(nx, 0f, nz);
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
                Vector3 ufoPos;

                if (elapsed < state.ApproachDuration)
                {
                    // Track the victim's live position during approach so the UFO
                    // always arrives directly above them regardless of movement.
                    Transform victimT = GetTransformByNetId(kvp.Key);
                    Vector3 approachTarget = victimT != null
                        ? victimT.position + Vector3.up * state.HoverHeight
                        : state.HoverPos;
                    float t = state.ApproachDuration > 0f
                        ? Mathf.Clamp01(elapsed / state.ApproachDuration)
                        : 1f;
                    ufoPos = Vector3.Lerp(state.UfoSpawnPos, approachTarget, t);
                }
                else
                {
                    // Lock HoverPos and ExplosionPos to the victim's actual position
                    // at the moment approach ends, then hold those values for the
                    // abduction and ascent phases.
                    if (!state.HoverLocked)
                    {
                        Transform victimT = GetTransformByNetId(kvp.Key);
                        if (victimT != null)
                        {
                            state.HoverPos = victimT.position + Vector3.up * state.HoverHeight;
                            state.ExplosionPos = state.HoverPos + Vector3.up * state.AscentHeight;
                        }
                        state.HoverLocked = true;
                    }
                    ufoPos = state.GetUfoPosition(elapsed);
                }

                if (state.UfoVfxInstance != null)
                    state.UfoVfxInstance.transform.position = ufoPos;

                UpdateBeamLine(kvp.Key, state, ufoPos, elapsed);
                UpdatePipCamera(kvp.Key, state, ufoPos, elapsed);
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
                HoverHeight = msg.HoverHeight,
                AscentHeight = msg.AscentHeight,
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
            CreatePipCamera(state);

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
            if (!s_sessions.ContainsKey(victimNetId))
                return;

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
                    // The beam disabled gravity on the victim; restore it now so the
                    // explosion force resolves under normal physics immediately.
                    if (localNetId == victimNetId)
                        rb.useGravity = true;

                    float dist = Vector3.Distance(rb.position, explosionPos);
                    if (dist < explosionRadius)
                    {
                        rb.AddExplosionForce(
                            explosionForce,
                            explosionPos,
                            explosionRadius,
                            0f,
                            ForceMode.VelocityChange
                        );

                        // Prevent the victim from being blasted further upward —
                        // they're already high in the air and should fall back down.
                        if (localNetId == victimNetId)
                        {
                            Vector3 vel = rb.linearVelocity;
                            if (vel.y > 0f) vel.y = 0f;
                            rb.linearVelocity = vel;
                        }
                    }
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

            if (state.PipCamera != null)
                Object.Destroy(state.PipCamera.gameObject);
            if (state.PipRenderTexture != null)
            {
                state.PipRenderTexture.Release();
                Object.Destroy(state.PipRenderTexture);
            }
        }

        // ── Position-lock coroutine (victim's client only) ────────────────────
        //
        //  During abduction: victim is suspended at NaturalLength below the UFO.
        //  During ascent:    offset lerps from NaturalLength → 0 so they get
        //                    sucked into the ship as it climbs.

        private static IEnumerator ForceCoroutine(uint victimNetId)
        {
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null)
                yield break;

            Rigidbody lastRb = null;
            bool knockoutApplied = false;

            while (s_sessions.ContainsKey(victimNetId))
            {
                if (!s_sessions.TryGetValue(victimNetId, out var state))
                    yield break;

                var seat = localInfo.ActiveGolfCartSeat;
                Rigidbody rb =
                    seat.IsValid() && seat.golfCart != null
                        ? seat.golfCart.AsEntity.Rigidbody
                        : localInfo.GetComponentInParent<Rigidbody>();

                // Restore gravity if we switched rigidbodies (entered/exited cart)
                if (lastRb != null && lastRb != rb)
                    lastRb.useGravity = true;
                lastRb = rb;

                if (rb != null)
                {
                    float sessionElapsed = Time.time - state.StartTime;

                    if (sessionElapsed >= state.ApproachDuration)
                    {
                        // Knock the victim over exactly once when the beam first engages
                        if (!knockoutApplied)
                        {
                            knockoutApplied = true;
                            ApplyAbductionKnockout(localInfo, state);
                        }

                        Vector3 targetPos = ComputeVictimTargetPos(state, sessionElapsed);
                        rb.useGravity = false;
                        rb.angularVelocity = Vector3.zero;
                        LockToPosition(rb, targetPos, state.MaxPullSpeed);
                    }
                }

                yield return new WaitForFixedUpdate();
            }

            // Restore physics when session ends
            if (lastRb != null)
                lastRb.useGravity = true;
        }

        private static void ApplyAbductionKnockout(PlayerInfo localInfo, UfoAbductionSessionState state)
        {
            var wielderTransform = GetTransformByNetId(state.WielderNetId);
            var wielderInfo = wielderTransform?.GetComponentInParent<PlayerInfo>();
            if (wielderInfo == null)
                return;

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
                localInfo.Movement.transform.InverseTransformPoint(wielderInfo.transform.position),
                Vector3.Distance(localInfo.transform.position, wielderInfo.transform.position),
                Vector3.zero,
                true,
                useId,
                false,
                true,
                out _
            );
        }

        // Returns the world position the victim should occupy at a given session elapsed time.
        private static Vector3 ComputeVictimTargetPos(UfoAbductionSessionState state, float elapsed)
        {
            Vector3 ufoPos = state.GetUfoPosition(elapsed);
            float abductionElapsed = elapsed - state.ApproachDuration;

            if (abductionElapsed < state.AbductionDuration)
            {
                // Suspended in the beam directly below the UFO
                return ufoPos - Vector3.up * state.NaturalLength;
            }

            // Sucked into the ship: offset shrinks from NaturalLength → 0 over ascent
            float ascentT = state.AscentDuration > 0f
                ? Mathf.Clamp01((abductionElapsed - state.AbductionDuration) / state.AscentDuration)
                : 1f;
            float offset = Mathf.Lerp(state.NaturalLength, 0f, ascentT);
            return ufoPos - Vector3.up * offset;
        }

        // Directly warps the rigidbody position toward targetPos (bypasses the
        // velocity pipeline so PlayerMovement's grounding logic can't fight us).
        private static void LockToPosition(Rigidbody rb, Vector3 targetPos, float maxSpeed)
        {
            rb.linearVelocity = Vector3.zero;
            rb.position = Vector3.MoveTowards(rb.position, targetPos, maxSpeed * Time.fixedDeltaTime);
        }

        // ── PiP camera helpers ────────────────────────────────────────────────

        private static void CreatePipCamera(UfoAbductionSessionState state)
        {
            state.PipRenderTexture = new RenderTexture(320, 180, 16);
            state.PipRenderTexture.Create();

            var camGo = new GameObject("UfoAbductionPiP");
            Object.DontDestroyOnLoad(camGo);

            var cam = camGo.AddComponent<Camera>();
            cam.targetTexture = state.PipRenderTexture;
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 500f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            // depth -20 so it renders before the main camera without conflicting
            cam.depth = -20f;
            state.PipCamera = cam;
        }

        private static void UpdatePipCamera(
            uint victimNetId,
            UfoAbductionSessionState state,
            Vector3 ufoPos,
            float elapsed
        )
        {
            if (state.PipCamera == null)
                return;

            Transform victimT = GetTransformByNetId(victimNetId);
            Vector3 victimPos = victimT != null ? victimT.position : ufoPos - Vector3.up * state.HoverHeight;

            // Focus on the midpoint so both victim and UFO stay in-frame
            Vector3 focusPoint = Vector3.Lerp(victimPos, ufoPos, 0.5f);

            // Pull the camera back proportionally to the UFO-victim separation so the
            // full approach arc is always framed (during hover this naturally zooms in)
            float separation = Vector3.Distance(victimPos, ufoPos);
            float camDist = Mathf.Max(separation * 0.7f, 12f);

            float orbitAngle = elapsed * 20f;
            Vector3 camOffset = Quaternion.Euler(20f, orbitAngle, 0f) * new Vector3(0f, 0f, -camDist);
            state.PipCamera.transform.position = focusPoint + camOffset;
            state.PipCamera.transform.LookAt(focusPoint);
        }

        /// Returns the RenderTexture for any currently active abduction session, or null.
        internal static RenderTexture GetActivePipTexture()
        {
            foreach (var kvp in s_sessions)
            {
                var tex = kvp.Value.PipRenderTexture;
                if (tex != null && tex.IsCreated())
                    return tex;
            }
            return null;
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
