using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class ShapeShifterItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.ShapeShifterItemType;
        public override string DisplayName => "Shape Shifter";
        public override string[] ConsoleAliases => new[] { "cubeball", "cube_ball" };
        public override Sprite Icon => AssetLoader.ShapeShifterIcon;
        public override GameObject HeldModelPrefab => AssetLoader.ShapeShifterHandheldPrefab;
        public override int MaxUses => (int)ModConfig.ShapeShifter.Uses.Value;
        public override float DefaultPoolWeight => 5f;
        public override Key GiveKey => ModConfig.ShapeShifter.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var overlay = ShapeShifterOverlay.Instance;
            if (overlay == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[ShapeShifter] No ShapeShifterOverlay instance found."
                );
                return;
            }

            overlay.OpenChooser(inventory.EquippedItemIndex);
        }
    }
}
