using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class AK47ItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.AK47ItemType;
        public override string DisplayName => "AK47";
        public override string[] ConsoleAliases =>
            new[] { "ak47", "AK47", "assault_rifle", "assaultrifle", "AR", "ar" };
        public override Sprite Icon => AssetLoader.AK47Icon;
        public override GameObject HeldModelPrefab => AssetLoader.AK47Prefab;
        public override bool UseRocketIconFallback => false;
        public override int MaxUses => (int)ModConfig.AK47.Uses.Value;
        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 1f,
                GlobalConfig.PoolAhead => 1f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.AK47.GiveKey.Value;
        public override EquipmentType EquipmentType => EquipmentType.ElephantGun;
        public override ItemType AnimatorItemType => ItemType.ElephantGun;
        public override ItemType AnimatorChangedItemType => ItemType.ElephantGun;

        // Starts the fire loop coroutine on button-down. The coroutine itself loops
        // while the button is held, consuming one use per bullet.
        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(AK47Item.FireLoop(inventory));
    }
}
