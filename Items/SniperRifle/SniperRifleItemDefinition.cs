using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class SniperRifleItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.SniperRifleItemType;
        public override string DisplayName => "M200 Intervention";
        public override string[] ConsoleAliases =>
            new[] { "m200", "sniper", "sniper_rifle", "intervention" };
        public override Sprite Icon => AssetLoader.SniperRifleIcon;
        public override GameObject HeldModelPrefab => AssetLoader.SniperRiflePrefab;
        public override bool UseRocketIconFallback => false; // uses pistol fallback
        public override int MaxUses => (int)ModConfig.SniperRifle.Uses.Value;
        public override int Tier => 2;
        public override Key GiveKey => ModConfig.SniperRifle.GiveKey.Value;
        public override EquipmentType EquipmentType => EquipmentType.ElephantGun;
        public override ItemType AnimatorItemType => ItemType.ElephantGun;
        public override ItemType AnimatorChangedItemType => ItemType.ElephantGun;

        // OnUse uses StartCoroutine — PlayerInventory is a MonoBehaviour, so this is valid.
        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(SniperRifleItem.ShootRoutine(inventory));
    }
}
