using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public abstract class CustomItemDefinition
    {
        // Identity
        public abstract ItemType ItemType { get; }
        public abstract string DisplayName { get; }
        public abstract string[] ConsoleAliases { get; }

        // comparison is OrdinalIgnoreCase

        // Assets (returned from AssetLoader — loaded lazily, always valid by use time)
        public abstract Sprite Icon { get; }
        public abstract GameObject HeldModelPrefab { get; }

        // Confirmed: held and dropped prefabs are identical for all 11 current items,
        // so a single property covers both contexts. If a future item needs a different
        // dropped representation, add: public virtual GameObject DroppedModelPrefab => HeldModelPrefab;
        public virtual bool UseRocketIconFallback => true; // false = use pistol fallback (Bat, Sniper)

        // Configuration
        // MaxUses reads Configuration.XxxUses.Value at call time (lazy — no static init risk).
        // The cast to int is intentional: BepInEx config values for use-counts are ConfigEntry<float>.
        public abstract int MaxUses { get; }
        public abstract int Tier { get; }
        public abstract Key GiveKey { get; }

        // Set only on non-host clients by SpawnWeightsSyncer.HandleSpawnWeights.
        // Cleared by SpawnWeightsSyncer.BroadcastWeightsIfChanged before each host resolution.
        // Host always resolves fresh from config (never touches this field).
        private float? _serverWeight = null;

        internal void ResetServerWeight() => _serverWeight = null;

        public float SpawnWeight
        {
            get
            {
                if (_serverWeight.HasValue)
                    return _serverWeight.Value;
                if (Configuration.GetItemSpawnWeightOverrideEnabled(ItemType))
                    return Configuration.GetItemSpawnWeightOverrideValue(ItemType);
                return Configuration.GetTierSpawnWeight(Configuration.GetItemTier(ItemType));
                // return Configuration.GetTierSpawnWeight(Tier);
            }
            set { _serverWeight = value; }
        }

        // When false the item is excluded from the spawn pool entirely (weight is ignored).
        // Delegates to Configuration so the value persists across sessions and is vote-writeable.
        public virtual bool Enabled
        {
            get => Configuration.GetItemEnabled(ItemType);
            set => Configuration.SetItemEnabled(ItemType, value);
        }

        // Game integration
        public virtual EquipmentType EquipmentType => EquipmentType.RocketLauncher;

        // AnimatorItemType for PlayerAnimatorSetEquippedItemPatch (SetEquippedItem).
        // This controls the animator INTEGER parameter / upper-body pose.
        //   OrbitalLaser  → rocket-launcher hold (most items)
        //   ElephantGun   → two-handed rifle hold (Sniper)
        //   RocketLauncher → javelin hold
        //   None          → bat (correct hand pose; leaves the parameter at whatever
        //                   the base game set — bat uses the golf-swing mechanic)
        public virtual ItemType AnimatorItemType => ItemType.OrbitalLaser;

        // AnimatorChangedItemType for PlayerAnimatorOnEquippedChangedPatch (OnNetworkedEquippedItemChanged).
        // Controls the runtime AnimatorController lookup on ALL clients.
        // Almost always the same as AnimatorItemType, EXCEPT the bat, which uses ItemType.None
        // here (gives the correct remote-client hand pose) vs. falling through with no assignment
        // in SetEquippedItem.
        public virtual ItemType AnimatorChangedItemType => ItemType.OrbitalLaser;

        // Use behavior (called from TryUseItemPatch)
        public virtual bool ShouldEatInputOnUse => true;
        public virtual bool UseResult => true;

        // Called from TryUseItemPatch.Prefix with the local PlayerInventory.
        // Items that need a coroutine call inventory.StartCoroutine(...) here directly —
        // PlayerInventory is a MonoBehaviour so StartCoroutine is available.
        // Example:
        //   public override void OnUse(PlayerInventory inventory)
        //       => inventory.StartCoroutine(StealthBomberItem.BomberRunRoutine(inventory));
        public abstract void OnUse(PlayerInventory inventory);

        // Per-frame equip hook — called from UpdateEquipmentSwitchersPatch for the local player only.
        // Must be idempotent (called every frame). Guard AddComponent with a null check:
        //   if (inventory.GetComponent<T>() == null) inventory.gameObject.AddComponent<T>();
        // Used for: JavelinLockOnIndicator, StickyGrenadeTrajectoryPreview.
        public virtual void OnEquip(PlayerInventory inventory) { }
    }
}
