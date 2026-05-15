using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class SuperCubeBallItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.SuperCubeBallItemType;
        public override string DisplayName => "Super Shape Shifter";
        public override string[] ConsoleAliases => new[] { "supercubeball", "super_cube_ball", "supershapeshifter", "super_shape_shifter" };
        public override Sprite Icon => AssetLoader.SuperCubeBallIcon ?? AssetLoader.CubeBallIcon;

        // Shares the same handheld prefab as CubeBall.
        public override GameObject HeldModelPrefab => AssetLoader.CubeBallHandheldPrefab;

        public override int MaxUses => (int)ModConfig.SuperCubeBall.Uses.Value;
        public override float DefaultPoolWeight => 3f;
        public override Key GiveKey => ModConfig.SuperCubeBall.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            // No target selection — fires instantly at all other players.
            NetworkClient.Send(
                new SuperCubeBallRequestMessage { EquippedSlotIndex = inventory.EquippedItemIndex }
            );
        }
    }
}
