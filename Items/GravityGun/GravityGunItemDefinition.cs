using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class GravityGunItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.GravityGunItemType;
        public override string DisplayName => "Gravity Gun";
        public override string[] ConsoleAliases => new[] { "gravity_gun", "gravity-gun" };

        public override Sprite Icon => AssetLoader.ElectricGravityGunIcon;
        public override GameObject HeldModelPrefab => AssetLoader.ElectricWhipHandheldPrefab;

        // Falls back to the rocket launcher icon when ElectricGravityGunIcon is null.
        public override bool UseRocketIconFallback => true;

        // Two-handed rifle stance — same as the Sniper Rifle / M200 Intervention.
        public override EquipmentType EquipmentType => EquipmentType.ElephantGun;
        public override ItemType AnimatorItemType => ItemType.ElephantGun;
        public override ItemType AnimatorChangedItemType => ItemType.ElephantGun;

        public override int MaxUses => (int)ModConfig.GravityGun.Uses.Value;

        public override int Tier => 3;

        public override Key GiveKey => ModConfig.GravityGun.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<GravityGunNetworkBridge>();
            if (bridge == null)
                return;

            if (bridge.LocalSessionActive)
                NetworkClient.Send(new GravityGunReleaseMessage());
            else
                bridge.ClientUse();
        }
    }
}
