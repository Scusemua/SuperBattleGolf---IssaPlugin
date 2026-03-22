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
        public override int MaxUses => (int)Configuration.AC130Uses.Value;
        public override float SpawnWeight {
            get { return Configuration.AC130SpawnWeight.Value; }
            set { Configuration.AC130SpawnWeight.Value = value; }
        }
        public override Key GiveKey => Configuration.AC130GiveKey.Value;

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
