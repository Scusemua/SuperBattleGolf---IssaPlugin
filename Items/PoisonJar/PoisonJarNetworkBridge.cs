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
            Vector3 throwDir = (forward + Vector3.up * Configuration.PoisonJarLobAngle.Value).normalized;
            Vector3 velocity = throwDir * Configuration.PoisonJarThrowSpeed.Value;

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
                IssaPluginPlugin.Log.LogWarning("[PoisonJar] Player does not have PoisonJar equipped.");
                return;
            }

            if (AssetLoader.PoisonJarPrefab == null)
            {
                IssaPluginPlugin.Log.LogError("[PoisonJar] Jar prefab not loaded.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            // Clamp server-side to prevent speed exploits
            Vector3 velocity = Vector3.ClampMagnitude(throwVelocity, Configuration.PoisonJarThrowSpeed.Value * 1.5f);

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
        /// Spawns a local splash VFX (scaled to the poison radius) and activates
        /// the poison overlay on the local player if they are within range.
        /// </summary>
        public static void HandleLanded(PoisonJarLandedMessage msg)
        {
            // Spawn local-only splash VFX, scaled proportionally to the AoE radius.
            // The default radius (8 m) maps to a scale of 1; larger/smaller radii scale linearly.
            var splashPrefab = AssetLoader.PoisonSplashPrefab;
            if (splashPrefab != null)
            {
                float scale = msg.Radius / Configuration.PoisonJarRadius.Value;
                var splash = Object.Instantiate(splashPrefab, msg.Position, Quaternion.identity);
                splash.transform.localScale = Vector3.one * scale;
                Object.Destroy(splash, 6f);
            }

            // Check if the local player is within the poison radius.
            var localIdentity = NetworkClient.localPlayer;
            if (localIdentity == null)
                return;

            float sqrDist = (localIdentity.transform.position - msg.Position).sqrMagnitude;
            if (sqrDist <= msg.Radius * msg.Radius)
            {
                IssaPluginPlugin.Log.LogInfo(
                    $"[PoisonJar] Local player poisoned for {msg.Duration:F1}s"
                );
                PoisonOverlay.Instance?.ActivatePoison(msg.Duration);
            }
        }

        // ── NetworkBridgeBase ──────────────────────────────────────────────

        public override void ServerHoleCleanup() { }

        public override void ClientHoleCleanup() { }
    }
}
