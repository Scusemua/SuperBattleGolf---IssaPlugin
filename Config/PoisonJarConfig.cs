using BepInEx.Configuration;
using UnityEngine.InputSystem;

namespace IssaPlugin
{
    public class PoisonJarConfig
    {
        private const string JarSection = "PoisonJar";
        private const string OverlaySection = "PoisonOverlay";

        public ConfigEntry<Key> GiveKey { get; private set; }
        public ConfigEntry<float> Uses { get; private set; }
        public ConfigEntry<float> SpawnWeight { get; private set; }
        public ConfigEntry<float> ThrowSpeed { get; private set; }
        public ConfigEntry<float> LobAngle { get; private set; }
        public ConfigEntry<float> Radius { get; private set; }
        public ConfigEntry<float> Duration { get; private set; }

        // Poison Overlay
        public ConfigEntry<bool> GhostEnabled { get; private set; }
        public ConfigEntry<float> GhostOffsetX { get; private set; }
        public ConfigEntry<float> GhostOffsetY { get; private set; }
        public ConfigEntry<float> GhostAlpha { get; private set; }
        public ConfigEntry<float> RollFreq1 { get; private set; }
        public ConfigEntry<float> RollAmp1 { get; private set; }
        public ConfigEntry<float> RollFreq2 { get; private set; }
        public ConfigEntry<float> RollAmp2 { get; private set; }
        public ConfigEntry<float> RollFreq3 { get; private set; }
        public ConfigEntry<float> RollAmp3 { get; private set; }
        public ConfigEntry<float> FovFreq1 { get; private set; }
        public ConfigEntry<float> FovAmp1 { get; private set; }
        public ConfigEntry<float> FovFreq2 { get; private set; }
        public ConfigEntry<float> FovAmp2 { get; private set; }
        public ConfigEntry<float> VigPulseFreq1 { get; private set; }
        public ConfigEntry<float> VigPulseFreq2 { get; private set; }
        public ConfigEntry<float> VigBaseAlpha { get; private set; }
        public ConfigEntry<float> VigPulseAmp1 { get; private set; }
        public ConfigEntry<float> VigPulseAmp2 { get; private set; }
        public ConfigEntry<float> TintBaseAlpha { get; private set; }
        public ConfigEntry<float> TintPulseAmp { get; private set; }
        public ConfigEntry<float> TintPulseFreq { get; private set; }
        public ConfigEntry<bool> AimNoiseEnabled { get; private set; }
        public ConfigEntry<float> AimNoiseYawAmp { get; private set; }
        public ConfigEntry<float> AimNoiseYawFreq { get; private set; }
        public ConfigEntry<float> AimNoisePitchAmp { get; private set; }
        public ConfigEntry<float> AimNoisePitchFreq { get; private set; }

