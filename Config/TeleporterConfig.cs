using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class TeleporterConfig
    {
        private const string Section = "Teleporter";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> TargetMoveSpeed { get; private set; }
        public ConfigEntry<float> MarkerRadius { get; private set; }

        public TeleporterConfig(ConfigFile cfg)
        {
            GiveKey = cfg.Bind(Section, "TeleporterGiveKey", Key.Home, "Hotkey to give yourself a Teleporter (debug/testing). Key.None = disabled.");
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Teleporter pickup.");
            SpawnWeight = cfg.Bind("ItemBoxSpawns", "TeleporterSpawnWeight", 10f, "Relative spawn weight for the Teleporter in the item pool.");
            TargetMoveSpeed = cfg.Bind(Section, "TargetMoveSpeed", 60f, "Speed (units/second) at which the target marker moves during the targeting phase.");
            MarkerRadius = cfg.Bind(Section, "MarkerRadius", 1f, "Radius (units) of the circular target marker disc shown in the targeting UI.");
        }
    }
}
