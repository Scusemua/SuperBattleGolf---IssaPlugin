using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class BearItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.BearItemType;
        public override string DisplayName => "Bear";
        public override string[] ConsoleAliases => new[] { "bear", "bears" };
        public override Sprite Icon => AssetLoader.BearIcon;
        public override GameObject HeldModelPrefab => AssetLoader.TeddyBearPrefab;
        public override int MaxUses => (int)ModConfig.Bear.Uses.Value;
        public override float DefaultPoolWeight => 3f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead      => 0f,
            GlobalConfig.PoolAhead     => 0f,
            GlobalConfig.PoolBehind50  => 1f,
            GlobalConfig.PoolBehind125 => 2f,
            GlobalConfig.PoolMobility  => 2f,
            _                          => DefaultPoolWeight,
        };
        public override Key GiveKey => ModConfig.Bear.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<BearNetworkBridge>();
            if (bridge != null)
                NetworkClient.Send(new BearActivateMessage());
            else
                IssaPluginPlugin.Log.LogError("[Bear] No BearNetworkBridge on player.");
        }
    }
}
