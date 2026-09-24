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
        public override int MaxUses => (int)ModConfig.BlackHoleGrenade.Uses.Value;
        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead  => 1f,
            GlobalConfig.PoolAhead => 1f,
            _                      => DefaultPoolWeight,
        };
        public override Key GiveKey => ModConfig.BlackHoleGrenade.GiveKey.Value;

        // Orbital Laser hold/use pose — same wiring as AK47 (Equipment+Animator) and
        // Cannon/Golf Cart Launcher (InheritAnimatorOverrideController).
        public override EquipmentType EquipmentType => EquipmentType.OrbitalLaser;
        public override ItemType AnimatorItemType => ItemType.OrbitalLaser;
        public override ItemType AnimatorChangedItemType => ItemType.OrbitalLaser;
        public override bool InheritAnimatorOverrideController => true;

        public override void OnEquip(PlayerInventory inventory)
        {
            var preview = inventory.GetComponent<LobTrajectoryPreview>();
            if (preview == null)
            {
                preview = inventory.gameObject.AddComponent<LobTrajectoryPreview>();
                preview.TargetItemType = ItemRegistry.BlackHoleGrenadeItemType;
                preview.ThrowSpeed = () => ModConfig.BlackHoleGrenade.ThrowSpeed.Value;
                preview.LobAngle = () => ModConfig.BlackHoleGrenade.LobAngle.Value;
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
