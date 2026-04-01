using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    class TeleporterItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.TeleporterItemType;
        public override string DisplayName => "Teleporter";
        public override string[] ConsoleAliases => new[] { "teleporter", "teleport", "tp" };
        public override Sprite Icon => AssetLoader.TeleporterIcon;
        public override GameObject HeldModelPrefab => AssetLoader.TeleporterHandheldPrefab;
        public override int MaxUses => (int)Configuration.TeleporterUses.Value;
        public override float SpawnWeight
        {
            get => Configuration.TeleporterSpawnWeight.Value;
            set => Configuration.TeleporterSpawnWeight.Value = value;
        }
        public override Key GiveKey => Configuration.TeleporterGiveKey.Value;

        // OnUse kicks off the client-side targeting coroutine.
        // PlayerInventory is a MonoBehaviour so StartCoroutine is valid here.
        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(TeleporterItem.TeleporterUseRoutine(inventory));
    }
}
