using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    class SpinachItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.SpinachItemType;
        public override string DisplayName => "Spinach";
        public override string[] ConsoleAliases => new[] { "steroids", "juice" };
        public override Sprite Icon => AssetLoader.SpinachIcon;
        public override GameObject HeldModelPrefab => AssetLoader.SpinachPrefab;
        public override int MaxUses => (int)ModConfig.Spinach.Uses.Value;
        public override int Tier => 2;
        public override Key GiveKey => ModConfig.Spinach.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var movement = GameManager.LocalPlayerMovement;
            if (movement == null)
                return;

            float duration = ModConfig.Spinach.Duration.Value;

            movement.InformDrankCoffee();
            movement.InformDrankCoffee();
            movement.InformDrankCoffee();

            SpinachBehaviour.Activate(duration);
            RedBullBehaviour.Activate(duration);
            ItemHelper.ConsumeEquippedItem(inventory);

            // Tell the server to broadcast the trail VFX to all clients.
            inventory.GetComponent<SpinachNetworkBridge>()?.ClientRequestVfx();
        }
    }
}
