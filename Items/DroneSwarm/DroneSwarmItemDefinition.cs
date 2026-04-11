using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class DroneSwarmItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.DroneSwarmItemType;
        public override string DisplayName => "Drone Swarm";
        public override string[] ConsoleAliases => new[] { "drone", "droneswarm", "drones" };
        public override Sprite Icon => AssetLoader.DroneSwarmIcon;
        public override GameObject HeldModelPrefab => AssetLoader.DroneControllerPrefab;
        public override int MaxUses => (int)ModConfig.DroneSwarm.Uses.Value;

        public override float DefaultPoolWeight => 1f;

        public override Key GiveKey => ModConfig.DroneSwarm.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            inventory.connectionToServer?.Send(new DroneSwarmSummonMessage());
        }
    }
}
