using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class PredatorMissileItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.PredatorMissileItemType;
        public override string DisplayName => "Predator Missile";
        public override string[] ConsoleAliases => new[] { "predatormissile", "missile" };
        public override Sprite Icon => AssetLoader.MissileIcon;
        public override GameObject HeldModelPrefab => AssetLoader.MissileTabletPrefab;
        public override int MaxUses => (int)ModConfig.PredatorMissile.Uses.Value;
        public override float DefaultPoolWeight => 10f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead  => 5f,
            GlobalConfig.PoolAhead => 5f,
            _                      => DefaultPoolWeight,
        };
        public override Key GiveKey => ModConfig.PredatorMissile.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<MissileNetworkBridge>();
            if (bridge != null)
                NetworkClient.Send(new MissileRequestMessage());
            else
                IssaPluginPlugin.Log.LogError("[Missile] No MissileNetworkBridge on player.");
        }
    }
}
