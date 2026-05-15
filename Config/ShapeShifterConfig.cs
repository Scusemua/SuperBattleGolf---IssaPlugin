using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class ShapeShifterConfig
    {
        private const string Section = "ShapeShifter";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> PhysicsStaticFriction { get; private set; }
        public ConfigEntry<float> PhysicsDynamicFriction { get; private set; }
        public ConfigEntry<float> PhysicsBounciness { get; private set; }
        public ConfigEntry<float> PhysicsAngularDamping { get; private set; }
        public ConfigEntry<float> PhysicsHitSpinFactor { get; private set; }

        // ── Per-shape enable/disable ──────────────────────────────────────────
        public ConfigEntry<bool> ShapeCubeEnabled { get; private set; }
        public ConfigEntry<bool> ShapeDiskEnabled { get; private set; }
        public ConfigEntry<bool> ShapeCylinderEnabled { get; private set; }
        public ConfigEntry<bool> ShapeConeEnabled { get; private set; }
        public ConfigEntry<bool> ShapePyramidEnabled { get; private set; }
        public ConfigEntry<bool> ShapeAcornEnabled { get; private set; }
        public ConfigEntry<bool> ShapeIsosphereEnabled { get; private set; }

        public ShapeShifterConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Hotkey to give yourself a Shape Shifter (debug/testing)."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per Shape Shifter pickup.");
            Duration = cfg.Bind(
                Section,
                "Duration",
                20f,
                "Seconds the target player's golf ball remains a cube."
            );
            SpawnWeight = cfg.Bind(
                Section,
                "SpawnWeight",
                5f,
                "Relative spawn weight for the Shape Shifter item in the item pool."
            );
            PhysicsStaticFriction = cfg.Bind(
                Section,
                "PhysicsStaticFriction",
                3f,
                "Static friction of the cube's physics material. Higher = more grip, more tumbling."
            );
            PhysicsDynamicFriction = cfg.Bind(
                Section,
                "PhysicsDynamicFriction",
                2f,
                "Dynamic friction of the cube's physics material. Higher = more resistance while sliding."
            );
            PhysicsBounciness = cfg.Bind(
                Section,
                "PhysicsBounciness",
                1.0f,
                new ConfigDescription(
                    "Bounciness of the cube's physics material (0 = no bounce, 1 = full bounce).",
                    new AcceptableValueRange<float>(0.0f, 1.0f)
                )
            );
            PhysicsAngularDamping = cfg.Bind(
                Section,
                "PhysicsAngularDamping",
                0.002f,
                new ConfigDescription(
                    "Angular damping override while the cube effect is active. Lower = more tumbling. "
                        + "The game normally resets this each frame; this patch overrides it after the reset.",
                    new AcceptableValueRange<float>(0.0f, 10.0f)
                )
            );
            PhysicsHitSpinFactor = cfg.Bind(
                Section,
                "PhysicsHitSpinFactor",
                5.0f,
                new ConfigDescription(
                    "Multiplier for the angular impulse applied when a cubed ball is hit. "
                        + "Higher = cube starts tumbling more immediately instead of sliding flat. "
                        + "Set to 0 to disable.",
                    new AcceptableValueRange<float>(0.0f, 20.0f)
                )
            );

            const string shapeSection = "ShapeShifter.Shapes";
            const string shapeDesc =
                "Whether this shape can be randomly selected by the Shape Shifter and Super Shape Shifter.";
            ShapeCubeEnabled = cfg.Bind(shapeSection, "Cube", true, shapeDesc);
            ShapeDiskEnabled = cfg.Bind(shapeSection, "Disk", true, shapeDesc);
            ShapeCylinderEnabled = cfg.Bind(shapeSection, "Cylinder", true, shapeDesc);
            ShapeConeEnabled = cfg.Bind(shapeSection, "Cone", true, shapeDesc);
            ShapePyramidEnabled = cfg.Bind(shapeSection, "Pyramid", true, shapeDesc);
            ShapeAcornEnabled = cfg.Bind(shapeSection, "Acorn", true, shapeDesc);
            ShapeIsosphereEnabled = cfg.Bind(shapeSection, "Isosphere", true, shapeDesc);
        }
    }
}
