using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Item definition for the Rocket Tether Grenade (ItemType 127).
    ///
    /// Thrown like a Poison Jar; on landing it detonates and attaches every player
    /// within <see cref="ModConfig.RocketTetherGrenade.BlastRadius"/> to an
    /// independent upward rocket — exactly the Rocket Tether effect, applied to
    /// multiple victims simultaneously.
    /// </summary>
    public class RocketTetherGrenadeItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.RocketTetherGrenadeItemType;
        public override string DisplayName => "Rocket Tether Grenade";
        public override string[] ConsoleAliases => ["rocket_tether_grenade", "rtgrenade"];

        public override Sprite Icon => AssetLoader.RocketTetherGrenadeIcon;
        public override GameObject HeldModelPrefab => AssetLoader.RocketTetherGrenadePrefab;

        public override int MaxUses => (int)ModConfig.RocketTetherGrenade.Uses.Value;
        public override float DefaultPoolWeight => 5f;
        public override Key GiveKey => ModConfig.RocketTetherGrenade.GiveKey.Value;

        public override void OnEquip(PlayerInventory inventory)
        {
            var preview = inventory.GetComponent<StickyGrenadeTrajectoryPreview>();
            if (preview == null)
            {
                preview = inventory.gameObject.AddComponent<StickyGrenadeTrajectoryPreview>();
                preview.TargetItemType = ItemRegistry.RocketTetherGrenadeItemType;
                preview.ThrowSpeed = () => ModConfig.RocketTetherGrenade.ThrowSpeed.Value;
                preview.LobAngle = () => ModConfig.RocketTetherGrenade.LobAngle.Value;
            }
        }

        public override void OnUse(PlayerInventory inventory)
        {
            inventory.GetComponent<RocketTetherGrenadeNetworkBridge>()?.ClientThrow();
        }
    }
}
