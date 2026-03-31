using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class JetpackItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.JetpackItemType;
        public override string DisplayName => "Jetpack";
        public override string[] ConsoleAliases => new[] { "jetpack", "jet", "jetpak" };

        public override Sprite Icon => AssetLoader.JetpackIcon;
        public override GameObject HeldModelPrefab => AssetLoader.JetpackEquippedPrefab;

        // UseRocketIconFallback not overridden — base class default (true) uses the
        // rocket-launcher icon as placeholder, which is more appropriate than pistol.

        public override int MaxUses => (int)Configuration.JetpackUses.Value;
        public override float SpawnWeight
        {
            get => Configuration.JetpackSpawnWeight.Value;
            set => Configuration.JetpackSpawnWeight.Value = value;
        }
        public override Key GiveKey => Configuration.JetpackGiveKey.Value;

        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(JetpackItem.FireLoop(inventory));
    }
}
