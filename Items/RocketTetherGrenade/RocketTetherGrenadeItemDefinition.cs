using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Item definition for the Rocket Tether Grenade (ItemType 127).
    ///
    /// Thrown like a Poison Jar; on landing it detonates and attaches every player
    /// within <see cref="ModConfig.RocketTetherGrenade.BlastRadius"/> to an
    /// independent upward rocket — exactly the Rocket Tether effect, applied to
    /// multiple victims simultaneously.
    /// </summary>
    public class RocketTetherGrenadeItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.RocketTetherGrenadeItemType;
        public override string DisplayName => "Rocket Tether Grenade";
        public override string[] ConsoleAliases => ["rocket_tether_grenade", "rtgrenade"];

        public override Sprite Icon => AssetLoader.RocketTetherGrenadeIcon;
        public override GameObject HeldModelPrefab => AssetLoader.RocketTetherGrenadePrefab;

        public override int MaxUses => (int)ModConfig.RocketTetherGrenade.Uses.Value;
        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead  => 1f,
            GlobalConfig.PoolAhead => 1f,
            _                      => DefaultPoolWeight,
        };
        public override Key GiveKey => ModConfig.RocketTetherGrenade.GiveKey.Value;

        // Orbital Laser hold/use pose — same wiring as AK47 (Equipment+Animator) and
        // Cannon/Golf Cart Launcher (InheritAnimatorOverrideController).
        public override EquipmentType EquipmentType => EquipmentType.OrbitalLaser;
        public override ItemType AnimatorItemType => ItemType.OrbitalLaser;
        public override ItemType AnimatorChangedItemType => ItemType.OrbitalLaser;
        public override bool InheritAnimatorOverrideController => true;

        public override void OnEquip(PlayerInventory inventory)
        {
            var preview = inventory.GetComponent<StickyGrenadeTrajectoryPreview>();
            if (preview == null)
            {
                preview = inventory.gameObject.AddComponent<StickyGrenadeTrajectoryPreview>();
                preview.TargetItemType = ItemRegistry.RocketTetherGrenadeItemType;
                preview.ThrowSpeed = () => ModConfig.RocketTetherGrenade.ThrowSpeed.Value;
                preview.LobAngle = () => ModConfig.RocketTetherGrenade.LobAngle.Value;
                preview.RingRadius = () => ModConfig.RocketTetherGrenade.BlastRadius.Value;
            }
        }

        public override void OnUse(PlayerInventory inventory)
        {
            inventory.GetComponent<RocketTetherGrenadeNetworkBridge>()?.ClientThrow();
        }
    }
}
