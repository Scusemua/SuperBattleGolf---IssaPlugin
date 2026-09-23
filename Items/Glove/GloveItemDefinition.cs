using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Glove — pick up and carry your own golf ball, then drop or throw it.
    ///
    /// Pose matches Freeze World / Low Gravity / Wind Storm: base-class defaults
    /// (RocketLauncher equipment, OrbitalLaser animator, no override controller).
    /// </summary>
    public class GloveItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.GloveItemType;
        public override string DisplayName => "Glove";
        public override string[] ConsoleAliases => new[] { "glove", "ballglove" };
        public override Sprite Icon => AssetLoader.GloveIcon;
        public override GameObject HeldModelPrefab => AssetLoader.GloveHandheldPrefab;
        public override int MaxUses => (int)ModConfig.Glove.Uses.Value;
        public override float DefaultPoolWeight => 10f;

        public override float GetDefaultPoolWeight(int poolIndex) =>
            poolIndex switch
            {
                GlobalConfig.PoolLead => 5f,
                GlobalConfig.PoolAhead => 5f,
                _ => DefaultPoolWeight,
            };

        public override Key GiveKey => ModConfig.Glove.GiveKey.Value;

        // Pickup does not require aim. Charge/throw after pickup is handled by the bridge.
        public override bool RequiresAimToUse => false;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<GloveNetworkBridge>();
            if (bridge != null)
                bridge.ClientRequestPickup();
            else
                IssaPluginPlugin.Log.LogError("[Glove] No GloveNetworkBridge on player.");
        }
    }
}
