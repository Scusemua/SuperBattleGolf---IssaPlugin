using IssaPlugin.Overlays;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class GrappleItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.GrappleItemType;
        public override string DisplayName => "Gravity Gun";
        public override string[] ConsoleAliases => new[] { "gravity_gun", "gravity-gun" };

        public override Sprite Icon => AssetLoader.ElectricGrappleIcon;
        public override GameObject HeldModelPrefab => AssetLoader.ElectricWhipHandheldPrefab;

        // Falls back to the rocket launcher icon when ElectricGrappleIcon is null.
        public override bool UseRocketIconFallback => true;

        // Two-handed rifle stance — same as the Sniper Rifle / M200 Intervention.
        public override EquipmentType EquipmentType => EquipmentType.ElephantGun;
        public override ItemType AnimatorItemType => ItemType.ElephantGun;
        public override ItemType AnimatorChangedItemType => ItemType.ElephantGun;

        public override int MaxUses => (int)Configuration.GrappleUses.Value;

        public override float SpawnWeight
        {
            get => Configuration.GrappleSpawnWeight.Value;
            set => Configuration.GrappleSpawnWeight.Value = value;
        }

        public override Key GiveKey => Configuration.GrappleGiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            inventory.GetComponent<GrappleNetworkBridge>()?.ClientUse();
        }
    }
}
