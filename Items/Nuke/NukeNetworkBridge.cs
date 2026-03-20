using System.Collections;
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
    public class NukeNetworkBridge : NetworkBehaviour
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

            float dropHeight = Configuration.NukeDropHeight.Value;
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

            // Build a unique ItemUseId for the temporary detonation rocket.
            var playerInfo = inventory.PlayerInfo;
            var itemUseId = new ItemUseId(
                playerInfo.PlayerId.Guid,
                NukeItem.NextUseIndex(),
                ItemType.RocketLauncher
            );

            // Attach the server-side falling + detonation behaviour.
            var behaviour = bombGo.AddComponent<NukeBombBehaviour>();
            behaviour.ThrowerInfo = playerInfo;
            behaviour.ItemUseId = itemUseId;
            behaviour.DropSpeed = Configuration.NukeDropSpeed.Value;
            behaviour.ExplosionScale = Configuration.NukeExplosionScale.Value;
            behaviour.SkyBlastForce = Configuration.NukeSkyBlastForce.Value;
            behaviour.SkyBlastRadius = Configuration.NukeSkyBlastRadius.Value;
            behaviour.SkyBlastUpwardModifier = Configuration.NukeSkyBlastUpwardModifier.Value;

            IssaPluginPlugin.Log.LogInfo($"[Nuke] Bomb spawned at {spawnPos:F1}.");

            // Wait for the bomb to be destroyed by NukeBombBehaviour.Detonate(),
            // with a generous timeout in case something goes wrong.
            float dropTime = dropHeight / Mathf.Max(Configuration.NukeDropSpeed.Value, 1f);
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
        //  Hole-transition cleanup  (server only)
        // ================================================================

        public void ServerHoleCleanup()
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
    }
}
