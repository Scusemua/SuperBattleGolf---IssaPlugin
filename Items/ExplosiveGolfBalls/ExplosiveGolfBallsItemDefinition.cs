using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    class ExplosiveGolfBallsItemDefinition : CustomItemDefinition
    {
        public override ItemType ItemType => ItemRegistry.ExplosiveGolfBallsItemType;
        public override string DisplayName => "Explosive Golf Balls";
        public override string[] ConsoleAliases => ["explodingballs", "expballs", "explosiveballs"];

        // Optional bundle assets — null until added to the bundle, at which point fallback is replaced.
        public override Sprite Icon => AssetLoader.ExplosiveGolfBallsIcon;
        public override GameObject HeldModelPrefab => AssetLoader.ExplosiveGolfBallsHandheldPrefab;

        public override int MaxUses => (int)ModConfig.ExplosiveGolfBalls.Uses.Value;
        public override float DefaultPoolWeight => 10f;
        public override Key GiveKey => ModConfig.ExplosiveGolfBalls.GiveKey.Value;

        // Passive item: the patch in ExplosiveGolfBallsPatches consumes uses at swing time.
        // OnUse is intentionally empty — the player does not need to equip this item.
        public override void OnUse(PlayerInventory inventory) { }
    }
}
