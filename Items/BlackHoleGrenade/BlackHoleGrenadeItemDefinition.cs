using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class BlackHoleGrenadeItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.BlackHoleGrenadeItemType;
        public override string DisplayName => "Black Hole Grenade";
        public override string[] ConsoleAliases =>
            new[] { "black_hole_grenade", "blackhole", "blackholegrenade" };
        public override Sprite Icon => AssetLoader.BlackHoleGrenadeIcon;
        public override GameObject HeldModelPrefab => AssetLoader.BlackHoleGrenadePrefab;
        public override int MaxUses => (int)Configuration.BlackHoleGrenadeUses.Value;
        public override float SpawnWeight
        {
            get { return Configuration.BlackHoleGrenadeSpawnWeight.Value; }
            set { Configuration.BlackHoleGrenadeSpawnWeight.Value = value; }
        }
        public override Key GiveKey => Configuration.BlackHoleGrenadeGiveKey.Value;

        public override void OnEquip(PlayerInventory inventory)
        {
            var preview = inventory.GetComponent<StickyGrenadeTrajectoryPreview>();
            if (preview == null)
            {
                preview = inventory.gameObject.AddComponent<StickyGrenadeTrajectoryPreview>();
                preview.TargetItemType = ItemRegistry.BlackHoleGrenadeItemType;
                preview.ThrowSpeed = () => Configuration.BlackHoleGrenadeThrowSpeed.Value;
                preview.LobAngle = () => Configuration.BlackHoleGrenadeLobAngle.Value;
            }
        }

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<BlackHoleGrenadeNetworkBridge>();
            if (bridge != null)
                bridge.ClientThrow();
            else
                IssaPluginPlugin.Log.LogError(
                    "[BlackHoleGrenade] No BlackHoleGrenadeNetworkBridge on player."
                );
        }
    }
}
