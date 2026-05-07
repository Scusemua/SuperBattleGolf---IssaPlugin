using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class CubeBallItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.CubeBallItemType;
        public override string DisplayName => "Cube Ball";
        public override string[] ConsoleAliases => new[] { "cubeball", "cube_ball" };
        public override Sprite Icon => AssetLoader.CubeBallIcon;
        public override GameObject HeldModelPrefab => AssetLoader.CubeBallHandheldPrefab;
        public override int MaxUses => (int)ModConfig.CubeBall.Uses.Value;
        public override float DefaultPoolWeight => 5f;
        public override Key GiveKey => ModConfig.CubeBall.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var overlay = CubeBallOverlay.Instance;
            if (overlay == null)
            {
                IssaPluginPlugin.Log.LogError("[CubeBall] No CubeBallOverlay instance found.");
                return;
            }

            overlay.OpenChooser(inventory.EquippedItemIndex);
        }
    }
}
