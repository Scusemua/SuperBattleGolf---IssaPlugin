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

        public override int MaxUses => (int)ModConfig.RocketTether.Uses.Value;

        public override float DefaultPoolWeight => 10f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead  => 5f,
            GlobalConfig.PoolAhead => 5f,
            _                      => DefaultPoolWeight,
        };

        public override Key GiveKey => ModConfig.RocketTether.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<RocketTetherNetworkBridge>();
            if (bridge == null)
                return;

            bridge.ClientUse();
        }
    }
}
