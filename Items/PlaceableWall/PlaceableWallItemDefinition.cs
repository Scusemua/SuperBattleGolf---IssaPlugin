using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class PlaceableWallItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.PlaceableWallItemType;
        public override string DisplayName => "Placeable Wall";
        public override string[] ConsoleAliases => new[] { "wall", "placewall" };

        public override Sprite Icon => AssetLoader.WallIcon;
        public override GameObject HeldModelPrefab => AssetLoader.WallHandheldPrefab;

        public override int MaxUses => (int)ModConfig.PlaceableWall.Uses.Value;
        public override int Tier => 1;
        public override Key GiveKey => ModConfig.PlaceableWall.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            inventory.GetComponent<PlaceableWallNetworkBridge>()?.ClientPlace();
        }

        // Called every frame for the local player while this item is equipped.
        // Adds the preview component once; it removes itself when the item is unequipped.
        public override void OnEquip(PlayerInventory inventory)
        {
            if (inventory.GetComponent<PlaceableWallPlacementPreview>() == null)
                inventory.gameObject.AddComponent<PlaceableWallPlacementPreview>();
        }
    }
}
