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
            AssetLoader.HunterDroneHandheldPrefab ?? AssetLoader.DroneControllerPrefab;

        public override int MaxUses => (int)ModConfig.HunterDrone.Uses.Value;

        public override float DefaultPoolWeight => 10f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 0f,
                GlobalConfig.PoolAhead => 0f,
                GlobalConfig.PoolBehind50 => 2f,
                GlobalConfig.PoolBehind200 => 4f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.HunterDrone.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            // Player has to be right-clicking to use.
            if (Mouse.current == null || !Mouse.current.rightButton.isPressed)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray aimRay = cam.ScreenPointToRay(mousePos);

            // Where the player is pointing in the world. Used as the drone's flight
            // direction when nobody is close enough to the cursor to be hunted; the
            // server picks the target (if any) from the ray itself.
            Vector3 aimPoint = Physics.Raycast(aimRay, out RaycastHit hit, 500f)
                ? hit.point
                : aimRay.GetPoint(200f);

            inventory.connectionToServer?.Send(
                new HunterDroneLaunchMessage
                {
                    AimPoint = aimPoint,
                    AimOrigin = aimRay.origin,
                    AimDirection = aimRay.direction,
                }
            );
        }
    }
}
