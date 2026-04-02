using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class BatItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.BaseballBatItemType;
        public override string DisplayName => "Baseball Bat";
        public override string[] ConsoleAliases => new[] { "baseballbat", "bat" };
        public override Sprite Icon => AssetLoader.BatIcon;
        public override GameObject HeldModelPrefab => AssetLoader.BatModelPrefab;
        public override bool UseRocketIconFallback => false; // uses pistol fallback
        public override int MaxUses => (int)ModConfig.BaseballBat.Uses.Value;
        public override int Tier => 1;
        public override Key GiveKey => ModConfig.BaseballBat.GiveKey.Value;
        public override EquipmentType EquipmentType => EquipmentType.GolfClub;

        // AnimatorItemType: bat falls through all branches in SetEquippedItem with no assignment.
        // We model that as ItemType.None — the patch must NOT remap None to OrbitalLaser.
        // See PlayerAnimatorSetEquippedItemPatch note below.
        public override ItemType AnimatorItemType => ItemType.None;
        public override ItemType AnimatorChangedItemType => ItemType.None; // correct remote hand pose
        public override bool ShouldEatInputOnUse => false;
        public override bool UseResult => false;

        public override void OnUse(PlayerInventory inventory) { } // swing handled by BatPatches
    }
}
