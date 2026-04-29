using IssaPlugin.Overlays;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class UfoAbductionItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.UfoAbductionItemType;
        public override string DisplayName => "UFO Abduction";
        public override string[] ConsoleAliases => new[] { "ufo", "abduction", "ufo_abduction" };

        public override Sprite Icon => AssetLoader.UfoAbductionIcon;
        public override GameObject HeldModelPrefab => AssetLoader.UfoAbductionHandheldPrefab;

        public override bool UseRocketIconFallback => true;

        public override EquipmentType EquipmentType => EquipmentType.ElephantGun;
        public override ItemType AnimatorItemType => ItemType.ElephantGun;
        public override ItemType AnimatorChangedItemType => ItemType.ElephantGun;

        public override int MaxUses => (int)ModConfig.UfoAbduction.Uses.Value;

        public override float DefaultPoolWeight => 8f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 4f,
                GlobalConfig.PoolAhead => 4f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.UfoAbduction.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<UfoAbductionNetworkBridge>();
            if (bridge == null)
                return;

            bridge.ClientUse();
        }
    }
}
