using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class GrapplingHookItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.GrapplingHookItemType;
        public override string DisplayName => "Grappling Hook";
        public override string[] ConsoleAliases =>
            new[] { "grapple", "grappling", "grapplinghook", "hook" };

        public override Sprite Icon => AssetLoader.GrapplingHookIcon;
        public override GameObject HeldModelPrefab => AssetLoader.GrapplingHookPrefab;
        public override int MaxUses => (int)ModConfig.GrapplingHook.Uses.Value;
        public override float DefaultPoolWeight => 10f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 3f,
                GlobalConfig.PoolAhead => 4f,
                GlobalConfig.PoolMobility => 15f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.GrapplingHook.GiveKey.Value;
        public override bool InheritAnimatorOverrideController => true;

        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(GrapplingHookItem.Use(inventory));
    }
}
