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
        public override Sprite Icon => AssetLoader.HunterDroneIcon ?? AssetLoader.DroneSwarmIcon;

        // Falls back to DroneControllerPrefab if hunter_drone_handheld.prefab is missing.
        public override GameObject HeldModelPrefab =>
            AssetLoader.HunterDronePrefab ?? AssetLoader.DroneControllerPrefab;

        public override int MaxUses => (int)ModConfig.HunterDrone.Uses.Value;

        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 0f,
                GlobalConfig.PoolAhead => 0f,
                GlobalConfig.PoolBehind50 => 1f,
                GlobalConfig.PoolBehind200 => 2f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.HunterDrone.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            Vector3 aimPoint;
            if (Mouse.current != null)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                Ray aimRay = Camera.main.ScreenPointToRay(mousePos);

                if (Physics.Raycast(aimRay, out RaycastHit hit, 500f))
                    aimPoint = hit.point;
                else
                    aimPoint = aimRay.GetPoint(200f);
            }
            else
            {
                aimPoint = inventory.transform.position + inventory.transform.forward * 200f;
            }

            inventory.connectionToServer?.Send(
                new HunterDroneLaunchMessage { AimPoint = aimPoint }
            );
        }
    }
}
