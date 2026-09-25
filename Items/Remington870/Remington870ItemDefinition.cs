using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class Remington870ItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.Remington870ItemType;
        public override string DisplayName => "Remington 870";
        public override string[] ConsoleAliases =>
            new[] { "870", "remington", "remington870", "pump", "pump_shotgun" };
        public override Sprite Icon => AssetLoader.Remington870Icon;
        public override GameObject HeldModelPrefab => AssetLoader.Remington870Prefab;
        public override bool UseRocketIconFallback => false;
        public override int MaxUses => (int)ModConfig.Remington870.Uses.Value;
        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 1f,
                GlobalConfig.PoolAhead => 1f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.Remington870.GiveKey.Value;
        public override EquipmentType EquipmentType => EquipmentType.ElephantGun;
        public override ItemType AnimatorItemType => ItemType.ElephantGun;
        public override ItemType AnimatorChangedItemType => ItemType.ElephantGun;
        public override ItemType? EffectiveItemProxy => ItemType.ElephantGun;
        public override float? GetAimSpreadDegrees() => ModConfig.Remington870.Inaccuracy.Value;
        public override float? FirearmMaxShotDistance => ModConfig.Remington870.MaxShotDistance.Value;

        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(Remington870Item.ShootRoutine(inventory));
    }
}
