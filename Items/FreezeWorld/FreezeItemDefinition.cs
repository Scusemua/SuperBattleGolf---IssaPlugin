using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

namespace IssaPlugin.Items
{
    public class FreezeItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.FreezeItemType;
        public override string DisplayName => "Freeze World";
        public override string[] ConsoleAliases => new[] { "freezeworld", "freeze" };
        public override Sprite Icon => AssetLoader.FreezeIcon;
        public override GameObject HeldModelPrefab => AssetLoader.FreezeModelPrefab;
        public override int MaxUses => (int)Configuration.FreezeUses.Value;
        public override float SpawnWeight => Configuration.FreezeSpawnWeight.Value;
        public override Key GiveKey => Configuration.FreezeGiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<FreezeNetworkBridge>();
            if (bridge != null)
                NetworkClient.Send(new FreezeActivateMessage());
            else
                IssaPluginPlugin.Log.LogError("[Freeze] No FreezeNetworkBridge on player.");
        }
    }
}
