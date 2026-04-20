using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class BoobyTrapItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.BoobyTrapItemType;
        public override string DisplayName => "Booby Trap";
        public override string[] ConsoleAliases => new[] { "booby", "boobytrap", "trap" };
        public override Sprite Icon => AssetLoader.BoobyTrapIcon;
        public override GameObject HeldModelPrefab => AssetLoader.BoobyTrapHandheldPrefab;
        public override int MaxUses => (int)ModConfig.BoobyTrap.Uses.Value;
        public override float DefaultPoolWeight => 8f;
        public override Key GiveKey => ModConfig.BoobyTrap.GiveKey.Value;

        // Use the melee animator slot so the held model is displayed in-hand
        // without triggering ranged-weapon animations.
        public override ItemType AnimatorItemType => ItemType.None;
        public override ItemType AnimatorChangedItemType => ItemType.None;

        public override void OnUse(PlayerInventory inventory)
        {
            var overlay = BoobyTrapOverlay.Instance;
            if (overlay == null)
            {
                IssaPluginPlugin.Log.LogError("[BoobyTrap] BoobyTrapOverlay.Instance is null.");
                return;
            }

            if (overlay.Mode == BoobyTrapOverlay.TrapMode.WorldTarget && overlay.HasWorldTarget)
            {
                // Trap the highlighted world object.
                NetworkClient.Send(
                    new BoobyTrapPlaceMessage
                    {
                        TargetType = overlay.BestTargetType,
                        TargetNetId = overlay.BestTargetNetId,
                    }
                );
            }
            else if (overlay.Mode == BoobyTrapOverlay.TrapMode.InventoryItem)
            {
                int slot = overlay.HighlightedSlotIndex;
                if (slot < 0)
                {
                    IssaPluginPlugin.Log.LogWarning(
                        "[BoobyTrap] No inventory slot highlighted — use aborted."
                    );
                    return;
                }

                NetworkClient.Send(new BoobyTrapDropItemMessage { SourceSlotIndex = slot });
            }
            else
            {
                // No target found — give the player feedback via overlay.
                overlay.ShowNoTarget();
            }
        }

        /// <summary>
        /// Called every frame while the item is equipped. Delegates all per-frame
        /// logic to BoobyTrapOverlay, which is already running as a persistent
        /// Plugin-level MonoBehaviour.
        /// </summary>
        public override void OnEquip(PlayerInventory inventory) { }
    }
}
