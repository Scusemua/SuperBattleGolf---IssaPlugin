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

                    // Keep the face pointed at the wielder. When the direction is nearly
                    // vertical (moon directly above), use Vector3.forward as the up reference
                    // to avoid gimbal lock in LookRotation.
                    Transform wielderT = GetTransformByNetId(state.WielderNetId);
                    if (wielderT != null)
                    {
                        Vector3 dirToWielder = (wielderT.position - moonPos).normalized;
                        if (dirToWielder.sqrMagnitude > 0.001f)
                        {
                            Vector3 up =
                                Mathf.Abs(Vector3.Dot(dirToWielder, Vector3.up)) > 0.99f
                                    ? Vector3.forward
                                    : Vector3.up;
                            state.MoonVfxInstance.transform.rotation = Quaternion.LookRotation(
                                dirToWielder,
                                up
                            );
                        }
                    }
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

        // ── Dusk skybox effect ────────────────────────────────────────────────

        private static readonly Color DuskSkyColor = new Color(0.05f, 0.02f, 0.08f);
        private static readonly Color DuskHorizonColor = new Color(0.6f, 0.15f, 0.0f);
        private static readonly Vector4 DuskSunDirection = new Vector4(0.5f, -0.2f, 0.5f, 0f);
        private const float DuskMoonCycle = 0.3f;
        private const float DuskStarsExposure = 5.0f;
        private const float DuskAmbientIntensity = 0.3f;
        private static readonly Color DuskAmbientLight = new Color(0.55f, 0.25f, 0.05f);
        private static readonly Color DuskFogColor = new Color(0.6f, 0.25f, 0.05f);
        private const float DuskFogDensity = 0.008f;
        private static readonly Color DuskSunLightColor = new Color(1.0f, 0.45f, 0.1f);
        private const float DuskSunLightIntensity = 0.4f;

        private static Material _duskSkyInstance;
        private static Material _originalSharedSkybox;
        private static Color _origSkyColor,
            _origHorizonColor;
        private static Vector4 _origSunDirection;
        private static float _origMoonCycle,
            _origStarsExposure,
            _origAmbientIntensity;
        private static Color _savedAmbientLight,
            _savedFogColor;
        private static float _savedFogDensity;
        private static bool _savedFog;
        private static Light _sunLight;
        private static Color _savedSunColor;
        private static float _savedSunIntensity;
        private static Coroutine _duskCoroutine;
        private static MonoBehaviour _duskCoroutineHost;

        private static void BeginDuskEffect(MonoBehaviour host)
        {
            if (!ModConfig.Moon.DuskEnabled.Value || host == null)
                return;

            _originalSharedSkybox = RenderSettings.skybox;
            if (_originalSharedSkybox == null)
                return;

            _origSkyColor = _originalSharedSkybox.GetColor("_SkyColor");
            _origHorizonColor = _originalSharedSkybox.GetColor("_HorizonColor");
            _origSunDirection = _originalSharedSkybox.GetVector("_SunDirection");
            _origMoonCycle = _originalSharedSkybox.GetFloat("_MoonCycle");
            _origStarsExposure = _originalSharedSkybox.GetFloat("_StarsExposure");
            _origAmbientIntensity = _originalSharedSkybox.GetFloat("_AmbientIntensity");

            _savedAmbientLight = RenderSettings.ambientLight;
            _savedFog = RenderSettings.fog;
            _savedFogColor = RenderSettings.fogColor;
            _savedFogDensity = RenderSettings.fogDensity;

            _sunLight = RenderSettings.sun;
            if (_sunLight == null)
            {
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light.type == LightType.Directional && light.isActiveAndEnabled)
                    {
                        _sunLight = light;
                        break;
                    }
                }
            }
            if (_sunLight != null)
            {
                _savedSunColor = _sunLight.color;
                _savedSunIntensity = _sunLight.intensity;
            }

            _duskSkyInstance = Object.Instantiate(_originalSharedSkybox);
            RenderSettings.skybox = _duskSkyInstance;
            _duskCoroutineHost = host;
            _duskCoroutine = host.StartCoroutine(DuskFadeIn());
        }

        private static void EndDuskEffect()
        {
            if (_duskSkyInstance == null)
                return;

            if (_duskCoroutine != null && _duskCoroutineHost != null)
                _duskCoroutineHost.StopCoroutine(_duskCoroutine);
            _duskCoroutine = null;

            if (_duskCoroutineHost == null)
            {
                ClearDuskState();
                return;
            }

            _duskCoroutine = _duskCoroutineHost.StartCoroutine(DuskFadeOut());
        }

        private static void RestoreSkyboxImmediate()
        {
            if (_duskSkyInstance == null)
                return;

            if (_duskCoroutine != null && _duskCoroutineHost != null)
                _duskCoroutineHost.StopCoroutine(_duskCoroutine);
            _duskCoroutine = null;

            ClearDuskState();
        }

        private static void ClearDuskState()
        {
            RenderSettings.skybox = _originalSharedSkybox;
            RenderSettings.ambientLight = _savedAmbientLight;
            RenderSettings.fog = _savedFog;
            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.fogDensity = _savedFogDensity;
            if (_sunLight != null)
            {
                _sunLight.color = _savedSunColor;
                _sunLight.intensity = _savedSunIntensity;
                _sunLight = null;
            }
            if (_duskSkyInstance != null)
            {
                Object.Destroy(_duskSkyInstance);
                _duskSkyInstance = null;
            }
            _originalSharedSkybox = null;
            _duskCoroutineHost = null;
        }

        private static IEnumerator DuskFadeIn()
        {
            bool hasSkyColor = _duskSkyInstance.HasProperty("_SkyColor");
            bool hasHorizonColor = _duskSkyInstance.HasProperty("_HorizonColor");
            bool hasSunDirection = _duskSkyInstance.HasProperty("_SunDirection");
            bool hasMoonCycle = _duskSkyInstance.HasProperty("_MoonCycle");
            bool hasStarsExposure = _duskSkyInstance.HasProperty("_StarsExposure");
            bool hasAmbientIntensity = _duskSkyInstance.HasProperty("_AmbientIntensity");

            if (
                !hasSkyColor
                && !hasHorizonColor
                && !hasSunDirection
                && !hasMoonCycle
                && !hasStarsExposure
                && !hasAmbientIntensity
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[MoonClientLogic] DuskEffect: skybox has no matching shader properties — dusk effect will not appear."
                );
                yield break;
            }

            RenderSettings.fog = true;

            float duration = ModConfig.Moon.DuskFadeDuration.Value;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                if (_duskSkyInstance == null)
                    yield break;

                if (hasSkyColor)
                    _duskSkyInstance.SetColor(
                        "_SkyColor",
                        Color.Lerp(_origSkyColor, DuskSkyColor, t)
                    );
                if (hasHorizonColor)
                    _duskSkyInstance.SetColor(
                        "_HorizonColor",
                        Color.Lerp(_origHorizonColor, DuskHorizonColor, t)
                    );
                if (hasSunDirection)
                    _duskSkyInstance.SetVector(
                        "_SunDirection",
                        Vector4.Lerp(_origSunDirection, DuskSunDirection, t)
                    );
                if (hasMoonCycle)
                    _duskSkyInstance.SetFloat(
                        "_MoonCycle",
                        Mathf.Lerp(_origMoonCycle, DuskMoonCycle, t)
                    );
                if (hasStarsExposure)
                    _duskSkyInstance.SetFloat(
                        "_StarsExposure",
                        Mathf.Lerp(_origStarsExposure, DuskStarsExposure, t)
                    );
                if (hasAmbientIntensity)
                    _duskSkyInstance.SetFloat(
                        "_AmbientIntensity",
                        Mathf.Lerp(_origAmbientIntensity, DuskAmbientIntensity, t)
                    );

                RenderSettings.ambientLight = Color.Lerp(_savedAmbientLight, DuskAmbientLight, t);
                RenderSettings.fogColor = Color.Lerp(_savedFogColor, DuskFogColor, t);
                RenderSettings.fogDensity = Mathf.Lerp(_savedFogDensity, DuskFogDensity, t);
                if (_sunLight != null)
                {
                    _sunLight.color = Color.Lerp(_savedSunColor, DuskSunLightColor, t);
                    _sunLight.intensity = Mathf.Lerp(_savedSunIntensity, DuskSunLightIntensity, t);
                }

                yield return null;
            }
        }

        private static IEnumerator DuskFadeOut()
        {
            if (_duskSkyInstance == null)
                yield break;

            // Snapshot current values so we fade from where we actually are —
            // handles sessions ending mid-approach before fade-in completes.
            bool hasSkyColor = _duskSkyInstance.HasProperty("_SkyColor");
            bool hasHorizonColor = _duskSkyInstance.HasProperty("_HorizonColor");
            bool hasSunDirection = _duskSkyInstance.HasProperty("_SunDirection");
            bool hasMoonCycle = _duskSkyInstance.HasProperty("_MoonCycle");
            bool hasStarsExposure = _duskSkyInstance.HasProperty("_StarsExposure");
            bool hasAmbientIntensity = _duskSkyInstance.HasProperty("_AmbientIntensity");

            Color fromSkyColor = hasSkyColor
                ? _duskSkyInstance.GetColor("_SkyColor")
                : _origSkyColor;
            Color fromHorizonColor = hasHorizonColor
                ? _duskSkyInstance.GetColor("_HorizonColor")
                : _origHorizonColor;
            Vector4 fromSunDirection = hasSunDirection
                ? _duskSkyInstance.GetVector("_SunDirection")
                : _origSunDirection;
            float fromMoonCycle = hasMoonCycle
                ? _duskSkyInstance.GetFloat("_MoonCycle")
                : _origMoonCycle;
            float fromStarsExposure = hasStarsExposure
                ? _duskSkyInstance.GetFloat("_StarsExposure")
                : _origStarsExposure;
            float fromAmbientIntensity = hasAmbientIntensity
                ? _duskSkyInstance.GetFloat("_AmbientIntensity")
                : _origAmbientIntensity;

            Color fromAmbientLight = RenderSettings.ambientLight;
            Color fromFogColor = RenderSettings.fogColor;
            float fromFogDensity = RenderSettings.fogDensity;
            Color fromSunColor = _sunLight != null ? _sunLight.color : _savedSunColor;
            float fromSunIntensity = _sunLight != null ? _sunLight.intensity : _savedSunIntensity;

            float duration = ModConfig.Moon.DuskFadeDuration.Value;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                if (_duskSkyInstance == null)
                    yield break;

                if (hasSkyColor)
                    _duskSkyInstance.SetColor(
                        "_SkyColor",
                        Color.Lerp(fromSkyColor, _origSkyColor, t)
                    );
                if (hasHorizonColor)
                    _duskSkyInstance.SetColor(
                        "_HorizonColor",
                        Color.Lerp(fromHorizonColor, _origHorizonColor, t)
                    );
                if (hasSunDirection)
                    _duskSkyInstance.SetVector(
                        "_SunDirection",
                        Vector4.Lerp(fromSunDirection, _origSunDirection, t)
                    );
                if (hasMoonCycle)
                    _duskSkyInstance.SetFloat(
                        "_MoonCycle",
                        Mathf.Lerp(fromMoonCycle, _origMoonCycle, t)
                    );
                if (hasStarsExposure)
                    _duskSkyInstance.SetFloat(
                        "_StarsExposure",
                        Mathf.Lerp(fromStarsExposure, _origStarsExposure, t)
                    );
                if (hasAmbientIntensity)
                    _duskSkyInstance.SetFloat(
                        "_AmbientIntensity",
                        Mathf.Lerp(fromAmbientIntensity, _origAmbientIntensity, t)
                    );

                RenderSettings.ambientLight = Color.Lerp(fromAmbientLight, _savedAmbientLight, t);
                RenderSettings.fogColor = Color.Lerp(fromFogColor, _savedFogColor, t);
                RenderSettings.fogDensity = Mathf.Lerp(fromFogDensity, _savedFogDensity, t);
                if (_sunLight != null)
                {
                    _sunLight.color = Color.Lerp(fromSunColor, _savedSunColor, t);
                    _sunLight.intensity = Mathf.Lerp(fromSunIntensity, _savedSunIntensity, t);
                }

                yield return null;
            }

            ClearDuskState();
        }

        /// Destroys all active sessions. Called by MoonNetworkBridge.ClientHoleCleanup().
        public static void ClearAll()
        {
            RestoreSkyboxImmediate();
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

            BeginDuskEffect(localInfo.Movement);

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

            // Detach the VFX so EndSessionInternal doesn't destroy it — we'll fly it away instead.
            float flyDuration = ModConfig.Moon.FlyAwayDuration.Value;
            GameObject flyVfx = null;
            MonoBehaviour flyHost = null;
            if (flyDuration > 0f && state.MoonVfxInstance != null && state.ForceMovement != null)
            {
                flyVfx = state.MoonVfxInstance;
                flyHost = state.ForceMovement;
                state.MoonVfxInstance = null;
            }

            EndDuskEffect();
            EndSessionInternal(wielderNetId);

            if (flyVfx != null)
                flyHost.StartCoroutine(
                    FlyAwayCoroutine(
                        flyVfx,
                        explosionPos,
                        state.MoonSpawnPos,
                        state.FinalScale,
                        state.InitialScale,
                        flyDuration
                    )
                );
        }

        private static IEnumerator FlyAwayCoroutine(
            GameObject vfx,
            Vector3 from,
            Vector3 to,
            float fromScale,
            float toScale,
            float duration
        )
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (vfx == null)
                    yield break;

                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                t *= t; // ease in — accelerates as it leaves
                vfx.transform.position = Vector3.Lerp(from, to, t);
                vfx.transform.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, t);
                yield return null;
            }

            if (vfx != null)
                Object.Destroy(vfx);
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

            uint localNetId = localInfo.GetComponent<NetworkIdentity>()?.netId ?? 0u;
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
                ItemType.RocketLauncher,
                false
            );
            bool _;

            localInfo.Movement.TryKnockOut(
                wielderInfo,
                KnockoutType.Rocket,
                false,
                localInfo.Movement.transform.InverseTransformPoint(state.MoonImpactPos),
                Vector3.Distance(localInfo.transform.position, state.MoonImpactPos),
                Vector3.zero,
                ElectromagnetShieldHitBlockType.FullyBlocked,
                useId,
                false,
                true,
                out _,
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
