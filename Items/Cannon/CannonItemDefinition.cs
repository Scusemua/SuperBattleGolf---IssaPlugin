using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class CannonItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.CannonItemType;
        public override string DisplayName => "Cannon";
        public override string[] ConsoleAliases => new[] { "cannon" };
        public override Sprite Icon => AssetLoader.CannonIcon;
        public override GameObject HeldModelPrefab => AssetLoader.CannonPrefab;

        public override bool UseRocketIconFallback => true;

        public override int MaxUses => (int)ModConfig.Cannon.Uses.Value;
        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 1f,
                GlobalConfig.PoolAhead => 1f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.Cannon.GiveKey.Value;

        public override ItemType AnimatorItemType => ItemType.RocketLauncher;
        public override ItemType AnimatorChangedItemType => ItemType.RocketLauncher;

        // Fires only while aimed in, like the base game's rocket launcher.
        public override bool RequiresAimToUse => true;

        // Hold the launcher in the rocket launcher's stance while idle, not just
        // while aiming.
        public override bool InheritAnimatorOverrideController => true;

        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(CannonItem.FireLoop(inventory));
    }
}
