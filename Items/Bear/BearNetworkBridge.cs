using System.Collections;
using System.Collections.Generic;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches (AddBridgeComponentsPatch).
    ///
    /// SERVER SIDE:
    ///   Validates the item use, consumes it, spawns BearCount bear GameObjects,
    ///   attaches BearBehaviour and BearHitReceiver to each, and tracks them for
    ///   cleanup when the session ends or a hole transition occurs.
    ///   Sends overlay begin/bear-killed messages to the owning client only
    ///   (TargetRpc replacement via connectionToClient.Send).
    ///
    /// CLIENT SIDE (owning player only):
    ///   Sends BearSummonMessage when the player activates the item.
    ///   Hosts the static BearStateMessage and impact handlers that route incoming
    ///   updates to the correct components on each client.
    ///
    /// Multiple players may have concurrent bear sessions — there is no global lock.
    /// Each BearNetworkBridge instance tracks only its own summoner's bears.
    /// </summary>
    public class BearNetworkBridge : NetworkBridgeBase
    {
        // ── Server state ──────────────────────────────────────────────────────

        /// <summary>
        /// Per-player record of bears already hit via <see cref="BearSwingHitMessage"/>
        /// during the current swing.  Keyed by (PlayerInfo, bear GameObject) so
        /// concurrent swings by different players do not interfere.
        /// Populated by <see cref="ServerHandleSwingHit"/>; cleared per-player by
        /// GolfClubBearHitPatch in BearWeaponHitPatches on OnFinishedSwinging.
        /// </summary>
        internal static readonly HashSet<(PlayerInfo, GameObject)> SwingHitPairs = [];

        private readonly List<GameObject> _activeBears = new List<GameObject>();
        private bool _serverSessionActive;
        private Coroutine _serverTimeout;

        /// <summary>
        /// Set by ServerPrepareBearRocket() each frame while a client is locked
        /// onto a bear. Consumed by BearRocketHomingPatch when the next rocket spawns.
        /// </summary>
        public bool PendingBearHoming;

        // ── Mirror lifecycle ──────────────────────────────────────────────────

        public override void OnStopServer()
        {
            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[Bear] Player disconnected during bear session — cleaning up."
                );
                ForceServerCleanup();
            }
        }

        // ── Client → Server ───────────────────────────────────────────────────

        /// <summary>
        /// Called on the server when the client sends BearSummonMessage.
        /// Validates ownership, consumes item, spawns bears.
        /// </summary>
        public void ServerSummonBears()
        {
            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (equipped != ItemRegistry.BearItemType)
            {
                IssaPluginPlugin.Log.LogWarning("[Bear] Player does not have Bear item equipped.");
                return;
            }

            if (AssetLoader.BearPrefab == null)
            {
                IssaPluginPlugin.Log.LogError("[Bear] Bear prefab not loaded.");
                return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);
            ItemWarningBroadcaster.Broadcast(
                inventory.PlayerInfo.PlayerId.PlayerName,
                ItemRegistry.BearItemType,
                "Bear",
                senderNetId: netId
            );

            int bearCount = (int)ModConfig.Bear.Count.Value;
            float spawnRadius = ModConfig.Bear.SpawnRadius.Value;
            float sessionDur = ModConfig.Bear.SessionDuration.Value;
            Vector3 playerPos = inventory.PlayerInfo.transform.position;

            for (int i = 0; i < bearCount; i++)
            {
                // Distribute bears evenly around the player with randomised angular spread
                float angle = (360f / bearCount) * i + Random.Range(-20f, 20f);
                float radius = Random.Range(spawnRadius * 0.6f, spawnRadius);
                float rad = angle * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
                Vector3 spawnPos = SnapToGround(playerPos + offset);

                SpawnOneBear(spawnPos, inventory.PlayerInfo);
            }

            _serverSessionActive = true;
            _serverTimeout = StartCoroutine(ServerTimeoutRoutine());

            // Tell only the owning client to show the bear HUD.
            // connectionToClient.Send() is the TargetRpc replacement for unweaved mods.
            connectionToClient.Send(
                new BearOverlayBeginMessage { BearCount = bearCount, SessionDuration = sessionDur }
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[Bear] Spawned {bearCount} bear(s) for "
                    + $"{inventory.PlayerInfo.PlayerId.PlayerName}."
            );
        }

        /// <summary>
        /// Called by the server-side BearPrepareHomingMessage handler.
        /// Flags this bridge so the next rocket spawned by this player
        /// homes toward the nearest bear.
        /// </summary>
        public void ServerPrepareBearRocket()
        {
            PendingBearHoming = true;
        }

        // ── Server internals ──────────────────────────────────────────────────

        private void SpawnOneBear(Vector3 position, PlayerInfo summoner)
        {
            var bearGo = Object.Instantiate(
                AssetLoader.BearPrefab,
                position,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
            );

            // The bear's NetworkTransform must be server-authoritative. Mirror's
            // default for NetworkTransformBase is SyncDirection.ClientToServer,
            // and the prefab carries that default — on a host (server + client in
            // one process) the local client then owns the transform and its
            // snapshot interpolation overwrites the server AI's MovePosition
            // every tick. The bear animates and rotates but never travels.
            // Must be set before NetworkServer.Spawn so the first sync is correct.
            foreach (var nt in bearGo.GetComponentsInChildren<NetworkTransformBase>(true))
            {
                nt.syncDirection = SyncDirection.ServerToClient;
                nt.syncPosition = true;
                nt.syncRotation = true;
            }

            // Hit receiver must be added before BearBehaviour so that
            // BearBehaviour.Start() can wire its event subscriptions
            var hitReceiver = bearGo.AddComponent<BearHitReceiver>();

            var behaviour = bearGo.AddComponent<BearBehaviour>();
            behaviour.SummonerInfo = summoner;
            behaviour.HitReceiver = hitReceiver;
            hitReceiver.Behaviour = behaviour;

            // Spawn AFTER full setup so Unity calls Start() post-Spawn, not pre-Spawn
            NetworkServer.Spawn(bearGo);

            _activeBears.Add(bearGo);

            // Per-bear lifecycle coroutine watches for its destruction
            StartCoroutine(WatchBear(bearGo));
        }

        private IEnumerator WatchBear(GameObject bearGo)
        {
            while (bearGo != null)
                yield return null;

            _activeBears.Remove(bearGo);

            // Notify owning client a bear was killed (HUD pip update)
            if (connectionToClient != null)
                connectionToClient.Send(new BearKilledClientMessage());

            // End session automatically once all bears are gone
            if (_serverSessionActive && _activeBears.Count == 0)
            {
                IssaPluginPlugin.Log.LogInfo("[Bear] All bears defeated — session ending.");
                EndServerSession();
            }
        }

        private IEnumerator ServerTimeoutRoutine()
        {
            yield return new WaitForSeconds(ModConfig.Bear.SessionDuration.Value);

            if (_serverSessionActive)
            {
                IssaPluginPlugin.Log.LogInfo("[Bear] Session timed out.");
                EndServerSession();
            }
        }

        private void EndServerSession()
        {
            if (!_serverSessionActive)
                return;

            _serverSessionActive = false;

            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            // Copy then clear BEFORE destroying so WatchBear coroutines that wake
            // from the forced destroy see an empty list and do not re-enter.
            var toDestroy = new List<GameObject>(_activeBears);
            _activeBears.Clear();

            foreach (var bear in toDestroy)
            {
                if (bear != null)
                    NetworkServer.Destroy(bear);
            }

            // Broadcast session-end to all clients (VFX / audio cleanup)
            NetworkServer.SendToAll(new BearHuntEndedMessage());

            // Tell owning client to hide its HUD
            if (connectionToClient != null)
                connectionToClient.Send(new BearOverlayEndMessage());

            IssaPluginPlugin.Log.LogInfo("[Bear] Server session ended.");
        }

        // ── Hole transition cleanup ───────────────────────────────────────────

        /// <summary>
        /// Called by Plugin.OnMatchStateChanged when a new hole begins.
        /// Destroys any active bears so they don't carry over between holes.
        /// </summary>
        public override void ServerHoleCleanup()
        {
            if (_serverSessionActive)
                ForceServerCleanup();
        }

        public override void ClientHoleCleanup() { }

        private void ForceServerCleanup()
        {
            if (_serverTimeout != null)
            {
                StopCoroutine(_serverTimeout);
                _serverTimeout = null;
            }

            foreach (var bear in _activeBears)
            {
                if (bear != null)
                    NetworkServer.Destroy(bear);
            }
            _activeBears.Clear();

            _serverSessionActive = false;

            if (connectionToClient != null)
                connectionToClient.Send(new BearOverlayEndMessage());
        }

        // ── Static NetworkClient message handlers ─────────────────────────────
        // Registered in NetworkManagerPatches.RegisterNetworkMessages().
        // Static — no BearNetworkBridge instance needed on the receiving client.

        /// Routes BearStateMessage to the correct BearAnimatorDriver by netId.
        public static void HandleBearState(BearStateMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.BearNetId, out var ni) || ni == null)
                return;

            ni.GetComponent<BearAnimatorDriver>()?.ApplyState(msg.State, msg.TargetPosition);
        }

        /// Plays camera shake on all clients when a bear lands a hit.
        public static void HandleBearAttackImpact(BearAttackImpactMessage msg)
        {
            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                msg.ImpactPosition
            );
        }

        /// Received by all clients when the session ends.
        /// Currently only logs — reserved for future client-side VFX / audio cleanup.
        /// If no VFX is ever added, remove this message and its registration in
        /// NetworkManagerPatches_BearAdditions.cs to reduce network overhead.
        public static void HandleBearSessionEnd(BearHuntEndedMessage msg)
        {
            IssaPluginPlugin.Log.LogInfo("[Bear] Session ended (all clients).");
        }

        // ── Per-client overlay messages (owning client only) ──────────────────

        /// Shows the bear HUD on the summoning player's screen.
        public static void HandleBearOverlayBegin(BearOverlayBeginMessage msg)
        {
            IssaPlugin.Overlays.BearOverlay.Instance?.SetActive(
                true,
                msg.BearCount,
                msg.SessionDuration
            );
        }

        /// Decrements the pip count in the summoning player's HUD.
        public static void HandleBearKilledClient(BearKilledClientMessage msg)
        {
            IssaPlugin.Overlays.BearOverlay.Instance?.DecrementBearCount();
        }

        /// Hides the summoning player's bear HUD.
        public static void HandleBearOverlayEnd(BearOverlayEndMessage msg)
        {
            IssaPlugin.Overlays.BearOverlay.Instance?.Deactivate();
        }

        // ── Server message handlers ───────────────────────────────────────────

        /// <summary>
        /// Server handler for <see cref="BearSwingHitMessage"/>.
        /// Validates proximity, deduplicates per-swing hits via <see cref="SwingHitPairs"/>,
        /// applies damage and knockback, and broadcasts the hit VFX to all clients.
        /// </summary>
        internal static void ServerHandleSwingHit(
            NetworkConnectionToClient conn,
            BearSwingHitMessage msg
        )
        {
            var playerInfo = conn.identity?.GetComponent<PlayerInfo>();
            if (playerInfo == null)
                return;

            if (!NetworkServer.spawned.TryGetValue(msg.BearNetId, out var bearNi) || bearNi == null)
                return;

            var receiver = bearNi.GetComponent<BearHitReceiver>();
            if (receiver == null)
                return;

            // Generous range check — the bear may have moved a little
            // between the client's detection and the server receiving the message.
            float dist = Vector3.Distance(playerInfo.transform.position, bearNi.transform.position);
            if (dist > ModConfig.Bear.MeleeHitRange.Value * 4f)
                return;

            // Record this (player, bear) pair so OnFinishedSwinging doesn't
            // double-hit the same bear for the same swing.
            if (!SwingHitPairs.Add((playerInfo, receiver.gameObject)))
                return; // duplicate message for the same bear this swing

            bool isBat =
                playerInfo.Inventory?.GetEffectivelyEquippedItem(true)
                == ItemRegistry.BaseballBatItemType;

            float damage = isBat
                ? ModConfig.Bear.DamageBaseballBat.Value
                : ModConfig.Bear.DamageGolfClub.Value;

            float knockbackForce = isBat
                ? ModConfig.Bear.BatKnockbackForce.Value
                : ModConfig.Bear.MeleeKnockbackForce.Value;

            Vector3 knockDir = (
                bearNi.transform.position - playerInfo.transform.position
            ).normalized;
            knockDir = (knockDir + Vector3.up * 0.5f).normalized;

            BearExplosionAttackerContext.CurrentAttacker = playerInfo;
            receiver.DealDamage(damage);
            BearExplosionAttackerContext.CurrentAttacker = null;
            receiver.Behaviour?.ApplyMeleeKnockback(knockDir, knockbackForce);

            NetworkServer.SendToAll(
                new BearHitVfxMessage
                {
                    HitPoint = bearNi.transform.position + Vector3.up * 1f,
                    AttackerOrigin = playerInfo.transform.position,
                }
            );

            IssaPluginPlugin.Log.LogInfo(
                $"[Bear] Hit by swing (client-reported) "
                    + $"from {playerInfo.PlayerId.PlayerName} for {damage} damage."
            );
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Vector3 SnapToGround(Vector3 pos)
        {
            Vector3 origin = pos + Vector3.up * 50f;

            if (
                Physics.Raycast(
                    origin,
                    Vector3.down,
                    out RaycastHit hit,
                    200f,
                    ItemHelper.GroundLayerMask
                )
            )
                return hit.point + Vector3.up * 0.1f;

            return pos;
        }
    }
}
