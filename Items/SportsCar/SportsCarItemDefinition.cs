using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class SportsCarItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType    => ItemRegistry.SportsCarItemType;
        public override string DisplayName  => "Sports Car";
        public override string[] ConsoleAliases => new[] { "sportscar", "car" };

        // No custom 3D held model — the item icon is shown in the HUD but there is
        // nothing to render in the player's hand (the vehicle is world-spawned on use).
        public override Sprite Icon           => AssetLoader.SportsCarSprite; 
        public override GameObject HeldModelPrefab => AssetLoader.SportsCarHandheldPrefab;

        public override int MaxUses      => (int)Configuration.SportsCarUses.Value;
        public override float SpawnWeight
        {
            get => Configuration.SportsCarSpawnWeight.Value;
            set => Configuration.SportsCarSpawnWeight.Value = value;
        }
        public override Key GiveKey => Configuration.SportsCarGiveKey.Value;

        // No hand pose needed — player immediately enters the cart after use.
        public override EquipmentType EquipmentType    => EquipmentType.None;
        public override ItemType AnimatorItemType      => ItemType.OrbitalLaser;
        public override ItemType AnimatorChangedItemType => ItemType.OrbitalLaser;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<SportsCarNetworkBridge>();
            if (bridge != null)
                NetworkClient.Send(new SportsCarActivateMessage());
            else
                IssaPluginPlugin.Log.LogError("[SportsCar] No SportsCarNetworkBridge on player.");
        }
    }
}
