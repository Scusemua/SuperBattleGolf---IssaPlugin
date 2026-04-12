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
        public override int MaxUses => (int)ModConfig.Teleporter.Uses.Value;
        public override float DefaultPoolWeight => 10f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead  => 5f,
            GlobalConfig.PoolAhead => 5f,
            _                      => DefaultPoolWeight,
        };
        public override Key GiveKey => ModConfig.Teleporter.GiveKey.Value;

        // OnUse kicks off the client-side targeting coroutine.
        // PlayerInventory is a MonoBehaviour so StartCoroutine is valid here.
        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(TeleporterItem.TeleporterUseRoutine(inventory));
    }
}
