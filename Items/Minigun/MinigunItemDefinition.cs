using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class MinigunItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.MinigunItemType;
        public override string DisplayName => "Minigun";
        public override string[] ConsoleAliases => new[] { "minigun", "mini_gun" };
        public override Sprite Icon => AssetLoader.MinigunIcon;
        public override GameObject HeldModelPrefab => AssetLoader.MinigunPrefab;
        public override bool UseRocketIconFallback => false;
        public override int MaxUses => (int)ModConfig.Minigun.Uses.Value;
        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 1f,
                GlobalConfig.PoolAhead => 1f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.Minigun.GiveKey.Value;
        public override EquipmentType EquipmentType => EquipmentType.ElephantGun;
        public override ItemType AnimatorItemType => ItemType.ElephantGun;
        public override ItemType AnimatorChangedItemType => ItemType.ElephantGun;
        public override ItemType? EffectiveItemProxy => ItemType.ElephantGun;
        public override float? GetAimSpreadDegrees() => ModConfig.Minigun.Inaccuracy.Value;
        public override float? FirearmMaxShotDistance => ModConfig.Minigun.MaxShotDistance.Value;

        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(MinigunItem.FireLoop(inventory));
    }
}
