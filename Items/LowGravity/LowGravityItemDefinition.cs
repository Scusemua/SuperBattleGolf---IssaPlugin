using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class LowGravityItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.LowGravityItemType;
        public override string DisplayName => "Low Gravity";
        public override string[] ConsoleAliases => new[] { "lowgravity", "gravity" };
        public override Sprite Icon => AssetLoader.LowGravityIcon;
        public override GameObject HeldModelPrefab => AssetLoader.LowGravityModelPrefab;
        public override int MaxUses => (int)ModConfig.LowGravity.Uses.Value;
        public override int Tier => 3;
        public override Key GiveKey => ModConfig.LowGravity.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<LowGravityNetworkBridge>();
            if (bridge != null)
                NetworkClient.Send(new LowGravityActivateMessage());
            else
                IssaPluginPlugin.Log.LogError("[LowGravity] No LowGravityNetworkBridge on player.");
        }
    }
}
