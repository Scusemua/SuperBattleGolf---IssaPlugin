using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class AA12ItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.AA12ItemType;
        public override string DisplayName => "AA-12";
        public override string[] ConsoleAliases =>
            new[] { "aa12", "aa-12", "auto_shotgun", "autoshotgun" };
        public override Sprite Icon => AssetLoader.AA12Icon;
        public override GameObject HeldModelPrefab => AssetLoader.AA12Prefab;
        public override bool UseRocketIconFallback => false;
        public override int MaxUses => (int)ModConfig.AA12.Uses.Value;
        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 1f,
                GlobalConfig.PoolAhead => 1f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.AA12.GiveKey.Value;
        public override EquipmentType EquipmentType => EquipmentType.ElephantGun;
        public override ItemType AnimatorItemType => ItemType.ElephantGun;
        public override ItemType AnimatorChangedItemType => ItemType.ElephantGun;
        public override ItemType? EffectiveItemProxy => ItemType.ElephantGun;
        public override float? GetAimSpreadDegrees() => ModConfig.AA12.Inaccuracy.Value;
        public override float? FirearmMaxShotDistance => ModConfig.AA12.MaxShotDistance.Value;

        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(AA12Item.FireLoop(inventory));
    }
}
