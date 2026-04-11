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
        public override GameObject HeldModelPrefab => AssetLoader.JetpackHandheldPrefab;

        // UseRocketIconFallback not overridden — base class default (true) uses the
        // rocket-launcher icon as placeholder, which is more appropriate than pistol.

        public override int MaxUses => (int)ModConfig.Jetpack.Uses.Value;
        public override float DefaultPoolWeight => 10f;
        public override Key GiveKey => ModConfig.Jetpack.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            IssaPluginPlugin.Log.LogInfo(
                $"[Jetpack] Player {inventory.PlayerInfo.PlayerId.PlayerName} is using Jetpack."
            );
            inventory.StartCoroutine(JetpackItem.FireLoop(inventory));
        }
    }
}
