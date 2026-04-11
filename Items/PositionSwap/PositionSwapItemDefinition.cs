using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class PositionSwapItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.PositionSwapItemType;
        public override string DisplayName => "Position Swap";
        public override string[] ConsoleAliases => new[] { "positionswap", "swap" };
        public override Sprite Icon => AssetLoader.PositionSwapIcon;
        public override GameObject HeldModelPrefab => AssetLoader.PositionSwapHandheldPrefab;
        public override int MaxUses => (int)ModConfig.PositionSwap.Uses.Value;
        public override float DefaultPoolWeight => 15f;
        public override Key GiveKey => ModConfig.PositionSwap.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            // OnUse fires only on the local client. Open the chooser overlay; the item
            // itself is consumed later by the server once a target is confirmed.
            IssaPlugin.Overlays.PositionSwapOverlay.Instance?.OpenChooser();
        }
    }
}
