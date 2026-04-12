using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class SuperDonutItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.SuperDonutItemType;
        public override string DisplayName => "Super Donut";
        public override string[] ConsoleAliases => new[] { "superdonut", "super_donut" };
        public override Sprite Icon => AssetLoader.SuperDonutIcon ?? AssetLoader.DonutIcon;
        public override GameObject HeldModelPrefab =>
            AssetLoader.SuperDonutHandheldPrefab ?? AssetLoader.DonutHandheldPrefab;
        public override int MaxUses => (int)ModConfig.SuperDonut.Uses.Value;
        public override float DefaultPoolWeight => 1f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead      => 0f,
            GlobalConfig.PoolAhead     => 0f,
            GlobalConfig.PoolBehind50  => 0.5f,
            GlobalConfig.PoolBehind200 => 2f,
            _                          => DefaultPoolWeight,
        };
        public override Key GiveKey => ModConfig.SuperDonut.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<SuperDonutNetworkBridge>();
            if (bridge != null)
                NetworkClient.Send(new SuperDonutStartMessage());
            else
                IssaPluginPlugin.Log.LogError("[SuperDonut] No SuperDonutNetworkBridge on player.");
        }
    }
}
