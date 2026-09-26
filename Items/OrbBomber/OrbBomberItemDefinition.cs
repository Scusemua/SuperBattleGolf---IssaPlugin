using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class OrbBomberItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.OrbBomberItemType;
        public override string DisplayName => "Orb Bomber";
        public override string[] ConsoleAliases => new[] { "orbbomber", "orb_bomber" };
        public override Sprite Icon => AssetLoader.OrbBomberIcon;
        public override GameObject HeldModelPrefab => AssetLoader.OrbBomberHandheldPrefab;
        public override int MaxUses => (int)ModConfig.OrbBomber.Uses.Value;
        public override float DefaultPoolWeight => 5f;
        public override Key GiveKey => ModConfig.OrbBomber.GiveKey.Value;

        // Same hold pose as Freeze World.
        public override EquipmentType EquipmentType => EquipmentType.OrbitalLaser;
        public override ItemType AnimatorItemType => ItemType.OrbitalLaser;
        public override ItemType AnimatorChangedItemType => ItemType.OrbitalLaser;
        public override bool InheritAnimatorOverrideController => true;

        public override void OnUse(PlayerInventory inventory)
        {
            var overlay = OrbBomberOverlay.Instance;
            if (overlay == null)
            {
                IssaPluginPlugin.Log.LogError("[OrbBomber] No OrbBomberOverlay instance found.");
                return;
            }

            overlay.OpenChooser(inventory.EquippedItemIndex);
        }
    }
}
