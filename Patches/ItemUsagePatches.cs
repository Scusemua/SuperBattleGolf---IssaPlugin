using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Patches
{
    [HarmonyPatch]
    static class TryUseItemPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerInventory), "TryUseItem");

        static bool Prefix(
            PlayerInventory __instance,
            bool isAirhornReaction,
            ref bool shouldEatInput,
            ref bool __result
        )
        {
            var equipped = __instance.GetEffectivelyEquippedItem(true);

            if (equipped == BatItem.BatItemType)
            {
                shouldEatInput = false;
                __result = false;
                return false;
            }

            if (equipped == StealthBomberItem.BomberItemType)
            {
                shouldEatInput = true;
                __result = true;
                __instance.StartCoroutine(StealthBomberItem.BomberRunRoutine(__instance));
                return false;
            }

            if (equipped == PredatorMissileItem.MissileItemType)
            {
                shouldEatInput = true;
                __result = true;
                var bridge = __instance.GetComponent<MissileNetworkBridge>();
                if (bridge != null)
                    NetworkClient.Send(new MissileRequestMessage());
                else
                    IssaPluginPlugin.Log.LogError("[Missile] No MissileNetworkBridge on player.");
                return false;
            }

            if (equipped == AC130Item.AC130ItemType)
            {
                shouldEatInput = true;
                __result = true;
                var bridge = __instance.GetComponent<AC130NetworkBridge>();
                if (bridge != null)
                    NetworkClient.Send(new AC130StartMessage());
                else
                    IssaPluginPlugin.Log.LogError("[AC130] No AC130NetworkBridge on player.");
                return false;
            }

            if (equipped == FreezeItem.FreezeItemType)
            {
                shouldEatInput = true;
                __result = true;
                var bridge = __instance.GetComponent<FreezeNetworkBridge>();
                if (bridge != null)
                    NetworkClient.Send(new FreezeActivateMessage());
                else
                    IssaPluginPlugin.Log.LogError("[Freeze] No FreezeNetworkBridge on player.");
                return false;
            }

            if (equipped == LowGravityItem.LowGravityItemType)
            {
                shouldEatInput = true;
                __result = true;
                var bridge = __instance.GetComponent<LowGravityNetworkBridge>();
                if (bridge != null)
                    NetworkClient.Send(new LowGravityActivateMessage());
                else
                    IssaPluginPlugin.Log.LogError(
                        "[LowGravity] No LowGravityNetworkBridge on player."
                    );
                return false;
            }

            if (equipped == SniperRifleItem.SniperRifleItemType)
            {
                shouldEatInput = true;
                __result = true;
                __instance.StartCoroutine(SniperRifleItem.ShootRoutine(__instance));
                return false;
            }

            if (equipped == UFOItem.UFOItemType)
            {
                shouldEatInput = true;
                __result = true;
                var bridge = __instance.GetComponent<UFONetworkBridge>();
                if (bridge != null)
                    NetworkClient.Send(new UFOStartMessage());
                else
                    IssaPluginPlugin.Log.LogError("[UFO] No UFONetworkBridge on player.");
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch]
    static class UpdateEquipmentSwitchersPatch
    {
        private static readonly Dictionary<PlayerInventory, CustomEquipState> _states = new();

        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PlayerInventory), "UpdateEquipmentSwitchers");

        static void Postfix(PlayerInventory __instance)
        {
            var equipped = __instance.GetEffectivelyEquippedItem(true);
            var rightSwitcher = __instance.PlayerInfo.RightHandEquipmentSwitcher;

            if (!ItemRegistry.IsCustomItem(equipped))
            {
                ClearCustomModel(__instance);
                ShowDefaultEquipment(rightSwitcher);
                return;
            }

            if (equipped == SniperRifleItem.SniperRifleItemType)
            {
                // ElephantGun pose — sniper holds the rifle two-handed like the elephant gun.
                rightSwitcher.SetEquipment(EquipmentType.ElephantGun);
                __instance.PlayerInfo.LeftHandEquipmentSwitcher.SetEquipment(EquipmentType.None);
            }
            else if (equipped == BatItem.BatItemType)
            {
                // Bat uses the golf-swing mechanic, so GolfClub gives the correct hand pose.
                rightSwitcher.SetEquipment(EquipmentType.GolfClub);
                __instance.PlayerInfo.LeftHandEquipmentSwitcher.SetEquipment(EquipmentType.None);
            }
            else if (
                equipped == StealthBomberItem.BomberItemType
                || equipped == PredatorMissileItem.MissileItemType
                || equipped == AC130Item.AC130ItemType
                || equipped == FreezeItem.FreezeItemType
                || equipped == LowGravityItem.LowGravityItemType
                || equipped == UFOItem.UFOItemType
            )
            {
                rightSwitcher.SetEquipment(EquipmentType.RocketLauncher);
                __instance.PlayerInfo.LeftHandEquipmentSwitcher.SetEquipment(EquipmentType.None);
            }

            // SetEquipment above fires OnEquipmentTypeChanged synchronously, which calls
            // EnsureCustomModel via OnEquipmentTypeChangedPatch. Call it here too as a
            // fallback — e.g. when two custom items share the same EquipmentType and the
            // SyncVar value doesn't change so the hook doesn't fire again.
            EnsureCustomModel(rightSwitcher, __instance, equipped);
        }

        private static void HideDefaultEquipment(EquipmentSwitcher switcher)
        {
            if (switcher.CurrentEquipment == null)
                return;

            foreach (
                var r in switcher.CurrentEquipment.gameObject.GetComponentsInChildren<Renderer>()
            )
                r.enabled = false;
        }

        internal static void ClearCustomModel(PlayerInventory inventory)
        {
            if (!_states.TryGetValue(inventory, out var state))
                return;

            if (state.Model != null)
                Object.Destroy(state.Model);

            _states.Remove(inventory);
        }

        /// Spawns or refreshes the custom visual model for <paramref name="equipped"/>.
        /// Safe to call multiple times — the _states guard prevents double-spawning.
        /// Called from both this Postfix (local player) and OnEquipmentTypeChangedPatch
        /// (all players, including remote clients where UpdateEquipmentSwitchers never runs).
        internal static void EnsureCustomModel(
            EquipmentSwitcher rightSwitcher,
            PlayerInventory inventory,
            ItemType equipped
        )
        {
            var prefab = GetPrefabForItem(equipped);
            if (prefab == null)
                return;

            if (
                !_states.TryGetValue(inventory, out var state)
                || state.ItemType != equipped
                || state.Model == null
            )
            {
                ClearCustomModel(inventory);

                var model = Object.Instantiate(prefab);
                model.transform.SetParent(rightSwitcher.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                model.SetActive(true);

                SetLayerRecursive(model, rightSwitcher.gameObject.layer);

                // Disable all colliders on the held model — they have no gameplay purpose
                // when held and can push the player through terrain/walls.
                foreach (var col in model.GetComponentsInChildren<Collider>())
                    col.enabled = false;

                _states[inventory] = new CustomEquipState { Model = model, ItemType = equipped };

                IssaPluginPlugin.Log.LogInfo(
                    $"[Equipment] Custom model spawned for item {(int)equipped}."
                );
            }

            HideDefaultEquipment(rightSwitcher);
        }

        private static GameObject GetPrefabForItem(ItemType type)
        {
            if (type == BatItem.BatItemType)
                return AssetLoader.BatModelPrefab;
            if (type == StealthBomberItem.BomberItemType)
                return AssetLoader.BomberTabletPrefab;
            if (type == PredatorMissileItem.MissileItemType)
                return AssetLoader.MissileTabletPrefab;
            if (type == AC130Item.AC130ItemType)
                return AssetLoader.Ac130TabletPrefab;
            if (type == FreezeItem.FreezeItemType)
                return AssetLoader.FreezeModelPrefab;
            if (type == LowGravityItem.LowGravityItemType)
                return AssetLoader.LowGravityModelPrefab;
            if (type == SniperRifleItem.SniperRifleItemType)
                return AssetLoader.SniperRiflePrefab;
            if (type == UFOItem.UFOItemType)
                return AssetLoader.UFOHandheldPrefab;
            return null;
        }

        private static void ShowDefaultEquipment(EquipmentSwitcher switcher)
        {
            if (switcher.CurrentEquipment == null)
                return;

            foreach (
                var r in switcher.CurrentEquipment.gameObject.GetComponentsInChildren<Renderer>()
            )
                r.enabled = true;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        public static bool HasCustomModel(PlayerInventory inventory) =>
            _states.TryGetValue(inventory, out var state) && state.Model != null;

        private struct CustomEquipState
        {
            public GameObject Model;
            public ItemType ItemType;
        }
    }

    /// Handles custom model spawning for ALL players (local and remote).
    ///
    /// UpdateEquipmentSwitchers — and therefore UpdateEquipmentSwitchersPatch — is only
    /// ever called from local-player methods (SelectItem, DeselectItem, OnStartLocalPlayer,
    /// etc.).  Remote players' equipment is driven exclusively by the NetworkequipmentType
    /// SyncVar hook, so this is the only place that reliably fires for remote clients.
    ///
    /// For the local player this fires synchronously inside the SetEquipment call made by
    /// UpdateEquipmentSwitchersPatch, so EnsureCustomModel runs first here; the subsequent
    /// EnsureCustomModel call in the Postfix is then a no-op (model already in _states).
    [HarmonyPatch]
    static class OnEquipmentTypeChangedPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(EquipmentSwitcher), "OnEquipmentTypeChanged");

        static void Postfix(EquipmentSwitcher __instance)
        {
            // Only handle the right-hand switcher — left hand never holds custom items.
            var playerInfo = __instance.GetComponentInParent<PlayerInfo>();
            if (playerInfo == null || __instance != playerInfo.RightHandEquipmentSwitcher)
                return;

            var inventory = playerInfo.Inventory;
            if (inventory == null)
                return;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (!ItemRegistry.IsCustomItem(equipped))
            {
                // Switched to a standard item — clear any stale custom model.
                UpdateEquipmentSwitchersPatch.ClearCustomModel(inventory);
                return;
            }

            UpdateEquipmentSwitchersPatch.EnsureCustomModel(__instance, inventory, equipped);
        }
    }

    /// Blocks the entire golf swing aim/charge/fire pipeline when a non-bat custom
    /// item is equipped.
    ///
    /// Root cause: GetEffectivelyEquippedItemPatch returns ItemType.None for custom
    /// items when ignoreEquipmentHiding=false (so the game's visual systems ignore
    /// them). CanAimSwing() calls GetEffectivelyEquippedItem(false) and sees None,
    /// believing no item is held — so it allows the swing-aim camera and power bar.
    ///
    /// Patching CanAimSwing() is the earliest intercept point. Returning false here
    /// keeps IsAimingSwing false, which prevents TryStartChargingSwing from running
    /// at all, which in turn keeps IsChargingSwing false so no swing fires.
    ///
    /// The bat is excluded because it intentionally uses the swing mechanic.
    [HarmonyPatch]
    static class CanAimSwingPatch
    {
        static MethodBase TargetMethod() => AccessTools.Method(typeof(PlayerGolfer), "CanAimSwing");

        static bool Prefix(PlayerGolfer __instance, ref bool __result)
        {
            var inventory = __instance.GetComponent<PlayerInventory>();
            if (inventory == null)
                return true;

            var equipped = inventory.GetEffectivelyEquippedItem(true);
            if (ItemRegistry.IsCustomItem(equipped) && equipped != BatItem.BatItemType)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    /// Intercepts the server-side drop handler for custom items.
    ///
    /// The base game's UserCode_CmdDropItemAt removes the item from inventory and
    /// then calls CourseManager.ServerSpawnItem, which fails for custom item types
    /// (no entry in GameManager.AllItems with a valid Prefab).  We handle custom
    /// items entirely: remove the slot ourselves, then spawn a DroppedCustomItem.
    [HarmonyPatch]
    static class ServerDropCustomItemPatch
    {
        private static readonly FieldInfo SlotsField = AccessTools.Field(
            typeof(PlayerInventory),
            "slots"
        );

        private static readonly MethodInfo RemoveItemAtMethod = AccessTools.Method(
            typeof(PlayerInventory),
            "RemoveItemAt"
        );

        static MethodBase TargetMethod() =>
            AccessTools.Method(
                typeof(PlayerInventory),
                "UserCode_CmdDropItemAt__Int32__Vector3__Vector3__ItemUseId"
            );

        static bool Prefix(
            PlayerInventory __instance,
            int index,
            Vector3 playerVelocity,
            Vector3 playerLocalAngularVelocity,
            ItemUseId itemUseId
        )
        {
            if (!NetworkServer.active)
                return true;

            var slots = (IList<InventorySlot>)SlotsField.GetValue(__instance);
            if (index < 0 || index >= slots.Count)
                return true;

            var slot = slots[index];
            if (!ItemRegistry.IsCustomItem(slot.itemType))
                return true;

            // Remove from inventory — mirrors the base game's first step.
            RemoveItemAtMethod.Invoke(__instance, new object[] { index, false });

            if (slot.remainingUses <= 0 || AssetLoader.DroppedCustomItemPrefab == null)
                return false;

            // Drop position — same math as base game's UserCode_CmdDropItemAt.
            var dropPos =
                __instance.transform.position
                + Vector3.up * GameManager.PlayerInventorySettings.DropItemVerticalOffset
                + __instance.transform.right * GameManager.GolfSettings.SwingHitBoxLocalCenter.x;

            var velocity = playerVelocity * 0.25f;
            var angularVelocity =
                velocity.sqrMagnitude > 0.001f
                    ? Vector3.Cross(Vector3.up, velocity.normalized) * 3f
                    : Vector3.zero;

            var go = Object.Instantiate(
                AssetLoader.DroppedCustomItemPrefab,
                dropPos,
                __instance.transform.rotation
            );

            go.layer = GameManager.LayerSettings.ItemsLayer;

            var dropped = go.GetComponent<DroppedCustomItem>();
            dropped.ItemType = slot.itemType;
            dropped.RemainingUses = slot.remainingUses;

            // Spawn the visual model on the server so its colliders become part of
            // the parent's compound Rigidbody — this gives the item real terrain
            // collision. The model's own Rigidbody (if any) is destroyed so its
            // colliders fold into the parent's compound shape instead of simulating
            // independently. Clients add a visual-only copy in OnStartClient.
            var modelPrefab = DroppedCustomItem.GetModelPrefabForType(slot.itemType);
            if (modelPrefab != null)
            {
                var model = Object.Instantiate(modelPrefab);
                model.transform.SetParent(go.transform, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                var modelRb = model.GetComponent<Rigidbody>();
                if (modelRb != null)
                    Object.Destroy(modelRb);

                model.SetActive(true);
            }

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = velocity;
                rb.angularVelocity = angularVelocity;
                rb.isKinematic = false;
                rb.useGravity = true;
            }

            go.SetActive(true);
            NetworkServer.Spawn(go);

            return false; // skip base game (would log an error and return null)
        }
    }

    [HarmonyPatch(typeof(Rocket), "Start")]
    static class RocketStartPatch
    {
        static void Postfix(Rocket __instance)
        {
            if (!PredatorMissileItem.ActiveMissileRockets.Contains(__instance))
                return;

            var entity = __instance.GetComponent<Entity>();
            if (entity != null && entity.HasRigidbody)
            {
                entity.Rigidbody.linearVelocity =
                    Vector3.down * Configuration.MissileFallSpeed.Value;
            }
        }
    }

    /// Redirects the animator integer parameter to OrbitalLaser when a non-bat
    /// custom item is equipped. The animator state machine uses this integer to
    /// transition to the correct upper-body pose (rocket-launcher-style hold).
    [HarmonyPatch(typeof(PlayerAnimatorIo), "SetEquippedItem")]
    static class PlayerAnimatorSetEquippedItemPatch
    {
        static void Prefix(ref ItemType equippedItem)
        {
            if (!ItemRegistry.IsCustomItem(equippedItem))
            {
                return;
            }

            if (equippedItem == SniperRifleItem.SniperRifleItemType)
            {
                equippedItem = ItemType.ElephantGun;
            }
            else if (equippedItem != BatItem.BatItemType)
            {
                equippedItem = ItemType.OrbitalLaser;
            }
        }
    }

    /// Redirects the runtime animator controller lookup to OrbitalLaser when a
    /// non-bat custom item is equipped. Without this, GameManager.AllItems fails
    /// to find ItemData for our custom item types and logs an error.
    [HarmonyPatch(typeof(PlayerAnimatorIo), "OnNetworkedEquippedItemChanged")]
    static class PlayerAnimatorOnEquippedChangedPatch
    {
        static void Prefix(ref ItemType equippedItem)
        {
            if (!ItemRegistry.IsCustomItem(equippedItem))
            {
                return;
            }

            if (equippedItem == SniperRifleItem.SniperRifleItemType)
            {
                equippedItem = ItemType.ElephantGun;
            }
            else if (equippedItem != BatItem.BatItemType)
            {
                equippedItem = ItemType.OrbitalLaser;
            }
        }
    }
}
