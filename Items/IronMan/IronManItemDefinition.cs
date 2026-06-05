using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class IronManItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.IronManItemType;
        public override string DisplayName => "Iron Man";
        public override string[] ConsoleAliases => new[] { "ironman", "iron", "ironsuit" };

        public override Sprite Icon => AssetLoader.IronManIcon;
        public override GameObject HeldModelPrefab => AssetLoader.IronManHandheldPrefab;

        public override int MaxUses => 1;
        public override float DefaultPoolWeight => 8f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead  => 4f,
            GlobalConfig.PoolAhead => 4f,
            _                      => DefaultPoolWeight,
        };

        public override Key GiveKey => ModConfig.IronMan.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            IssaPluginPlugin.Log.LogInfo(
                $"[IronMan] Player {inventory.PlayerInfo.PlayerId.PlayerName} is activating Iron Man suit."
            );
            NetworkClient.Send(new IronManActivateMessage());
        }
    }
}
