using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

namespace IssaPlugin.Items
{
    public class PredatorMissileItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.PredatorMissileItemType;
        public override string DisplayName => "Predator Missile";
        public override string[] ConsoleAliases => new[] { "predatormissile", "missile" };
        public override Sprite Icon => AssetLoader.MissileIcon;
        public override GameObject HeldModelPrefab => AssetLoader.MissileTabletPrefab;
        public override int MaxUses => (int)Configuration.MissileUses.Value;
        public override float SpawnWeight
        {
            get { return Configuration.MissileSpawnWeight.Value; }
            set { Configuration.MissileSpawnWeight.Value = value; }
        }
        public override Key GiveKey => Configuration.MissileGiveKey.Value;

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
