using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class StickyGrenadeItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.StickyGrenadeItemType;
        public override string DisplayName => "Sticky Grenade";
        public override string[] ConsoleAliases =>
            new[] { "sticky_grenade", "sticky", "stickygrenade" };
        public override Sprite Icon => AssetLoader.StickyGrenadeIcon;
        public override GameObject HeldModelPrefab => AssetLoader.StickyGrenadePrefab;
        public override int MaxUses => (int)Configuration.StickyGrenadeUses.Value;
        public override float SpawnWeight => Configuration.StickyGrenadeSpawnWeight.Value;
        public override Key GiveKey => Configuration.StickyGrenadeGiveKey.Value;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<StickyGrenadeNetworkBridge>();
            if (bridge != null)
                bridge.ClientThrow();
            else
                IssaPluginPlugin.Log.LogError(
                    "[StickyGrenade] No StickyGrenadeNetworkBridge on player."
                );
        }

        // OnEquip is called every frame — guard with null check.
        // StickyGrenadeTrajectoryPreview self-destructs when a different item is equipped.
        public override void OnEquip(PlayerInventory inventory)
        {
            if (inventory.GetComponent<StickyGrenadeTrajectoryPreview>() == null)
                inventory.gameObject.AddComponent<StickyGrenadeTrajectoryPreview>();
        }
    }
}
