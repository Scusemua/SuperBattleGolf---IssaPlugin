using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class DroneSwarmItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType      => ItemRegistry.DroneSwarmItemType;
        public override string   DisplayName   => "Drone Swarm";
        public override string[] ConsoleAliases => new[] { "drone", "droneswarm", "drones" };
        public override Sprite   Icon          => AssetLoader.DroneSwarmIcon;
        public override GameObject HeldModelPrefab => AssetLoader.DroneControllerPrefab;
        public override int      MaxUses       => (int)Configuration.DroneSwarmUses.Value;

        public override int Tier => 3;

        public override Key GiveKey => Configuration.DroneSwarmGiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            inventory.connectionToServer?.Send(new DroneSwarmSummonMessage());
        }
    }
}
