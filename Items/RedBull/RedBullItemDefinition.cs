using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class RedBullItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.RedBullItemType;
        public override string DisplayName => "Red Bull";
        public override string[] ConsoleAliases => new[] { "redbull", "bull" };

        public override Sprite Icon => AssetLoader.RedBullIcon;
        public override bool UseRocketIconFallback => false; // pistol fallback if icon missing
        public override GameObject HeldModelPrefab => AssetLoader.RedBullHandheldPrefab;

        public override int MaxUses => (int)Configuration.RedBullUses.Value;
        public override int Tier => 1;
        public override Key GiveKey => Configuration.RedBullGiveKey.Value;

        // PlayerMovement.AddSpeedBoost(float duration) — private, so we need reflection.
        private static readonly MethodInfo _addSpeedBoostMethod;

        static RedBullItemDefinition()
        {
            var pmType = AccessTools.TypeByName("PlayerMovement");
            if (pmType != null)
                _addSpeedBoostMethod = AccessTools.Method(pmType, "AddSpeedBoost");
        }

        public override void OnUse(PlayerInventory inventory)
        {
            var movement = GameManager.LocalPlayerMovement;
            if (movement == null)
                return;

            float duration = Configuration.RedBullDuration.Value;

            // Call the private AddSpeedBoost directly with our custom duration
            // instead of InformDrankCoffee (which hard-codes CoffeeEffectDuration).
            _addSpeedBoostMethod?.Invoke(movement, new object[] { duration });

            RedBullBehaviour.Activate(duration);
            ItemHelper.ConsumeEquippedItem(inventory);

            // Tell the server to broadcast the trail VFX to all clients.
            inventory.GetComponent<RedBullNetworkBridge>()?.ClientRequestTrail();
        }
    }
}
