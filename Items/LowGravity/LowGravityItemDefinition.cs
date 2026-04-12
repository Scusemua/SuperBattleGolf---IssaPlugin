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
        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead  => 1f,
            GlobalConfig.PoolAhead => 1f,
            _                      => DefaultPoolWeight,
        };
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
