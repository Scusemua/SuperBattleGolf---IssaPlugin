using System.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class HarrierItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.HarrierItemType;
        public override string DisplayName => "Harrier Jet";
        public override string[] ConsoleAliases => new[] { "harrier", "harrierjet", "jet" };
        public override Sprite Icon => AssetLoader.HarrierIcon;
        public override GameObject HeldModelPrefab => AssetLoader.HarrierTabletPrefab;
        public override int MaxUses => (int)ModConfig.Harrier.Uses.Value;

        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead  => 1f,
            GlobalConfig.PoolAhead => 1f,
            _                      => DefaultPoolWeight,
        };

        public override Key GiveKey => ModConfig.Harrier.GiveKey.Value;

        // Orbital Laser hold/use pose — same wiring as AK47 (Equipment+Animator) and
        // Cannon/Golf Cart Launcher (InheritAnimatorOverrideController).
        public override EquipmentType EquipmentType => EquipmentType.OrbitalLaser;
        public override ItemType AnimatorItemType => ItemType.OrbitalLaser;
        public override ItemType AnimatorChangedItemType => ItemType.OrbitalLaser;
        public override bool InheritAnimatorOverrideController => true;

        public override void OnUse(PlayerInventory inventory)
        {
            inventory.connectionToServer?.Send(new HarrierRequestMessage());
        }
    }
}
