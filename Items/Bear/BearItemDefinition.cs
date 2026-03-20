using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

namespace IssaPlugin.Items
{
    public class BearItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => BearItem.BearItemType;
        public override string DisplayName => "Bear";
        public override string[] ConsoleAliases => new[] { "bear", "bears" };
        public override Sprite Icon => AssetLoader.BearIcon;
        public override GameObject HeldModelPrefab => AssetLoader.TeddyBearPrefab;
        public override int MaxUses => (int)Configuration.BearUses.Value;
        public override float SpawnWeight => Configuration.BearSpawnWeight.Value;
        public override Key GiveKey => Configuration.BearGiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<BearNetworkBridge>();
            if (bridge != null)
                NetworkClient.Send(new BearSummonMessage());
            else
                IssaPluginPlugin.Log.LogError("[Bear] No BearNetworkBridge on player.");
        }
    }
}
