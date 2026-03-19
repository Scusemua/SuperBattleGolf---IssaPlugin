using System.Reflection;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Server side:  Validates the throw, consumes the item, spawns and
    ///               configures the grenade GameObject.
    ///
    /// Client side:  Derives throw velocity from the camera and sends
    ///               StickyGrenadeThrowMessage.  Also hosts the static handlers for
    ///               StickyGrenadeStuckMessage and StickyGrenadeDetonatedMessage.
    /// </summary>
    public class StickyGrenadeNetworkBridge : NetworkBehaviour
    {
        // ----------------------------------------------------------------
        //  Server state
        // ----------------------------------------------------------------

        private bool _serverThrowActive;

        // ----------------------------------------------------------------
        //  Client → Server  (called from message handler in NetworkManagerPatches)
        // ----------------------------------------------------------------

        public void ServerHandleThrow(Vector3 throwOrigin, Vector3 throwVelocity)
        {
            if (_serverThrowActive)
            {
                IssaPluginPlugin.Log.LogWarning("[StickyGrenade] Throw already in progress.");
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[StickyGrenade] No PlayerInventory on bridge object."
                );
                return;
            }

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != StickyGrenadeItem.StickyGrenadeItemType)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[StickyGrenade] Player does not have StickyGrenade equipped."
                );
                return;
            }

            if (AssetLoader.StickyGrenadePrefab == null)
            {
                IssaPluginPlugin.Log.LogError("[StickyGrenade] Grenade prefab not loaded.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            // Clamp throw speed server-side to prevent exploits
            float maxSpeed = Configuration.StickyGrenadeMaxThrowSpeed.Value;
            Vector3 velocity = Vector3.ClampMagnitude(throwVelocity, maxSpeed);

            var grenadeGo = Object.Instantiate(
                AssetLoader.StickyGrenadePrefab,
                throwOrigin,
                Quaternion.LookRotation(velocity.normalized, Vector3.up)
            );

            if (grenadeGo == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[StickyGrenade] Grenade prefab failed to instantiate."
                );
                return;
            }

            // Apply initial velocity before Spawn so the first NetworkTransform
            // update already has the correct velocity baked in.
            var rb = grenadeGo.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.useGravity = true;
                rb.isKinematic = false;
                rb.linearVelocity = velocity;
            }

            NetworkServer.Spawn(grenadeGo);

            // Attach the server-side flight/stick/fuse behaviour
            var behaviour = grenadeGo.AddComponent<StickyGrenadeBehaviour>();
            behaviour.ThrowerInfo = inventory.PlayerInfo;
            behaviour.GraceTime = Configuration.StickyGrenadeGraceTime.Value;
            behaviour.FuseTime = Configuration.StickyGrenadeFuseTime.Value;
            behaviour.StickRadius = Configuration.StickyGrenadeStickRadius.Value;
            behaviour.ExplosionScale = Configuration.StickyGrenadeExplosionScale.Value;

            _serverThrowActive = false; // allow immediate re-throw (uses consumed)

            IssaPluginPlugin.Log.LogInfo(
                $"[StickyGrenade] Grenade thrown from {throwOrigin} velocity={velocity.magnitude:F1} m/s"
            );
        }

        // ----------------------------------------------------------------
        //  Client throw — called from TryUseItemPatch
        // ----------------------------------------------------------------

        /// <summary>
        /// Derives the throw parameters from the local camera and sends
        /// StickyGrenadeThrowMessage to the server.
        /// </summary>
        public void ClientThrow()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[StickyGrenade] No main camera for throw direction."
                );
                return;
            }

            // Spawn slightly in front and above the camera so the grenade clears
            // the player's own body colliders from the first frame.
            Vector3 forward = cam.transform.forward;
            Vector3 throwOrigin = cam.transform.position + forward * 1.2f + Vector3.up * 0.3f;

            // Mix forward with a slight upward arc so the grenade lobs naturally
            Vector3 throwDir = (
                forward + Vector3.up * Configuration.StickyGrenadeLobAngle.Value
            ).normalized;
            Vector3 velocity = throwDir * Configuration.StickyGrenadeThrowSpeed.Value;

            NetworkClient.Send(
                new StickyGrenadeThrowMessage
                {
                    ThrowOrigin = throwOrigin,
                    ThrowVelocity = velocity,
                }
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[StickyGrenade] Client throw: origin={throwOrigin} speed={velocity.magnitude:F1}"
            );
        }

        // ----------------------------------------------------------------
        //  Server → All Clients message handlers
        //  (static — registered in NetworkManagerPatches, no instance needed)
        // ----------------------------------------------------------------

        /// Called on every client when the server reports the grenade has stuck.
        public static void HandleStickyGrenadeStuck(StickyGrenadeStuckMessage msg)
        {
            // Find the grenade on this client
            if (
                !NetworkClient.spawned.TryGetValue(msg.GrenadeNetId, out var grenadeNi)
                || grenadeNi == null
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[StickyGrenade] HandleStickyGrenadeStuck: grenade netId={msg.GrenadeNetId} not found."
                );
                return;
            }

            var setup = grenadeNi.GetComponent<StickyGrenadeClientSetup>();
            if (setup == null)
                return;

            Transform stickyTarget = null;

            if (msg.TargetNetId != 0)
            {
                // Moving target — resolve its transform from spawned objects
                if (
                    NetworkClient.spawned.TryGetValue(msg.TargetNetId, out var targetNi)
                    && targetNi != null
                )
                {
                    stickyTarget = targetNi.transform;
                }
            }

            // If TargetNetId == 0 or target not found: grenade is world-static.
            // StickyGrenadeClientSetup.OnStuck with null target holds the last NetworkTransform
            // position, which is the stuck world position.
            setup.OnStuck(stickyTarget, msg.LocalOffset, msg.FuseRemaining);
        }

        /// Called on every client just before the grenade explodes.
        public static void HandleStickyGrenadeDetonated(StickyGrenadeDetonatedMessage msg)
        {
            // Play a client-local VFX at the blast position so the explosion is
            // visible even if Mirror's Destroy message arrives first.
            VfxManager.PlayPooledVfxLocalOnly(
                VfxType.RocketLauncherRocketExplosion,
                msg.WorldPosition,
                Quaternion.identity,
                Vector3.one * Configuration.StickyGrenadeExplosionScale.Value
            );

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                msg.WorldPosition
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[StickyGrenade] Client: detonation VFX at {msg.WorldPosition}"
            );
        }
    }
}
