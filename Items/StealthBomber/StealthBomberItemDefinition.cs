using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    class StealthBomberItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.StealthBomberItemType;
        public override string DisplayName => "Stealth Bomber";
        public override string[] ConsoleAliases => new[] { "stealthbomber", "bomber" };
        public override Sprite Icon => AssetLoader.BomberIcon;
        public override GameObject HeldModelPrefab => AssetLoader.BomberTabletPrefab;
        public override int MaxUses => (int)ModConfig.StealthBomber.Uses.Value;
        public override float DefaultPoolWeight => 10f;
        public override Key GiveKey => ModConfig.StealthBomber.GiveKey.Value;

        // OnUse uses StartCoroutine — PlayerInventory is a MonoBehaviour, so this is valid.
        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(StealthBomberItem.BomberRunRoutine(inventory));
    }
}
