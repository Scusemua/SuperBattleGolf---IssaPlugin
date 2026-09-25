using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class NightItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.NightItemType;
        public override string DisplayName => "Night Time";
        public override string[] ConsoleAliases => new[] { "night", "nighttime" };
        public override Sprite Icon => AssetLoader.NightIcon;
        public override bool UseRocketIconFallback => false;
        public override GameObject HeldModelPrefab => AssetLoader.NightModelPrefab;
        public override int MaxUses => (int)ModConfig.Night.Uses.Value;
        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 1f,
                GlobalConfig.PoolAhead => 2f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.Night.GiveKey.Value;

        public override bool Enabled
        {
            get => !ModConfig.Global.ForceNightMode.Value && base.Enabled;
            set => base.Enabled = value;
        }

        public override EquipmentType EquipmentType => EquipmentType.OrbitalLaser;
        public override ItemType AnimatorItemType => ItemType.OrbitalLaser;
        public override ItemType AnimatorChangedItemType => ItemType.OrbitalLaser;
        public override bool InheritAnimatorOverrideController => true;

        public override void OnUse(PlayerInventory inventory)
        {
            if (ModConfig.Global.ForceNightMode.Value)
                return;

            NetworkClient.Send(new NightActivateMessage());
        }
    }
}
