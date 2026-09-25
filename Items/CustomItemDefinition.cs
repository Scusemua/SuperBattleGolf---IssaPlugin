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
        public abstract Key GiveKey { get; }

        // Default spawn weight used as the starting value for all 6 per-pool config entries.
        // Derived from the former tier default weights:
        //   Tier 1 (common) = 15f, Tier 2 = 10f, Tier 3 = 5f, Tier 4 = 3f, Tier 5 (rare) = 1f
        // Also used by SpawnConfigUI.GetTierIndex for UI tier bucketing — should reflect
        // the item's general rarity regardless of any per-pool overrides.
        public abstract float DefaultPoolWeight { get; }

        // Per-pool default weight used when a BepInEx config key is first created.
        // Override to express different spawn rates per pool (e.g. 0f for pools where
        // this item should never appear by default). The '_' wildcard in overrides MUST
        // delegate to DefaultPoolWeight so out-of-range pool indices are handled safely:
        //
        //   public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        //   {
        //       GlobalConfig.PoolLead     => 0f,
        //       GlobalConfig.PoolMobility => 0f,
        //       _                         => DefaultPoolWeight,
        //   };
        public virtual float GetDefaultPoolWeight(int poolIndex) => DefaultPoolWeight;

        // Config key prefix used in GlobalConfig.BindAllItemPoolWeights.
        // Defaults to DisplayName with all non-alphanumeric characters stripped.
        // Override only if the result would be ambiguous or unsuitable as a config key.
        public virtual string ConfigKeyPrefix =>
            System.Text.RegularExpressions.Regex.Replace(DisplayName, @"[^A-Za-z0-9]", "");

        // Set on non-host clients by SpawnWeightsSyncer.HandleSpawnWeights for all 6 pools.
        // Cleared by SpawnWeightsSyncer.BroadcastWeightsIfChanged before each host resolution.
        // Host always resolves fresh from config.
        private readonly float?[] _serverPoolWeights = new float?[6];

        internal void SetServerPoolWeight(int poolIndex, float weight) =>
            _serverPoolWeights[poolIndex] = weight;

        internal void ResetServerWeights() => System.Array.Clear(_serverPoolWeights, 0, 6);

        public float GetPoolWeight(int poolIndex)
        {
            if ((uint)poolIndex < 6u && _serverPoolWeights[poolIndex].HasValue)
                return _serverPoolWeights[poolIndex].Value;
            return ModConfig.GetItemPoolWeight(ItemType, poolIndex);
        }

        // When false the item is excluded from the spawn pool entirely (weight is ignored).
        // Delegates to Configuration so the value persists across sessions and is vote-writeable.
        public virtual bool Enabled
        {
            get => ModConfig.GetItemEnabled(ItemType);
            set => ModConfig.SetItemEnabled(ItemType, value);
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

        /// When true, this item's injected ItemData borrows the AnimatorOverrideController
        /// belonging to <see cref="AnimatorItemType"/>, so the player holds it in that base
        /// item's stance while idle — not just while aiming.
        ///
        /// Custom items otherwise get a null controller (see ItemRegistry.GetOrCreateItemData),
        /// which leaves the idle pose as the default empty-handed stance.
        ///
        /// Defaults to false so items whose AnimatorItemType is only a rough stand-in keep
        /// their existing look; opt in when the item really should mimic that weapon.
        public virtual bool InheritAnimatorOverrideController => false;

        // Use behavior (called from TryUseItemPatch)
        public virtual bool ShouldEatInputOnUse => true;
        public virtual bool UseResult => true;

        /// When true, the item only fires while the player is aimed in (right-click held),
        /// matching base-game aimed weapons such as the rocket launcher.
        ///
        /// The base game enforces this in PlayerInventory.TryUseItem via
        /// <c>ItemData.NonAimUse == ItemNonAimingUse.None</c>, but TryUseItemPatch takes
        /// over before that check is reached, so custom items opt in here instead.
        ///
        /// Defaults to false: most custom items (Nuke, Teleporter, Freeze World, ...) are
        /// instant-use and must stay usable without aiming.
        public virtual bool RequiresAimToUse => false;

        /// <summary>
        /// When set, <c>GetEffectivelyEquippedItem(false)</c> reports this base-game
        /// item so aim pose and body rotation match it. Elephant-gun stance guns set
        /// <see cref="ItemType.ElephantGun"/>. Equipment type alone is not enough:
        /// the flamethrower, gravity gun, and rocket tether also use that equipment.
        /// </summary>
        public virtual ItemType? EffectiveItemProxy => null;

        /// <summary>
        /// Degrees of spread drawn as the aim ring, while right-click is held.
        /// Null hides the ring. The sniper stays null and uses its scope instead.
        /// </summary>
        public virtual float? GetAimSpreadDegrees() => null;

        /// <summary>
        /// Max distance of this item's hitscan shot. The bear gun-hit ray uses it
        /// so a short-range firearm cannot wound a bear past its pellets.
        /// Null leaves the bear patch's own range.
        /// </summary>
        public virtual float? FirearmMaxShotDistance => null;

        // Called from TryUseItemPatch.Prefix with the local PlayerInventory.
        // Items that need a coroutine call inventory.StartCoroutine(...) here directly —
        // PlayerInventory is a MonoBehaviour so StartCoroutine is available.
        // Example:
        //   public override void OnUse(PlayerInventory inventory)
        //       => inventory.StartCoroutine(StealthBomberItem.BomberRunRoutine(inventory));
        public abstract void OnUse(PlayerInventory inventory);

        // Per-frame equip hook — called from LocalPlayerUpdateEquipmentSwitchers for the local player only.
        // Must be idempotent (called every frame). Guard AddComponent with a null check:
        //   if (inventory.GetComponent<T>() == null) inventory.gameObject.AddComponent<T>();
        // Used for: JavelinLockOnIndicator, BallisticTrajectoryPreview adapters
        // (LobTrajectoryPreview, GloveTrajectoryPreview).
        public virtual void OnEquip(PlayerInventory inventory) { }
    }
}
