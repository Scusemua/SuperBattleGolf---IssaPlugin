using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

namespace IssaPlugin.Items
{
    public class LowGravityItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => LowGravityItem.LowGravityItemType;
        public override string DisplayName => "Low Gravity";
        public override string[] ConsoleAliases => new[] { "lowgravity", "gravity" };
        public override Sprite Icon => AssetLoader.LowGravityIcon;
        public override GameObject HeldModelPrefab => AssetLoader.LowGravityModelPrefab;
        public override int MaxUses => (int)Configuration.LowGravityUses.Value;
        public override float SpawnWeight => Configuration.LowGravitySpawnWeight.Value;
        public override Key GiveKey => Configuration.LowGravityGiveKey.Value;

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
