using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class UfoAbductionConfig
    {
        private const string Section = "UfoAbduction";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> LockOnRange { get; private set; }
        public ConfigEntry<float> LockOnConeAngleDeg { get; private set; }

        // Phase durations
        public ConfigEntry<float> ApproachDuration { get; private set; }
        public ConfigEntry<float> AbductionDuration { get; private set; }
        public ConfigEntry<float> AscentDuration { get; private set; }

        // Heights
        public ConfigEntry<float> HoverHeight { get; private set; }
        public ConfigEntry<float> DropHeight { get; private set; }

        // Spring physics during abduction / ascent
        public ConfigEntry<float> SpringForce { get; private set; }
        public ConfigEntry<float> MaxPullSpeed { get; private set; }
        public ConfigEntry<float> NaturalLength { get; private set; }

        // Explosion on release
        public ConfigEntry<float> ExplosionForce { get; private set; }
        public ConfigEntry<float> ExplosionRadius { get; private set; }

        // Kept for backward compat; no longer used in transit phase
        public ConfigEntry<float> AscentDriftAmplitude { get; private set; }
        public ConfigEntry<float> AscentDriftFrequency { get; private set; }

        public ConfigEntry<float> DropoffSelectionTimeout { get; private set; }

        public UfoAbductionConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(
                Section,
                "GiveKey",
                Key.None,
                "Hotkey to give yourself a UFO Abduction (debug/testing)."
            );
            Uses = cfg.Bind(Section, "Uses", 1f, "Number of uses per UFO Abduction pickup.");
            LockOnRange = cfg.Bind(
                Section,
                "LockOnRange",
                100f,
                "Maximum targeting distance (units)."
            );
            LockOnConeAngleDeg = cfg.Bind(
                Section,
                "LockOnConeAngleDeg",
                60f,
                "Full aim-cone angle (degrees) for lock-on."
            );
            ApproachDuration = cfg.Bind(
                Section,
                "ApproachDuration",
                10f,
                "Seconds for UFO to fly over the target before beginning abduction."
            );
            AbductionDuration = cfg.Bind(
                Section,
                "AbductionDuration",
                3f,
                "Seconds the UFO hovers and beams the victim upward."
            );
            AscentDuration = cfg.Bind(
                Section,
                "AscentDuration",
                5f,
                "Transit duration — seconds for UFO to fly to the drop zone."
            );
            HoverHeight = cfg.Bind(
                Section,
                "HoverHeight",
                20f,
                "Height (units) above victim's position where the UFO hovers during abduction."
            );
            DropHeight = cfg.Bind(
                Section,
                "DropHeight",
                20f,
                "Height (units) above the chosen destination from which the UFO releases the victim."
            );
            SpringForce = cfg.Bind(
                Section,
                "SpringForce",
                1f,
                "Spring stiffness pulling the victim toward the UFO."
            );
            MaxPullSpeed = cfg.Bind(
                Section,
                "MaxPullSpeed",
                15f,
                "Maximum velocity change (m/s) applied to the victim per physics tick."
            );
            NaturalLength = cfg.Bind(
                Section,
                "NaturalLength",
                35f,
                "Distance below the UFO at which no pull force is applied."
            );
            ExplosionForce = cfg.Bind(
                Section,
                "ExplosionForce",
                200f,
                "Peak velocity change (m/s) applied at the explosion centre."
            );
            ExplosionRadius = cfg.Bind(
                Section,
                "ExplosionRadius",
                60f,
                "Radius (units) within which the explosion affects nearby players."
            );
            AscentDriftAmplitude = cfg.Bind(
                Section,
                "AscentDriftAmplitude",
                20f,
                "No longer used. Kept for backward compatibility."
            );
            AscentDriftFrequency = cfg.Bind(
                Section,
                "AscentDriftFrequency",
                1f,
                "No longer used. Kept for backward compatibility."
            );
            DropoffSelectionTimeout = cfg.Bind(
                Section,
                "DropoffSelectionTimeout",
                20f,
                "Seconds the wielder has to select a drop zone before the abduction is cancelled."
            );
        }
    }
}
