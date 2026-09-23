using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class MoonItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.MoonItemType;
        public override string DisplayName => "Majora's Moon";
        public override string[] ConsoleAliases => new[] { "moon", "majoras_moon" };

        public override Sprite Icon => AssetLoader.MoonIcon;
        public override GameObject HeldModelPrefab => AssetLoader.MoonHandheldPrefab;

        public override bool UseRocketIconFallback => true;

        // Orbital Laser hold/use pose — same wiring as AK47 (Equipment+Animator) and
        // Cannon/Golf Cart Launcher (InheritAnimatorOverrideController). Without the
        // GetEffectivelyEquippedItem remap + Inherit, the idle stance stays empty-handed.
        public override EquipmentType EquipmentType => EquipmentType.OrbitalLaser;
        public override ItemType AnimatorItemType => ItemType.OrbitalLaser;
        public override ItemType AnimatorChangedItemType => ItemType.OrbitalLaser;
        public override bool InheritAnimatorOverrideController => true;

        public override int MaxUses => (int)ModConfig.Moon.Uses.Value;

        public override float DefaultPoolWeight => 4f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 2f,
                GlobalConfig.PoolAhead => 2f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.Moon.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<MoonNetworkBridge>();
            if (bridge == null)
                return;

            bridge.ClientUse();
        }
    }
}
