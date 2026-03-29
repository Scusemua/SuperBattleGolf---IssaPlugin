using IssaPlugin.Overlays;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class RocketTetherItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.RocketTetherItemType;
        public override string DisplayName => "Rocket Tether";
        public override string[] ConsoleAliases => new[] { "player_linker", "linker" };

        public override Sprite Icon => AssetLoader.RocketTetherIcon;
        public override GameObject HeldModelPrefab => AssetLoader.RocketTetherPrefab;

        public override bool UseRocketIconFallback => true;

        // Two-handed rifle stance — same as Gravity Gun / Sniper Rifle.
        public override EquipmentType EquipmentType => EquipmentType.ElephantGun;
        public override ItemType AnimatorItemType => ItemType.ElephantGun;
        public override ItemType AnimatorChangedItemType => ItemType.ElephantGun;

        public override int MaxUses => (int)Configuration.RocketTetherUses.Value;

        public override float SpawnWeight
        {
            get => Configuration.RocketTetherSpawnWeight.Value;
            set => Configuration.RocketTetherSpawnWeight.Value = value;
        }

        public override Key GiveKey => Configuration.RocketTetherGiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<RocketTetherNetworkBridge>();
            if (bridge == null)
                return;

            bridge.ClientUse();
        }
    }
}
