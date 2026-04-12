using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class WindStormItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.WindStormItemType;
        public override string DisplayName => "Wind Storm";
        public override string[] ConsoleAliases => new[] { "windstorm", "storm", "wind" };

        public override Sprite Icon => AssetLoader.WindStormIcon; // placeholder: wind_storm_icon.png
        public override bool UseRocketIconFallback => false; 
        public override GameObject HeldModelPrefab => AssetLoader.WindStormModelPrefab; // placeholder: wind_storm_model.prefab

        public override int MaxUses => (int)ModConfig.WindStorm.Uses.Value;
        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 1f,
                GlobalConfig.PoolAhead => 2f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.WindStorm.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            NetworkClient.Send(new WindStormActivateMessage());
        }
    }
}
