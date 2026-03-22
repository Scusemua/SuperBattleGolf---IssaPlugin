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

        public override int MaxUses => (int)Configuration.PlaceableWallUses.Value;
        public override float SpawnWeight => Configuration.PlaceableWallSpawnWeight.Value;
        public override Key GiveKey => Configuration.PlaceableWallGiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            inventory.GetComponent<PlaceableWallNetworkBridge>()?.ClientPlace();
        }
    }
}
