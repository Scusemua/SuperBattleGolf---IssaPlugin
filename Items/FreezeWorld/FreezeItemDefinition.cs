using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

namespace IssaPlugin.Items
{
    public class FreezeItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.FreezeItemType;
        public override string DisplayName => "Freeze World";
        public override string[] ConsoleAliases => new[] { "freezeworld", "freeze" };
        public override Sprite Icon => AssetLoader.FreezeIcon;
        public override GameObject HeldModelPrefab => AssetLoader.FreezeModelPrefab;
        public override int MaxUses => (int)ModConfig.Freeze.Uses.Value;
        public override int Tier => 1;
        public override Key GiveKey => ModConfig.Freeze.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<FreezeNetworkBridge>();
            if (bridge != null)
                NetworkClient.Send(new FreezeActivateMessage());
            else
                IssaPluginPlugin.Log.LogError("[Freeze] No FreezeNetworkBridge on player.");
        }
    }
}
