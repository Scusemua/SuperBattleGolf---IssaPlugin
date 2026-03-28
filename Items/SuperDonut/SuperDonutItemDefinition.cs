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
        public override int MaxUses => (int)Configuration.SuperDonutUses.Value;
        public override float SpawnWeight
        {
            get { return Configuration.SuperDonutSpawnWeight.Value; }
            set { Configuration.SuperDonutSpawnWeight.Value = value; }
        }
        public override Key GiveKey => Configuration.SuperDonutGiveKey.Value;

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
