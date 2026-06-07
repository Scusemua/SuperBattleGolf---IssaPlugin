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
                $"[IronMan] OnUse — NetworkClient.active={NetworkClient.active} NetworkServer.active={NetworkServer.active} Writer.write={(Writer<IronManActivateMessage>.write != null ? "OK" : "NULL")}"
            );
            try
            {
                NetworkClient.Send(new IronManActivateMessage());
                IssaPluginPlugin.Log.LogInfo("[IronMan] NetworkClient.Send completed");
            }
            catch (System.Exception e)
            {
                IssaPluginPlugin.Log.LogError($"[IronMan] NetworkClient.Send threw: {e}");
            }
        }
    }
}
