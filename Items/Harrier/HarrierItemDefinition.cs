using System.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class HarrierItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.HarrierItemType;
        public override string DisplayName => "Harrier Jet";
        public override string[] ConsoleAliases => new[] { "harrier", "harrierjet", "jet" };
        public override Sprite Icon => AssetLoader.HarrierIcon;
        public override GameObject HeldModelPrefab => AssetLoader.HarrierTabletPrefab;
        public override int MaxUses => (int)Configuration.HarrierUses.Value;

        public override int Tier => 3;

        public override Key GiveKey => Configuration.HarrierGiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            inventory.connectionToServer?.Send(new HarrierRequestMessage());
        }
    }
}
