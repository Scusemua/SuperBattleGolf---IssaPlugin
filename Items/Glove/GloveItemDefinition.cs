using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Glove — pick up and carry your own golf ball, then drop or throw it.
    /// Held/animated as the Orbital Laser (see EquipmentType / Animator overrides).
    /// </summary>
    public class GloveItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.GloveItemType;
        public override string DisplayName => "Glove";
        public override string[] ConsoleAliases => new[] { "glove", "ballglove" };
        public override Sprite Icon => AssetLoader.GloveIcon;
        public override GameObject HeldModelPrefab => AssetLoader.GloveHandheldPrefab;
        public override int MaxUses => (int)ModConfig.Glove.Uses.Value;
        public override float DefaultPoolWeight => 10f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 5f,
                GlobalConfig.PoolAhead => 5f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.Glove.GiveKey.Value;

        // Orbital Laser hold/use pose — same wiring as AK47 (Equipment+Animator) and
        // Cannon/Golf Cart Launcher (InheritAnimatorOverrideController).
        public override EquipmentType EquipmentType => EquipmentType.OrbitalLaser;
        public override ItemType AnimatorItemType => ItemType.OrbitalLaser;
        public override ItemType AnimatorChangedItemType => ItemType.OrbitalLaser;
        public override bool InheritAnimatorOverrideController => true;

        // Pickup does not require aim. Charge/throw after pickup is handled by the bridge.
        public override bool RequiresAimToUse => false;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<GloveNetworkBridge>();
            if (bridge != null)
                bridge.ClientRequestPickup();
            else
                IssaPluginPlugin.Log.LogError("[Glove] No GloveNetworkBridge on player.");
        }
    }
}
