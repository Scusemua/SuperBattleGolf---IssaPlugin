using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class JavelinItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => JavelinItem.JavelinItemType;
        public override string DisplayName => "Javelin";
        public override string[] ConsoleAliases => new[] { "javelin" };
        public override Sprite Icon => AssetLoader.JavelinIcon;
        public override GameObject HeldModelPrefab => AssetLoader.JavelinHandheldPrefab;
        public override int MaxUses => (int)Configuration.JavelinUses.Value;
        public override float SpawnWeight => Configuration.JavelinSpawnWeight.Value;
        public override Key GiveKey => Configuration.JavelinGiveKey.Value;
        public override ItemType AnimatorItemType => ItemType.RocketLauncher;
        public override ItemType AnimatorChangedItemType => ItemType.RocketLauncher;

        public override void OnUse(PlayerInventory inventory)
        {
            var bridge = inventory.GetComponent<JavelinNetworkBridge>();
            if (bridge != null)
            {
                bridge.ClientUpdateLockOn();
                if (bridge.HasLock && !bridge.IsWaitingForDetonation)
                    bridge.ClientFire(bridge.ClientLockedTarget);
                else if (bridge.IsWaitingForDetonation)
                    IssaPluginPlugin.Log.LogInfo("[Javelin] Already in-flight, please wait.");
                else
                    IssaPluginPlugin.Log.LogInfo("[Javelin] No lock — aim at the ground first.");
            }
            else
                IssaPluginPlugin.Log.LogError("[Javelin] No JavelinNetworkBridge on player.");
        }

        // OnEquip is called every frame while equipped — guard with null check to avoid
        // adding multiple components.
        public override void OnEquip(PlayerInventory inventory)
        {
            if (inventory.GetComponent<JavelinLockOnIndicator>() == null)
                inventory.gameObject.AddComponent<JavelinLockOnIndicator>();
        }
    }
}
