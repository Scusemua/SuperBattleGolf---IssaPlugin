using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Evil Glove — aim-lock any player's golf ball (including your own), then
    /// hold / throw using the shared <see cref="GloveNetworkBridge"/>.
    /// Tier 3 / Rare; does not register strokes on release.
    /// </summary>
    public class EvilGloveItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.EvilGloveItemType;
        public override string DisplayName => "Evil Glove";
        public override string[] ConsoleAliases => new[] { "evilglove", "evil_glove", "badglove" };
        public override Sprite Icon => AssetLoader.EvilGloveIcon;
        public override GameObject HeldModelPrefab => AssetLoader.EvilGloveHandheldPrefab;
        public override int MaxUses => (int)ModConfig.EvilGlove.Uses.Value;

        // Tier 3 / Rare
        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 0f,
                GlobalConfig.PoolAhead => 0f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.EvilGlove.GiveKey.Value;

        public override EquipmentType EquipmentType => EquipmentType.OrbitalLaser;
        public override ItemType AnimatorItemType => ItemType.OrbitalLaser;
        public override ItemType AnimatorChangedItemType => ItemType.OrbitalLaser;
        public override bool InheritAnimatorOverrideController => true;

        // Aim lock-on requires RMB (same gate as other aim items).
        public override bool RequiresAimToUse => true;

        public override void OnEquip(PlayerInventory inventory)
        {
            if (inventory.GetComponent<GloveTrajectoryPreview>() == null)
                inventory.gameObject.AddComponent<GloveTrajectoryPreview>();
        }

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<GloveNetworkBridge>();
            if (bridge != null)
                bridge.ClientRequestEvilPickup();
            else
                IssaPluginPlugin.Log.LogError("[EvilGlove] No GloveNetworkBridge on player.");
        }
    }
}
