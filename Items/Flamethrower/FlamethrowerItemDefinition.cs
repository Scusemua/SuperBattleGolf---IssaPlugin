using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class FlamethrowerItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.FlamethrowerItemType;
        public override string DisplayName => "Flamethrower";
        public override string[] ConsoleAliases =>
            new[] { "flamethrower", "flame", "flamer", "fire" };

        public override Sprite Icon => AssetLoader.FlamethrowerIcon;
        public override GameObject HeldModelPrefab => AssetLoader.FlamethrowerHandheldPrefab;

        public override bool UseRocketIconFallback => true;

        public override int MaxUses => (int)ModConfig.Flamethrower.Uses.Value;
        public override int Tier => 3;
        public override Key GiveKey => ModConfig.Flamethrower.GiveKey.Value;

        // Treat the flamethrower as a two-handed rifle for animator purposes —
        // same as the AK-47.
        public override EquipmentType EquipmentType => EquipmentType.ElephantGun;
        public override ItemType AnimatorItemType => ItemType.ElephantGun;
        public override ItemType AnimatorChangedItemType => ItemType.ElephantGun;

        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(FlamethrowerItem.FireLoop(inventory));
    }
}
