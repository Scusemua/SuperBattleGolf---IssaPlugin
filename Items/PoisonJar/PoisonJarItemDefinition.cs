using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Item definition for the Poison Jar (ItemType 117).
    ///
    /// On use: the player lobs the jar as a ballistic projectile.
    /// On landing: a configurable-radius AoE applies the poison (poison-vision) overlay
    /// to all players within range — including the thrower — for a configurable duration.
    /// </summary>
    public class PoisonJarItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.PoisonJarItemType;
        public override string DisplayName => "Poison Jar";
        public override string[] ConsoleAliases => ["poisonjar", "poison"];

        public override Sprite Icon => AssetLoader.PoisonJarIcon;
        public override GameObject HeldModelPrefab => AssetLoader.PoisonJarHandheldPrefab;

        public override int MaxUses => (int)ModConfig.PoisonJar.Uses.Value;
        public override float DefaultPoolWeight => 10f;
        public override Key GiveKey => ModConfig.PoisonJar.GiveKey.Value;

        public override void OnEquip(PlayerInventory inventory)
        {
            var preview = inventory.GetComponent<StickyGrenadeTrajectoryPreview>();
            if (preview == null)
            {
                preview = inventory.gameObject.AddComponent<StickyGrenadeTrajectoryPreview>();
                preview.TargetItemType = ItemRegistry.PoisonJarItemType;
                preview.ThrowSpeed = () => ModConfig.PoisonJar.ThrowSpeed.Value;
                preview.LobAngle = () => ModConfig.PoisonJar.LobAngle.Value;
            }
        }

        public override void OnUse(PlayerInventory inventory)
        {
            inventory.GetComponent<PoisonJarNetworkBridge>()?.ClientThrow();
        }
    }
}
