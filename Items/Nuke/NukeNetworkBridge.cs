using System.Collections;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Flow:
    ///   1. Client presses use  → sends NukeFireMessage.
    ///   2. Server receives it  → ServerHandleFire() starts ServerFireRoutine.
    ///   3. Server spawns NukeBombPrefab high above the map centre.
    ///   4. NukeBombBehaviour drives it straight down.
    ///   5. On ground impact, NukeBombBehaviour.Detonate() fires the explosion
    ///      (temp rocket + sky blast) and destroys the bomb.
    /// </summary>
    public class NukeNetworkBridge : NetworkBridgeBase
    {
        // ================================================================
        //  Server state
        // ================================================================

        private bool _serverRoutineActive;
        private Coroutine _serverRoutine;

        // ================================================================
        //  Client → Server
        // ================================================================

        public void ServerHandleFire()
        {
            if (_serverRoutineActive)
            {
                IssaPluginPlugin.Log.LogWarning("[Nuke] Server routine already active.");
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogError("[Nuke] No PlayerInventory on bridge object.");
                return;
            }

            _serverRoutineActive = true;
            _serverRoutine = StartCoroutine(ServerFireRoutine(inventory));
        }

        // ================================================================
        //  Server fire routine
        // ================================================================

        private IEnumerator ServerFireRoutine(PlayerInventory inventory)
        {
            ItemHelper.ConsumeEquippedItem(inventory);

            if (AssetLoader.NukeBombPrefab == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[Nuke] NukeBombPrefab not loaded — add 'nuke_bomb.prefab' to the asset bundle."
                );
                _serverRoutineActive = false;
                yield break;
            }

            float dropHeight = ModConfig.Nuke.DropHeight.Value;
            Vector3 mapCenter = inventory.transform.position;
            Vector3 spawnPos = new Vector3(mapCenter.x, mapCenter.y + dropHeight, mapCenter.z);

            var bombGo = Object.Instantiate(
                AssetLoader.NukeBombPrefab,
                spawnPos,
                Quaternion.identity
            );
            if (bombGo == null)
            {
                IssaPluginPlugin.Log.LogError("[Nuke] Failed to instantiate NukeBombPrefab.");
                _serverRoutineActive = false;
                yield break;
            }

            NetworkServer.Spawn(bombGo);

            // Warn all other players before the bomb reaches the ground.
            var playerInfo = inventory.PlayerInfo;
            ItemWarningBroadcaster.Broadcast(
                playerInfo.PlayerId.PlayerName,
                ItemRegistry.NukeItemType,
                "Nuke",
                trackedNetId: bombGo.GetComponent<NetworkIdentity>()?.netId ?? 0,
                senderNetId: netId
            );
            var itemUseId = new ItemUseId(
                playerInfo.PlayerId.Guid,
                NukeItem.NextUseIndex(),
                ItemType.RocketLauncher
            );

            // Attach the server-side falling + detonation behaviour.
            var behaviour = bombGo.AddComponent<NukeBombBehaviour>();
            behaviour.ThrowerInfo = playerInfo;
            behaviour.ThrowerRigidbody = GetComponentInParent<Rigidbody>();
            behaviour.ItemUseId = itemUseId;
            behaviour.DropSpeed = ModConfig.Nuke.DropSpeed.Value;
            behaviour.ExplosionScale = ModConfig.Nuke.ExplosionScale.Value;
            behaviour.SkyBlastForce = ModConfig.Nuke.SkyBlastForce.Value;
            behaviour.SkyBlastRadius = ModConfig.Nuke.SkyBlastRadius.Value;
            behaviour.SkyBlastVerticalBias = ModConfig.Nuke.SkyBlastVerticalBias.Value;

            IssaPluginPlugin.Log.LogInfo($"[Nuke] Bomb spawned at {spawnPos:F1}.");

            // Wait for the bomb to be destroyed by NukeBombBehaviour.Detonate(),
            // with a generous timeout in case something goes wrong.
            float dropTime = dropHeight / Mathf.Max(ModConfig.Nuke.DropSpeed.Value, 1f);
            float timeout = dropTime + 10f;
            float elapsed = 0f;

            while (bombGo != null && elapsed < timeout)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Force-detonate if the bomb survived to the timeout.
            if (bombGo != null)
            {
                var b = bombGo.GetComponent<NukeBombBehaviour>();
                b?.Detonate();
            }

            _serverRoutineActive = false;
            _serverRoutine = null;
            IssaPluginPlugin.Log.LogInfo("[Nuke] Server routine complete.");
        }

        // ================================================================
        //  Hole-transition cleanup
        // ================================================================

        public override void ServerHoleCleanup()
        {
            if (!_serverRoutineActive)
                return;

            if (_serverRoutine != null)
            {
                StopCoroutine(_serverRoutine);
                _serverRoutine = null;
            }

            _serverRoutineActive = false;
            IssaPluginPlugin.Log.LogInfo("[Nuke] Server state cleared on hole transition.");
        }

        public override void ClientHoleCleanup() /* Runs on client */
        { }

        // ================================================================
        //  NetworkMessage handlers (registered by NetworkManagerPatches)
        // ================================================================

        internal static void HandleNukeExplosion(NukeExplosionMessage msg)
        {
            // Explosion VFX — reuse the nuke fire prefab already in the bundle.
            if (AssetLoader.NukeExplosionVfxPrefab != null)
            {
                var vfx = Instantiate(
                    AssetLoader.NukeExplosionVfxPrefab,
                    msg.Position,
                    Quaternion.identity
                );
                vfx.transform.localScale = Vector3.one * msg.NukeExplosionVfxScale;
                Destroy(vfx, ModConfig.Nuke.ExplosionVfxDuration.Value);
            }
            else
            {
                // Fallback to the pooled rocket explosion if the prefab isn't loaded.
                VfxManager.PlayPooledVfxLocalOnly(
                    VfxType.RocketLauncherRocketExplosion,
                    msg.Position,
                    Quaternion.identity,
                    Vector3.one * msg.NukeExplosionVfxScale
                );
            }

            // Impact sound.
            if (AssetLoader.NukeExplosionClip != null)
            {
                var go = new GameObject("Nuke_Sound");
                var src = go.AddComponent<AudioSource>();
                src.clip = AssetLoader.NukeExplosionClip;
                src.spatialBlend = 0f;
                src.volume = 1f;
                src.Play();
                Destroy(go, AssetLoader.NukeExplosionClip.length + 0.1f);
            }

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                msg.Position
            );

            // Sky blast — apply the impulse and register the knockout on the local
            // player.  Each client runs this for themselves so TryKnockOut can call
            // CmdInformKnockedOut back to the server (a [Command] must originate from
            // the owning client).
            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null)
                return;

            if (ModConfig.Nuke.ExcludeThrower.Value && localInfo == msg.ThrowerInfo)
                return;

            var movement = localInfo.Movement;
            var rb = localInfo.GetComponentInParent<Rigidbody>();
            if (movement == null || rb == null)
                return;

            Vector3 toPlayer = movement.transform.position - msg.Position;
            Vector3 horizontal = new Vector3(toPlayer.x, 0f, toPlayer.z).normalized;
            Vector3 blastDir = Vector3
                .Lerp(horizontal, Vector3.up, msg.SkyBlastVerticalBias)
                .normalized;
            Vector3 velocityChange = blastDir * msg.SkyBlastForce;

            rb.AddForce(velocityChange, ForceMode.VelocityChange);

            bool _;
            movement.TryKnockOut(
                msg.ThrowerInfo,
                KnockoutType.Rocket,
                false, // isLegSweep
                movement.transform.InverseTransformPoint(msg.Position), // localOrigin
                toPlayer.magnitude, // distance
                velocityChange, // used for unground check
                true, // does not ignore electromagnetic shield
                msg.ItemUseId,
                false, // fromSpecialState
                true, // canFallbackToUnground
                out _
            );

            // Give players a speed boost.
            var playerMovement = GameManager.LocalPlayerMovement;
            if (playerMovement != null)
            {
                movement.StartCoroutine(ApplyCoffeeMovementSpeed(movement));
            }
        }

        internal static IEnumerator ApplyCoffeeMovementSpeed(PlayerMovement movement)
        {
            yield return new WaitForSeconds(3);
            movement.InformDrankCoffee();
            movement.InformDrankCoffee();
            movement.InformDrankCoffee();
        }
    }
}
