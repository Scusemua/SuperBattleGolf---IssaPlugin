using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class AC130ItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.AC130ItemType;
        public override string DisplayName => "AC130 Gunship";
        public override string[] ConsoleAliases => new[] { "ac130" };
        public override Sprite Icon => AssetLoader.AC130Icon;
        public override GameObject HeldModelPrefab => AssetLoader.Ac130TabletPrefab;
        public override int MaxUses => (int)ModConfig.AC130.Uses.Value;
        public override float DefaultPoolWeight => 3f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead      => 0f,
            GlobalConfig.PoolAhead     => 0f,
            GlobalConfig.PoolBehind50  => 1f,
            GlobalConfig.PoolBehind125 => 2f,
            GlobalConfig.PoolMobility  => 2f,
            _                          => DefaultPoolWeight,
        };
        public override Key GiveKey => ModConfig.AC130.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<AC130NetworkBridge>();
            if (bridge != null)
                NetworkClient.Send(new AC130StartMessage());
            else
                IssaPluginPlugin.Log.LogError("[AC130] No AC130NetworkBridge on player.");
        }
    }
}
