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
        public override int MaxUses => (int)Configuration.PositionSwapUses.Value;
        public override float SpawnWeight
        {
            get => Configuration.PositionSwapSpawnWeight.Value;
            set => Configuration.PositionSwapSpawnWeight.Value = value;
        }
        public override Key GiveKey => Configuration.PositionSwapGiveKey.Value;

        // Return false so the game doesn't count this as a "successful use" and try
        // to auto-advance/consume — the item is consumed server-side after target selection.
        public override bool UseResult => false;

        public override void OnUse(PlayerInventory inventory)
        {
            // OnUse fires only on the local client. Open the chooser overlay; the item
            // itself is consumed later by the server once a target is confirmed.
            IssaPlugin.Overlays.PositionSwapOverlay.Instance?.OpenChooser();
        }
    }
}
