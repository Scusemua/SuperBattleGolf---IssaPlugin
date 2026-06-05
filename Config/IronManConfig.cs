using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class IronManConfig
    {
        private const string Section = "IronMan";

        public ConfigEntry<Key>   GiveKey              { get; private set; }
        public ConfigEntry<float> Duration             { get; private set; }
        public ConfigEntry<int>   MaxRockets           { get; private set; }
        public ConfigEntry<float> FlightSpeed          { get; private set; }
        public ConfigEntry<float> RocketExplosionScale { get; private set; }

        public IronManConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Hotkey to give yourself an Iron Man suit (debug/testing). Key.None disables it."
            );
            Duration = cfg.Bind(
                Section,
                "Duration",
                20f,
                "How many seconds the Iron Man suit session lasts."
            );
            MaxRockets = cfg.Bind(
                Section,
                "MaxRockets",
                6,
                "Number of wrist rockets available per session. Session also ends if all rockets are fired."
            );
            FlightSpeed = cfg.Bind(
                Section,
                "FlightSpeed",
                18f,
                "Flight acceleration (m/s²) applied each physics step in the movement direction. Gravity is always counteracted so the player hovers when not pressing a direction key."
            );
            RocketExplosionScale = cfg.Bind(
                Section,
                "RocketExplosionScale",
                1.2f,
                "Explosion radius multiplier applied to each wrist rocket (1.0 = standard game rocket size)."
            );
        }
    }
}
