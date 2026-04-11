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
        public override int MaxUses => (int)ModConfig.Harrier.Uses.Value;

        public override float DefaultPoolWeight => 5f;

        public override Key GiveKey => ModConfig.Harrier.GiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            inventory.connectionToServer?.Send(new HarrierRequestMessage());
        }
    }
}
