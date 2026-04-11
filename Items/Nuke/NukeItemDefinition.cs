using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class NukeItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.NukeItemType;
        public override string DisplayName => "Nuke";
        public override string[] ConsoleAliases => new[] { "nuke" };
        public override Sprite Icon => AssetLoader.NukeIcon;
        public override GameObject HeldModelPrefab => AssetLoader.NuclearDetonatorPrefab;
        public override int MaxUses => (int)ModConfig.Nuke.Uses.Value;
        public override float DefaultPoolWeight => 5f;
        public override Key GiveKey => ModConfig.Nuke.GiveKey.Value;
        public override ItemType AnimatorItemType => ItemType.RocketLauncher;
        public override ItemType AnimatorChangedItemType => ItemType.RocketLauncher;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<NukeNetworkBridge>();
            if (bridge != null)
                NetworkClient.Send(new NukeFireMessage());
            else
                IssaPluginPlugin.Log.LogError("[Nuke] No NukeNetworkBridge on player.");
        }
    }
}
