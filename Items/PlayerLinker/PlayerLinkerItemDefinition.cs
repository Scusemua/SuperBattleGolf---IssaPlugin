using IssaPlugin.Overlays;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class PlayerLinkerItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.PlayerLinkerItemType;
        public override string DisplayName => "Player Linker";
        public override string[] ConsoleAliases => new[] { "player_linker", "linker" };

        public override Sprite Icon => AssetLoader.PlayerLinkerIcon;
        public override GameObject HeldModelPrefab => AssetLoader.PlayerLinkerPrefab;

        public override bool UseRocketIconFallback => true;

        // Two-handed rifle stance — same as Gravity Gun / Sniper Rifle.
        public override EquipmentType EquipmentType => EquipmentType.ElephantGun;
        public override ItemType AnimatorItemType => ItemType.ElephantGun;
        public override ItemType AnimatorChangedItemType => ItemType.ElephantGun;

        public override int MaxUses => (int)Configuration.PlayerLinkerUses.Value;

        public override float SpawnWeight
        {
            get => Configuration.PlayerLinkerSpawnWeight.Value;
            set => Configuration.PlayerLinkerSpawnWeight.Value = value;
        }

        public override Key GiveKey => Configuration.PlayerLinkerGiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<PlayerLinkerNetworkBridge>();
            if (bridge == null)
                return;

            bridge.ClientUse();
        }
    }
}
