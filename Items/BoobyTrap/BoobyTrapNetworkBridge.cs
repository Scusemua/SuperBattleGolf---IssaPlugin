using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Owns all server-side booby-trap state (static dicts shared across all
    /// bridge instances) and handles the three incoming client messages:
    ///   BoobyTrapPlaceMessage    — trap a world object (item box / golf cart)
    ///   BoobyTrapDropItemMessage — drop an inventory item as a booby trap
    ///
    /// Also provides ServerMarkSlotAsTrapped / ServerTriggerDetonation helpers
    /// called from BoobyTrapPatches and DroppedCustomItem respectively.
    /// </summary>
    public class BoobyTrapNetworkBridge : NetworkBridgeBase
    {
        // ====================================================================
        //  Server-side state  (static — shared across all bridge instances)
        // ====================================================================

        // item-box netId  → trapper playerNetId
        private static readonly Dictionary<uint, uint> TrappedItemBoxes = new();

        // golf-cart netId → trapper playerNetId
        private static readonly Dictionary<uint, uint> TrappedGolfCarts = new();

        // victim player netId → set of trapped inventory-slot indices
        private static readonly Dictionary<uint, HashSet<int>> TrappedInventorySlots = new();

        // ====================================================================
        //  Client-side state  (local player only)
        // ====================================================================

        // Slot indices that the server has told THIS client are booby-trapped.
        // Cleared on hole transition.
        private static readonly HashSet<int> LocalTrappedSlots = new();

        private static readonly MethodInfo ServerExplodeMethod = typeof(Rocket).GetMethod(
            "ServerExplode",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // Reflected access to the private SyncList<InventorySlot> slots field.
        private static readonly FieldInfo SlotsField = AccessTools.Field(
            typeof(PlayerInventory),
            "slots"
        );

        // ====================================================================
        //  Internal slot helper
        // ====================================================================

        private static InventorySlot GetSlot(PlayerInventory inv, int index)
        {
            var slots = SlotsField?.GetValue(inv) as IList<InventorySlot>;
            if (slots == null || index < 0 || index >= slots.Count)
                return InventorySlot.Empty;
            return slots[index];
        }

        private static int GetSlotCount(PlayerInventory inv)
        {
            var slots = SlotsField?.GetValue(inv) as IList<InventorySlot>;
            return slots?.Count ?? 0;
        }

        // ====================================================================
        //  Public query helpers  (called from BoobyTrapPatches)
        // ====================================================================

        /// Returns true when the given item-box netId has an active trap and,
        /// if SelfDamage is disabled, the entering player is not the trapper.
        /// <paramref name="trapperNetId"/> is set to the trapper's netId on success (0 otherwise).
        public static bool TryConsumeItemBoxTrap(
            uint boxNetId,
            uint enteringPlayerNetId,
            out uint trapperNetId
        )
        {
            if (!TrappedItemBoxes.TryGetValue(boxNetId, out trapperNetId))
                return false;

            if (!ModConfig.BoobyTrap.SelfDamage.Value && enteringPlayerNetId == trapperNetId)
            {
                trapperNetId = 0;
                return false;
            }

            TrappedItemBoxes.Remove(boxNetId);
            return true;
        }

        /// Returns true when the given golf-cart netId has an active trap and,
        /// if SelfDamage is disabled, the entering player is not the trapper.
        /// <paramref name="trapperNetId"/> is set to the trapper's netId on success (0 otherwise).
        public static bool TryConsumeGolfCartTrap(
            uint cartNetId,
            uint enteringPlayerNetId,
            out uint trapperNetId
        )
        {
            if (!TrappedGolfCarts.TryGetValue(cartNetId, out trapperNetId))
                return false;

            if (!ModConfig.BoobyTrap.SelfDamage.Value && enteringPlayerNetId == trapperNetId)
            {
                trapperNetId = 0;
                return false;
            }

            TrappedGolfCarts.Remove(cartNetId);
            return true;
        }

        /// Returns true when the player has a trapped slot at the equipped index.
        /// Inventory traps do not track the original trapper — the "victim" is
        /// always a different player, so SelfDamage has no effect here.
        public static bool TryConsumeInventoryTrap(uint playerNetId, int slotIndex)
        {
            if (!TrappedInventorySlots.TryGetValue(playerNetId, out var slots))
                return false;

            if (!slots.Remove(slotIndex))
                return false;

            if (slots.Count == 0)
                TrappedInventorySlots.Remove(playerNetId);

            return true;
        }

        // ====================================================================
        //  Called by DroppedCustomItem.ServerPickup
        // ====================================================================

        public static void ServerMarkSlotAsTrapped(uint playerNetId, int slotIndex)
        {
            if (!TrappedInventorySlots.TryGetValue(playerNetId, out var slots))
            {
                slots = new HashSet<int>();
                TrappedInventorySlots[playerNetId] = slots;
            }

            slots.Add(slotIndex);

            // Notify the victim's client so TryUseItem can intercept before the item fires.
            if (
                NetworkServer.spawned.TryGetValue(playerNetId, out var ni)
                && ni.connectionToClient != null
            )
            {
                ni.connectionToClient.Send(
                    new BoobyTrapSlotMarkedMessage { SlotIndex = slotIndex }
                );
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[BoobyTrap] Marked slot {slotIndex} as trapped for player {playerNetId}."
            );
        }

        // ====================================================================
        //  Client-side slot-trap helpers
        // ====================================================================

        public static bool IsLocalSlotTrapped(int slotIndex) =>
            LocalTrappedSlots.Contains(slotIndex);

        public static void ClearLocalTrappedSlot(int slotIndex) =>
            LocalTrappedSlots.Remove(slotIndex);

        // ====================================================================
        //  Server detonation helper
        // ====================================================================

        /// <summary>
        /// Spawns a temporary Rocket at <paramref name="position"/>, scales its
        /// explosion, calls ServerExplode, then broadcasts the client VFX message.
        /// <paramref name="trapperInfo"/> may be null if the trapper disconnected.
        /// </summary>
        public static void ServerTriggerDetonation(Vector3 position, PlayerInfo trapperInfo)
        {
            if (!NetworkServer.active)
                return;

            ulong guid = trapperInfo != null ? trapperInfo.PlayerId.Guid : 0UL;
            var itemUseId = new ItemUseId(
                guid,
                BoobyTrapItem.NextUseIndex(),
                ItemType.RocketLauncher
            );

            NetworkServer.SendToAll(
                new BoobyTrapExplosionMessage
                {
                    Position = position,
                    TrapperInfo = trapperInfo,
                    ItemUseId = itemUseId,
                }
            );

            var rocketPrefab = GameManager.ItemSettings?.RocketPrefab;
            if (rocketPrefab == null)
            {
                IssaPluginPlugin.Log.LogError("[BoobyTrap] RocketPrefab is null.");
                return;
            }

            var tempRocket = Object.Instantiate(rocketPrefab, position, Quaternion.identity);
            if (tempRocket == null)
                return;

            tempRocket.gameObject.AddComponent<CustomSpawnedRocket>();
            tempRocket.ServerInitialize(trapperInfo, null, itemUseId);
            NetworkServer.Spawn(tempRocket.gameObject);
            ExplosionScaler.Register(tempRocket, ModConfig.BoobyTrap.ExplosionScale.Value);
            ServerExplodeMethod?.Invoke(tempRocket, new object[] { position });

            IssaPluginPlugin.Log.LogInfo($"[BoobyTrap] Detonated at {position:F1}.");
        }

        // ====================================================================
        //  Message handlers  (registered in NetworkManagerPatches)
        // ====================================================================

        internal static void ServerHandlePlaceMessage(
            NetworkConnectionToClient conn,
            BoobyTrapPlaceMessage msg
        )
        {
            if (!NetworkServer.active)
                return;

            var identity = conn.identity;
            var inventory = identity?.GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            // Server-side proximity validation.
            if (
                !NetworkServer.spawned.TryGetValue(msg.TargetNetId, out var targetNi)
                || targetNi == null
            )
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[BoobyTrap] Place rejected — target netId {msg.TargetNetId} not found."
                );
                return;
            }

            float range = ModConfig.BoobyTrap.Range.Value;
            float dist = Vector3.Distance(
                inventory.transform.position,
                targetNi.transform.position
            );
            if (dist > range * 1.5f) // 50 % server-side tolerance for latency
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[BoobyTrap] Place rejected — too far ({dist:F1} m, max {range:F1} m)."
                );
                return;
            }

            uint trapperNetId = identity.netId;

            switch (msg.TargetType)
            {
                case BoobyTrapTargetType.ItemBox:
                    if (targetNi.GetComponent<ItemSpawner>() == null)
                    {
                        IssaPluginPlugin.Log.LogWarning(
                            "[BoobyTrap] Place rejected — target has no ItemSpawner."
                        );
                        return;
                    }
                    TrappedItemBoxes[msg.TargetNetId] = trapperNetId;
                    IssaPluginPlugin.Log.LogInfo(
                        $"[BoobyTrap] Item box {msg.TargetNetId} trapped by player {trapperNetId}."
                    );
                    break;

                case BoobyTrapTargetType.GolfCart:
                    if (targetNi.GetComponent<GolfCartInfo>() == null)
                    {
                        IssaPluginPlugin.Log.LogWarning(
                            "[BoobyTrap] Place rejected — target has no GolfCartInfo."
                        );
                        return;
                    }
                    TrappedGolfCarts[msg.TargetNetId] = trapperNetId;
                    IssaPluginPlugin.Log.LogInfo(
                        $"[BoobyTrap] Golf cart {msg.TargetNetId} trapped by player {trapperNetId}."
                    );
                    break;

                default:
                    IssaPluginPlugin.Log.LogWarning(
                        $"[BoobyTrap] Unknown target type {msg.TargetType}."
                    );
                    return;
            }

            ItemHelper.ConsumeEquippedItem(inventory);

            // Notify the trapper only (local-only VFX).
            conn.Send(
                new BoobyTrapPlacedMessage
                {
                    TargetType = msg.TargetType,
                    TargetNetId = msg.TargetNetId,
                }
            );
        }

        internal static void ServerHandleDropItemMessage(
            NetworkConnectionToClient conn,
            BoobyTrapDropItemMessage msg
        )
        {
            if (!NetworkServer.active)
                return;

            var identity = conn.identity;
            var inventory = identity?.GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            int sourceSlot = msg.SourceSlotIndex;
            if (sourceSlot < 0)
                return;

            var slot = GetSlot(inventory, sourceSlot);

            if (slot.itemType == ItemType.None)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[BoobyTrap] Drop rejected — slot {sourceSlot} is empty."
                );
                return;
            }

            if (slot.itemType == ItemRegistry.BoobyTrapItemType)
            {
                IssaPluginPlugin.Log.LogWarning("[BoobyTrap] Drop rejected — cannot trap itself.");
                return;
            }

            var itemType = slot.itemType;
            int remainingUses = slot.remainingUses;

            // Remove the source item from inventory.
            ItemHelper.ConsumeItemAtSlot(inventory, sourceSlot);

            // Spawn a DroppedCustomItem marked as booby trapped.
            var prefab = AssetLoader.DroppedCustomItemPrefab;
            if (prefab == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[BoobyTrap] DroppedCustomItemPrefab is null — cannot drop."
                );
                return;
            }

            var dropPos = inventory.transform.position + Vector3.up * 0.5f;

            var rootGo = Object.Instantiate(prefab, dropPos, Quaternion.identity);
            var droppedItem = rootGo.GetComponent<DroppedCustomItem>();
            if (droppedItem == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[BoobyTrap] DroppedCustomItemPrefab has no DroppedCustomItem component."
                );
                Object.Destroy(rootGo);
                return;
            }

            droppedItem.ItemType = itemType;
            droppedItem.RemainingUses = remainingUses;
            droppedItem.IsBoobyTrapped = true;

            // Visual model child (same pattern as ServerDropCustomItemPatch).
            var modelPrefab = DroppedCustomItem.GetModelPrefabForType(itemType);
            if (modelPrefab != null)
            {
                var model = Object.Instantiate(modelPrefab);
                model.transform.SetParent(rootGo.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                var modelRb = model.GetComponent<Rigidbody>();
                if (modelRb != null)
                    Object.Destroy(modelRb);

                foreach (var col in model.GetComponentsInChildren<Collider>())
                    col.enabled = true;

                model.SetActive(true);
            }

            var rb = rootGo.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.linearVelocity = Vector3.up * 3f;
            }

            rootGo.layer = GameManager.LayerSettings.ItemsLayer;
            rootGo.SetActive(true);
            NetworkServer.Spawn(rootGo);

            // Consume the Booby Trap use.
            ItemHelper.ConsumeEquippedItem(inventory);

            IssaPluginPlugin.Log.LogInfo(
                $"[BoobyTrap] Dropped {itemType} (slot {sourceSlot}) as booby trap from player {identity.netId}."
            );
        }

        // ====================================================================
        //  Client message handlers
        // ====================================================================

        internal static void HandleBoobyTrapPlaced(BoobyTrapPlacedMessage msg)
        {
            BoobyTrapOverlay.Instance?.ClientOnTrapPlaced(msg.TargetNetId, msg.TargetType);
        }

        /// Server told us one of our inventory slots is booby-trapped.
        internal static void HandleSlotMarked(BoobyTrapSlotMarkedMessage msg)
        {
            LocalTrappedSlots.Add(msg.SlotIndex);
            IssaPluginPlugin.Log.LogInfo(
                $"[BoobyTrap] Client: slot {msg.SlotIndex} marked as trapped."
            );
        }

        /// Client tells the server it tried to use a booby-trapped slot.
        /// Server validates, consumes the item, and detonates.
        internal static void ServerHandleInventoryTriggered(
            NetworkConnectionToClient conn,
            BoobyTrapInventoryTriggeredMessage msg
        )
        {
            if (!NetworkServer.active)
                return;

            var identity = conn.identity;
            var inventory = identity?.GetComponent<PlayerInventory>();
            if (inventory == null)
                return;

            uint netId = identity.netId;

            if (!TryConsumeInventoryTrap(netId, msg.SlotIndex))
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[BoobyTrap] Inventory trigger rejected — slot {msg.SlotIndex} was not trapped for player {netId}."
                );
                return;
            }

            // Remove the item so it doesn't fire.
            ItemHelper.ConsumeItemAtSlot(inventory, msg.SlotIndex);

            ServerTriggerDetonation(inventory.transform.position, null);

            IssaPluginPlugin.Log.LogInfo(
                $"[BoobyTrap] Inventory trap detonated for player {netId} at slot {msg.SlotIndex}."
            );
        }

        internal static void HandleBoobyTrapExplosion(BoobyTrapExplosionMessage msg)
        {
            VfxManager.PlayPooledVfxLocalOnly(
                VfxType.RocketLauncherRocketExplosion,
                msg.Position,
                Quaternion.identity,
                Vector3.one * ModConfig.BoobyTrap.ExplosionScale.Value
            );

            CameraModuleController.Shake(
                GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings,
                msg.Position
            );

            var localInfo = GameManager.LocalPlayerInfo;
            if (localInfo == null)
                return;

            var movement = localInfo.Movement;
            var rb = localInfo.GetComponentInParent<Rigidbody>();
            if (movement == null || rb == null)
                return;

            Vector3 toPlayer = movement.transform.position - msg.Position;
            float dist = toPlayer.magnitude;
            if (dist < 0.001f)
                return;

            Vector3 blastDir = (toPlayer + Vector3.up * 0.4f).normalized;
            float force = Mathf.Lerp(60f, 0f, dist / 20f);
            if (force <= 0f)
                return;

            Vector3 velocityChange = blastDir * force;
            rb.AddForce(velocityChange, ForceMode.VelocityChange);

            bool _;
            movement.TryKnockOut(
                msg.TrapperInfo,
                KnockoutType.Rocket,
                false,
                movement.transform.InverseTransformPoint(msg.Position),
                dist,
                velocityChange,
                true,
                msg.ItemUseId,
                false,
                true,
                out _
            );
        }

        // ====================================================================
        //  Hole-transition cleanup
        // ====================================================================

        public override void ServerHoleCleanup()
        {
            if (!isServer)
                return;

            TrappedItemBoxes.Clear();
            TrappedGolfCarts.Clear();
            TrappedInventorySlots.Clear();
            IssaPluginPlugin.Log.LogInfo("[BoobyTrap] Server state cleared on hole transition.");
        }

        public override void ClientHoleCleanup()
        {
            LocalTrappedSlots.Clear();
            BoobyTrapOverlay.Instance?.ClientHoleCleanup();
        }
    }
}
