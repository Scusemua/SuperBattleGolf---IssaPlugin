using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

namespace IssaPlugin.Items
{
    public class DonutItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.DonutItemType;
        public override string DisplayName => "Donut";
        public override string[] ConsoleAliases => new[] { "donut" };
        public override Sprite Icon => AssetLoader.DonutIcon;
        public override GameObject HeldModelPrefab => AssetLoader.DonutHandheldPrefab;
        public override int MaxUses => (int)Configuration.DonutUses.Value;
        public override float SpawnWeight
        {
            get { return Configuration.DonutSpawnWeight.Value; }
            set { Configuration.DonutSpawnWeight.Value = value; }
        }
        public override Key GiveKey => Configuration.DonutGiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<DonutNetworkBridge>();
            if (bridge != null)
                NetworkClient.Send(new DonutStartMessage());
            else
                IssaPluginPlugin.Log.LogError("[Donut] No DonutNetworkBridge on player.");
        }
    }
}
