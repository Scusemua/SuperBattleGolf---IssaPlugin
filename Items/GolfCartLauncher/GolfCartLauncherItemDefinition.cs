using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class GolfCartLauncherItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.GolfCartLauncherItemType;
        public override string DisplayName => "Golf Cart Launcher";
        public override string[] ConsoleAliases =>
            new[] { "golfcartlauncher", "cartlauncher", "cartgun", "carts" };

        // TODO: add "golf_cart_launcher_icon.png" and "golf_cart_launcher.prefab" to the
        // asset bundle, then populate these in AssetLoader. Until then both are null —
        // UseRocketIconFallback keeps the item usable with the rocket launcher's icon/model.
        public override Sprite Icon => AssetLoader.GolfCartLauncherIcon;
        public override GameObject HeldModelPrefab => AssetLoader.GolfCartLauncherPrefab;

        public override bool UseRocketIconFallback => true;

        public override int MaxUses => (int)ModConfig.GolfCartLauncher.Uses.Value;
        public override float DefaultPoolWeight => 5f;

        public override float GetDefaultPoolWeight(int poolIndex) => poolIndex switch
        {
            GlobalConfig.PoolLead => 1f,
            GlobalConfig.PoolAhead => 1f,
            _ => DefaultPoolWeight,
        };

        public override Key GiveKey => ModConfig.GolfCartLauncher.GiveKey.Value;

        // Held and animated as a rocket launcher — it is a shoulder-fired launcher that
        // happens to fire golf carts. EquipmentType/Animator defaults from the base class
        // are already RocketLauncher/OrbitalLaser, so only the animator types are pinned here.
        public override ItemType AnimatorItemType => ItemType.RocketLauncher;
        public override ItemType AnimatorChangedItemType => ItemType.RocketLauncher;

        // Fires only while aimed in, like the base game's rocket launcher.
        public override bool RequiresAimToUse => true;

        // Hold the launcher in the rocket launcher's stance while idle, not just
        // while aiming.
        public override bool InheritAnimatorOverrideController => true;

        public override void OnUse(PlayerInventory inventory) =>
            inventory.StartCoroutine(GolfCartLauncherItem.FireLoop(inventory));
    }
}
