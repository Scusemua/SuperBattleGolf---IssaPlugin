using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class SuperShapeShifterItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.SuperShapeShifterItemType;
        public override string DisplayName => "Super Shape Shifter";
        public override string[] ConsoleAliases =>
            new[]
            {
                "supercubeball",
                "super_cube_ball",
                "supershapeshifter",
                "super_shape_shifter",
            };
        public override Sprite Icon =>
            AssetLoader.SuperShapeShifterIcon ?? AssetLoader.ShapeShifterIcon;

        // Shares the same handheld prefab as ShapeShifter.
        public override GameObject HeldModelPrefab => AssetLoader.ShapeShifterHandheldPrefab;

        public override int MaxUses => (int)ModConfig.SuperShapeShifter.Uses.Value;
        public override float DefaultPoolWeight => 3f;
        public override Key GiveKey => ModConfig.SuperShapeShifter.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            // No target selection — fires instantly at all other players.
            NetworkClient.Send(
                new SuperShapeShifterRequestMessage
                {
                    EquippedSlotIndex = inventory.EquippedItemIndex,
                }
            );
        }
    }
}
