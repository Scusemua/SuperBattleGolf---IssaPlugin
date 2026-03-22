using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class SubMachineGunItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.SubMachineGunItemType;
        public override string DisplayName => "Uzi";
        public override string[] ConsoleAliases =>
            new[] { "uzi", "smg", "sub_machine_gun", "submachinegun" };
        public override Sprite Icon => AssetLoader.SubMachineGunIcon;
        public override GameObject HeldModelPrefab => AssetLoader.SubMachineGunPrefab;
        public override bool UseRocketIconFallback => false;
        public override int MaxUses => (int)Configuration.SubMachineGunUses.Value;
        public override float SpawnWeight
        {
            get { return Configuration.SubMachineGunSpawnWeight.Value; }
            set { Configuration.SubMachineGunSpawnWeight.Value = value; }
        }
        public override Key GiveKey => Configuration.SubMachineGunGiveKey.Value;
        public override EquipmentType EquipmentType => EquipmentType.ElephantGun;
        public override ItemType AnimatorItemType => ItemType.ElephantGun;
        public override ItemType AnimatorChangedItemType => ItemType.ElephantGun;

        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(SubMachineGunItem.ShootRoutine(inventory));
    }
}
