using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Server side:  Validates the throw, consumes the item, spawns and
    ///               configures the jar GameObject.
    ///
    /// Client side:  Derives throw velocity from the camera and sends
    ///               PoisonJarThrowMessage.  Also hosts the static handler for
    ///               PoisonJarLandedMessage which spawns local VFX and activates
    ///               the poison (poison) overlay on affected players.
    /// </summary>
    public class PoisonJarNetworkBridge : NetworkBridgeBase
    {
        /// Metres above <see cref="PlayerInfo.HeadBone"/>. Same offset as the glove ball icon.
        private const float PoisonedVfxHeightAboveHead = 1.1f;

        /// Metres above the player root when that player has no head bone.
        private const float PoisonedVfxHeightAboveRoot = 2.2f;

        // ── Client → Server ────────────────────────────────────────────────

        /// <summary>
        /// Called from PoisonJarItemDefinition.OnUse on the local client.
        /// Computes throw parameters from the main camera and sends them to the server.
        /// </summary>
        public void ClientThrow()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                IssaPluginPlugin.Log.LogWarning("[PoisonJar] No main camera for throw direction.");
                return;
            }

            Vector3 forward = cam.transform.forward;
            Vector3 throwOrigin = cam.transform.position + forward * 1.2f + Vector3.up * 0.3f;
            Vector3 throwDir = (
                forward + Vector3.up * ModConfig.PoisonJar.LobAngle.Value
            ).normalized;
            Vector3 velocity = throwDir * ModConfig.PoisonJar.ThrowSpeed.Value;

            NetworkClient.Send(
                new PoisonJarThrowMessage { ThrowOrigin = throwOrigin, ThrowVelocity = velocity }
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[PoisonJar] Client throw: origin={throwOrigin} speed={velocity.magnitude:F1}"
            );
        }

        // ── Server handler (called from NetworkManagerPatches) ─────────────

        public void ServerHandleThrow(Vector3 throwOrigin, Vector3 throwVelocity)
        {
            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogError("[PoisonJar] No PlayerInventory on bridge object.");
                return;
            }

            if (inventory.GetEffectivelyEquippedItem(true) != ItemRegistry.PoisonJarItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[PoisonJar] Player does not have PoisonJar equipped."
                );
                return;
            }

            if (AssetLoader.PoisonJarPrefab == null)
            {
                IssaPluginPlugin.Log.LogError("[PoisonJar] Jar prefab not loaded.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            // Clamp server-side to prevent speed exploits
            Vector3 velocity = Vector3.ClampMagnitude(
                throwVelocity,
                ModConfig.PoisonJar.ThrowSpeed.Value * 1.5f
            );

            var jarGo = Object.Instantiate(
                AssetLoader.PoisonJarPrefab,
                throwOrigin,
                velocity.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(velocity.normalized, Vector3.up)
                    : Quaternion.identity
            );

            if (jarGo == null)
            {
                IssaPluginPlugin.Log.LogError("[PoisonJar] Jar prefab failed to instantiate.");
                return;
            }

            var rb = jarGo.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = true;
                rb.isKinematic = false;
                rb.linearVelocity = velocity;

                Vector3 spinAxis = Vector3.Cross(velocity.normalized, Vector3.up).normalized;
                if (spinAxis.sqrMagnitude < 0.01f)
                    spinAxis = Vector3.right;
                rb.angularVelocity = spinAxis * 8f;
            }

            NetworkServer.Spawn(jarGo);

            var behaviour = jarGo.AddComponent<PoisonJarBehaviour>();
            behaviour.ThrowerNetId = netId;

            IssaPluginPlugin.Log.LogInfo(
                $"[PoisonJar] Jar thrown from {throwOrigin} speed={velocity.magnitude:F1} m/s"
            );
        }

        // ── Server → All Clients ───────────────────────────────────────────

        /// <summary>
        /// Called on every client when the server reports the jar has landed.
        /// Spawns a local splash VFX, a one-shot burst above each poisoned player,
        /// and activates the poison overlay when the local player was poisoned.
        /// </summary>
        public static void HandleLanded(PoisonJarLandedMessage msg)
        {
            // Splash scale is 1 when the landed radius matches the configured radius.
            var splashPrefab = AssetLoader.PoisonSplashPrefab;
            if (splashPrefab != null)
            {
                float scale = msg.Radius / ModConfig.PoisonJar.Radius.Value;
                var splash = Object.Instantiate(splashPrefab, msg.Position, Quaternion.identity);
                splash.transform.localScale = Vector3.one * scale;
                Object.Destroy(splash, 6f);
            }

            SpawnPoisonedPlayerVfx(msg.PoisonedNetIds);

            var localIdentity = NetworkClient.localPlayer;
            if (localIdentity == null || !WasPoisoned(msg.PoisonedNetIds, localIdentity.netId))
                return;

            IssaPluginPlugin.Log.LogInfo(
                $"[PoisonJar] Local player poisoned for {msg.Duration:F1}s"
            );
            PoisonOverlay.Instance?.ActivatePoison(msg.Duration);
        }

        /// <summary>
        /// Local-only burst above each player the server poisoned. Each client
        /// spawns its own copy so the thrower can see who was hit.
        /// </summary>
        private static void SpawnPoisonedPlayerVfx(uint[] netIds)
        {
            var prefab = AssetLoader.PoisonedPlayerVfxPrefab;
            if (prefab == null || netIds == null || netIds.Length == 0)
                return;

            for (int i = 0; i < netIds.Length; i++)
            {
                if (
                    !NetworkClient.spawned.TryGetValue(netIds[i], out var identity)
                    || identity == null
                )
                    continue;

                var info = identity.GetComponent<PlayerInfo>();
                if (info == null)
                    continue;

                AttachPoisonVfx(info, prefab);
            }
        }

        private static bool WasPoisoned(uint[] netIds, uint netId)
        {
            if (netIds == null)
                return false;

            for (int i = 0; i < netIds.Length; i++)
            {
                if (netIds[i] == netId)
                    return true;
            }

            return false;
        }

        private static void AttachPoisonVfx(PlayerInfo playerInfo, GameObject prefab)
        {
            Transform anchor =
                playerInfo.HeadBone != null ? playerInfo.HeadBone : playerInfo.transform;
            float height =
                playerInfo.HeadBone != null
                    ? PoisonedVfxHeightAboveHead
                    : PoisonedVfxHeightAboveRoot;
            Vector3 localOffset = new Vector3(0f, height, 0f);

            // Position is applied before Awake, so Play On Awake emits above the head.
            var vfx = Object.Instantiate(
                prefab,
                anchor.TransformPoint(localOffset),
                anchor.rotation,
                anchor
            );

            foreach (var col in vfx.GetComponentsInChildren<Collider>(true))
                col.enabled = false;

            Object.Destroy(vfx, OneShotLifetime(vfx));
        }

        /// <summary>
        /// Seconds until the last particle of a one-shot prefab dies, including
        /// curve lifetimes and trails. The prefab is left to play on its own.
        /// </summary>
        private static float OneShotLifetime(GameObject vfx)
        {
            float lifetime = 0f;
            var systems = vfx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
                lifetime = Mathf.Max(lifetime, OneSystemLifetime(systems[i]));

            if (lifetime <= 0f)
                lifetime = 4f;

            return lifetime + 0.25f;
        }

        private static float OneSystemLifetime(ParticleSystem ps)
        {
            var main = ps.main;
            float seconds =
                UpperBound(main.startDelay) + main.duration + UpperBound(main.startLifetime);
            if (ps.trails.enabled)
                seconds += UpperBound(ps.trails.lifetime);

            float speed = main.simulationSpeed;
            if (speed < 0.01f)
                speed = 0.01f;
            return seconds / speed;
        }

        private static float UpperBound(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.TwoConstants:
                    return curve.constantMax;
                case ParticleSystemCurveMode.Curve:
                    return curve.curveMultiplier * CurvePeak(curve.curve);
                case ParticleSystemCurveMode.TwoCurves:
                    return curve.curveMultiplier
                        * Mathf.Max(CurvePeak(curve.curveMin), CurvePeak(curve.curveMax));
                default:
                    return curve.constant;
            }
        }

        private static float CurvePeak(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
                return 1f;

            float start = curve.keys[0].time;
            float end = curve.keys[curve.length - 1].time;
            float peak = 0f;
            const int steps = 8;
            for (int i = 0; i <= steps; i++)
            {
                float t = Mathf.Lerp(start, end, i / (float)steps);
                peak = Mathf.Max(peak, curve.Evaluate(t));
            }

            return peak;
        }

        // ── NetworkBridgeBase ──────────────────────────────────────────────

        public override void ServerHoleCleanup() { }

        public override void ClientHoleCleanup() { }
    }
}
