using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class PowerJammerItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.PowerJammerItemType;
        public override string DisplayName => "Power Jammer";
        public override string[] ConsoleAliases => new[] { "powerjammer", "jammer" };
        public override Sprite Icon => AssetLoader.PowerJammerIcon;
        public override GameObject HeldModelPrefab => AssetLoader.PowerJammerModelPrefab;
        public override int MaxUses => (int)ModConfig.PowerJammer.Uses.Value;
        public override float DefaultPoolWeight => 5f;
        public override Key GiveKey => ModConfig.PowerJammer.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<PowerJammerNetworkBridge>();
            if (bridge != null)
                NetworkClient.Send(new PowerJammerActivateMessage());
            else
                IssaPluginPlugin.Log.LogError(
                    "[PowerJammer] No PowerJammerNetworkBridge on player."
                );
        }
    }
}
