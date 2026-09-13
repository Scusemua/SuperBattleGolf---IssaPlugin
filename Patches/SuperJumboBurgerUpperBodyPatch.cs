using System.Reflection;
using HarmonyLib;
using IssaPlugin.Items;

namespace IssaPlugin.Patches
{
    /// <summary>
    /// Keeps the animator's upper-body layer enabled while eating a Super Jumbo Burger,
    /// so the eat animation plays over a locomotion animation instead of being
    /// suppressed while the player is moving.
    ///
    /// The base game's gate (PlayerAnimatorIo.ShouldUpperBodyLayerBeEnabled) has a clause
    /// written specifically for its own burger:
    ///
    ///     GetEffectivelyEquippedItem(true) == ItemType.JumboBurger
    ///         &amp;&amp; CurrentItemUse != ItemUseType.None
    ///
    /// That is what lets the vanilla burger's eat animation play while running. It tests
    /// the literal ItemType.JumboBurger, so our item (136) fails it.
    ///
    /// The gate's other clause fails too: it reads GetEffectivelyEquippedItem(false),
    /// which GetEffectivelyEquippedItemPatch deliberately forces to None for every custom
    /// item. With both clauses false the layer stays off, the eat animation has no layer
    /// to play on, and the locomotion animation wins — which is why it looked fine
    /// standing still and did nothing while running.
    ///
    /// A Postfix that ORs in our own condition is the narrowest fix available: it can
    /// only ever enable the layer, never disable one the base game wanted on, and it
    /// leaves GetEffectivelyEquippedItem — which many other systems read — untouched.
    /// </summary>
    [HarmonyPatch]
    static class SuperJumboBurgerUpperBodyPatch
    {
        static MethodBase TargetMethod() =>
            AccessTools.Method(
                typeof(PlayerAnimatorIo),
                "<LocalPlayerUpdateUpperBodyLayerEnabled>g__ShouldUpperBodyLayerBeEnabled|153_0"
            );

        static void Postfix(PlayerAnimatorIo __instance, ref bool __result)
        {
            // Already enabled — nothing to add.
            if (__result || __instance == null)
                return;

            var info = __instance.GetComponent<PlayerInfo>();
            var inventory = info != null ? info.Inventory : null;
            if (inventory == null)
                return;

            // Mirrors the base game's own burger clause, but for our item type.
            if (
                inventory.GetEffectivelyEquippedItem(true)
                    == ItemRegistry.SuperJumboBurgerItemType
                && inventory.CurrentItemUse != ItemUseType.None
            )
                __result = true;
        }
    }
}
