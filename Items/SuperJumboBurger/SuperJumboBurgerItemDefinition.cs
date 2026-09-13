using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// A larger, configurable version of the base game's Jumbo Burger.
    ///
    /// Icon and model both come from the mod's asset bundle.
    /// </summary>
    class SuperJumboBurgerItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.SuperJumboBurgerItemType;
        public override string DisplayName => "Super Jumbo Burger";
        public override string[] ConsoleAliases => ["superburger", "megaburger", "bigmac"];

        public override Sprite Icon => AssetLoader.SuperJumboBurgerIcon;
        public override GameObject HeldModelPrefab => AssetLoader.SuperJumboBurgerPrefab;

        public override int MaxUses => (int)ModConfig.SuperJumboBurger.Uses.Value;
        public override float DefaultPoolWeight => 1f; // rare — it is a strong effect

        public override Key GiveKey => ModConfig.SuperJumboBurger.GiveKey.Value;

        // Reuse the base game's Jumbo Burger stances and animations wholesale: it is
        // eaten rather than aimed, and should look identical in hand.
        //   AnimatorItemType         — upper-body pose while equipped (local player).
        //   AnimatorChangedItemType  — the same pose on all OTHER clients, driven by the
        //                              equipment SyncVar hook rather than the local path.
        //   InheritAnimatorOverrideController — borrows the burger's override controller
        //                              so the IDLE stance matches too; without it the
        //                              player idles empty-handed and only looks right
        //                              mid-use.
        //   EquipmentType            — the burger's equipment slot/model category.
        public override ItemType AnimatorItemType => ItemType.JumboBurger;
        public override ItemType AnimatorChangedItemType => ItemType.JumboBurger;
        public override bool InheritAnimatorOverrideController => true;
        public override EquipmentType EquipmentType => EquipmentType.JumboBurger;

        public override void OnUse(PlayerInventory inventory)
        {
            // Only consume the item and broadcast effects if the form actually started.
            // The base game can refuse activation (match resolved, knocked out, frozen,
            // already giant), and eating a use for nothing would be worse than a no-op.
            if (!SuperJumboBurgerBehaviour.Activate(inventory))
                return;

            ItemHelper.ConsumeEquippedItem(inventory);

            // Tell the server to broadcast the eat/grow VFX to every client. The size
            // change itself needs no message — it rides the characterScale SyncVar.
            inventory.GetComponent<SuperJumboBurgerNetworkBridge>()?.ClientRequestEffects();
        }
    }
}
