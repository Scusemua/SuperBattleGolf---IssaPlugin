using System.Collections.Generic;
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

        // Reused across uses so target selection allocates nothing per throw.
        private readonly List<(PlayerInfo player, float angle, float sqDist)> _targetScratch =
            new List<(PlayerInfo, float, float)>();

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

            // Pick the player being aimed at. Doing this client-side first means a bad
            // aim simply does nothing instead of consuming the item — the server repeats
            // the check authoritatively before it consumes anything.
            var target = HunterDroneTargeting.SelectTarget(
                aimRay.origin,
                aimRay.direction,
                GameManager.LocalPlayerInfo,
                _targetScratch
            );

            if (target == null)
            {
                IssaPluginPlugin.Log.LogInfo(
                    "[HunterDrone] Not aiming at any player — drone not thrown."
                );
                return;
            }

            // Launch toward the target so the drone sets off in the right direction even
            // before its homing kicks in.
            Vector3 aimPoint = target.transform.position;

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