        public PoisonJarConfig(ConfigFile cfg, GlobalConfig global)
        {
            GiveKey = cfg.Bind(JarSection, "GiveKey", Key.NumpadPeriod, "Debug key to add the Poison Jar to your inventory.");
            Uses = cfg.Bind(JarSection, "Uses", 1f, "Number of uses per Poison Jar pickup.");
            ThrowSpeed = cfg.Bind(JarSection, "ThrowSpeed", 18.0f, "Initial throw speed in metres per second.");
            LobAngle = cfg.Bind(JarSection, "LobAngle", 0.35f, "Upward component added to the throw direction (0 = flat, 1 = 45° up).");
            Radius = cfg.Bind(JarSection, "Radius", 2.0f, "Radius in metres of the poison AoE. Also scales the splash VFX.");
            Duration = cfg.Bind(JarSection, "Duration", 10.0f, "How long (seconds) the poison visual lasts on affected players.");

            SpawnWeight = global.BindSpawnWeight(cfg, 117, "PoisonJarSpawnWeight", 10f, "Override spawn weight for the Poison Jar.");

            GhostEnabled = cfg.Bind(OverlaySection, "GhostEnabled", true, "Enable the double-vision ghost image.");
            GhostOffsetX = cfg.Bind(OverlaySection, "GhostOffsetX", 0.04f, "Horizontal ghost offset as a fraction of screen width (e.g. 0.04 = 4%).");
            GhostOffsetY = cfg.Bind(OverlaySection, "GhostOffsetY", 0.01f, "Vertical ghost offset as a fraction of screen height.");
            GhostAlpha = cfg.Bind(OverlaySection, "GhostAlpha", 0.35f, "Opacity of the ghost image (0–1).");
            RollFreq1 = cfg.Bind(OverlaySection, "RollFreq1", 1.0f, "Primary lean frequency (Hz).");
            RollAmp1 = cfg.Bind(OverlaySection, "RollAmp1", 4.5f, "Primary lean amplitude (degrees).");
            RollFreq2 = cfg.Bind(OverlaySection, "RollFreq2", 0.28f, "Slow drift frequency (Hz).");
            RollAmp2 = cfg.Bind(OverlaySection, "RollAmp2", 12f, "Slow drift amplitude (degrees).");
            RollFreq3 = cfg.Bind(OverlaySection, "RollFreq3", 1.35f, "Subtle tremor frequency (Hz).");
            RollAmp3 = cfg.Bind(OverlaySection, "RollAmp3", 5f, "Subtle tremor amplitude (degrees).");
            FovFreq1 = cfg.Bind(OverlaySection, "FovFreq1", 0.5f, "Primary FOV breathing frequency (Hz).");
            FovAmp1 = cfg.Bind(OverlaySection, "FovAmp1", 5f, "Primary FOV breathing amplitude (degrees).");
            FovFreq2 = cfg.Bind(OverlaySection, "FovFreq2", 1.2f, "Secondary FOV breathing frequency (Hz).");
            FovAmp2 = cfg.Bind(OverlaySection, "FovAmp2", 8f, "Secondary FOV breathing amplitude (degrees).");
            VigPulseFreq1 = cfg.Bind(OverlaySection, "VigPulseFreq1", 1.2f, "Vignette primary pulse frequency (Hz).");
            VigPulseFreq2 = cfg.Bind(OverlaySection, "VigPulseFreq2", 6f, "Vignette secondary pulse frequency (Hz).");
            VigBaseAlpha = cfg.Bind(OverlaySection, "VigBaseAlpha", 0.50f, "Vignette base opacity (0–1).");
            VigPulseAmp1 = cfg.Bind(OverlaySection, "VigPulseAmp1", 0.15f, "Vignette primary pulse amplitude.");
            VigPulseAmp2 = cfg.Bind(OverlaySection, "VigPulseAmp2", 0.08f, "Vignette secondary pulse amplitude.");
            TintBaseAlpha = cfg.Bind(OverlaySection, "TintBaseAlpha", 0.1f, "Full-screen tint base opacity (0–1).");
            TintPulseAmp = cfg.Bind(OverlaySection, "TintPulseAmp", 0.05f, "Full-screen tint pulse amplitude.");
            TintPulseFreq = cfg.Bind(OverlaySection, "TintPulseFreq", 3f, "Full-screen tint pulse frequency (Hz).");
            AimNoiseEnabled = cfg.Bind(OverlaySection, "AimNoiseEnabled", true, "If true, adds sinusoidal yaw and pitch drift to make aiming harder while poisoned.");
            AimNoiseYawAmp = cfg.Bind(OverlaySection, "AimNoiseYawAmp", 2.5f, "Yaw (horizontal) aim drift amplitude in degrees.");
            AimNoiseYawFreq = cfg.Bind(OverlaySection, "AimNoiseYawFreq", 0.8f, "Yaw aim drift frequency in Hz.");
            AimNoisePitchAmp = cfg.Bind(OverlaySection, "AimNoisePitchAmp", 1.5f, "Pitch (vertical) aim drift amplitude in degrees.");
            AimNoisePitchFreq = cfg.Bind(OverlaySection, "AimNoisePitchFreq", 1.1f, "Pitch aim drift frequency in Hz.");
        }
    }
}
