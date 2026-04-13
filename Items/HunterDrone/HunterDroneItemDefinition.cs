using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class HunterDroneItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.HunterDroneItemType;
        public override string DisplayName => "Hunter Drone";
        public override string[] ConsoleAliases => new[] { "hunterdrone", "hd", "hdrone" };

        // Falls back to DroneSwarmIcon if hunter_drone_icon.png is not in the bundle.
        public override Sprite Icon =>
            AssetLoader.HunterDroneIcon ?? AssetLoader.DroneSwarmIcon;

        // Falls back to DroneControllerPrefab if hunter_drone_handheld.prefab is missing.
        public override GameObject HeldModelPrefab =>
            AssetLoader.HunterDroneHandheldPrefab ?? AssetLoader.DroneControllerPrefab;

        public override int MaxUses => (int)ModConfig.HunterDrone.Uses.Value;

        public override float DefaultPoolWeight => 1f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead      => 0f,
            GlobalConfig.PoolAhead     => 0f,
            GlobalConfig.PoolBehind50  => 0.5f,
            GlobalConfig.PoolBehind200 => 2f,
            _                          => DefaultPoolWeight,
        };

        public override Key GiveKey => ModConfig.HunterDrone.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            inventory.connectionToServer?.Send(new HunterDroneLaunchMessage());
        }
    }
}
