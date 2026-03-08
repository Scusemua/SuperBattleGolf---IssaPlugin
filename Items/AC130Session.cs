using System.Collections;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    public class AC130Session
    {
        // Config
        public readonly float Altitude;
        public readonly float OrbitRadius;
        public readonly float Duration;
        public readonly float FireCooldown;
        public readonly float BaseOrbitSpeed;
        public readonly float BoostedOrbitSpeed;
        public readonly float AltitudeOffsetMax;
        public readonly float AltitudeAdjustSpeed;
        public readonly float AimYawSpeed;
        public readonly float AimPitchSpeed;
        public readonly float AimPitchMin;
        public readonly float AimPitchMax;
        public readonly float AimYawMax;

        // Mutable loop state
        public float AltitudeOffset;
        public float AimYaw;
        public float AimPitch;
        public float Elapsed;
        public float Cooldown;

        // Scene objects
        public readonly Vector3 MapCentre;
        public readonly GameObject PivotGo;
        public readonly GameObject GunshipVisual;
        public readonly AC130FlyBehaviour FlyComp;
        public readonly OrbitCameraModule OrbitModule;
        public readonly float SavedPitch;
        public readonly float SavedYaw;
        public readonly bool SavedDisablePhysics;

        public AC130Session(PlayerInventory inventory, Vector3 mapCentre)
        {
            MapCentre = mapCentre;
            Altitude = Configuration.AC130Altitude.Value;
            OrbitRadius = Configuration.AC130OrbitRadius.Value;
            Duration = Configuration.AC130Duration.Value;
            FireCooldown = Configuration.AC130FireCooldown.Value;
            BaseOrbitSpeed = Configuration.AC130OrbitSpeed.Value;
            BoostedOrbitSpeed = BaseOrbitSpeed * Configuration.AC130BoostMultiplier.Value;
            AltitudeOffsetMax = Configuration.AC130AltitudeOffsetMax.Value;
            AltitudeAdjustSpeed = Configuration.AC130AltitudeAdjustSpeed.Value;
            AimYawSpeed = Configuration.AC130AimYawSpeed.Value;
            AimPitchSpeed = Configuration.AC130AimPitchSpeed.Value;
            AimPitchMin = Configuration.AC130AimPitchMin.Value;
            AimPitchMax = Configuration.AC130AimPitchMax.Value;
            AimYawMax = Configuration.AC130AimYawMax.Value;
            AimPitch = Configuration.AC130AimPitchDefault.Value;

            // Camera
            CameraModuleController.TryGetOrbitModule(out OrbitModule);
            SavedPitch = OrbitModule?.Pitch ?? 0f;
            SavedYaw = OrbitModule?.Yaw ?? 0f;
            SavedDisablePhysics = OrbitModule?.disablePhysics ?? false;

            PivotGo = new GameObject("AC130Pivot");
            PivotGo.transform.position = mapCentre;

            if (OrbitModule != null)
            {
                OrbitModule.SetSubject(PivotGo.transform);
                OrbitModule.SetPitch(Configuration.AC130CameraPitch.Value);
                OrbitModule.SetDistanceAddition(Configuration.AC130CameraDistance.Value);
                OrbitModule.disablePhysics = true;
                OrbitModule.ForceUpdateModule();
            }

            // Gunship
            float startAngle = 0f;
            Vector3 startPos = AC130Helpers.OrbitPosition(mapCentre, startAngle, OrbitRadius, Altitude);
            Vector3 startForward = AC130Helpers.OrbitTangent(startAngle);

            if (AssetLoader.AC130Prefab != null)
            {
                GunshipVisual = Object.Instantiate(
                    AssetLoader.AC130Prefab,
                    startPos,
                    Quaternion.LookRotation(startForward, Vector3.up));

                FlyComp = GunshipVisual.AddComponent<AC130FlyBehaviour>();
                FlyComp.mapCentre = mapCentre;
                FlyComp.orbitRadius = OrbitRadius;
                FlyComp.altitude = Altitude;
                FlyComp.orbitSpeed = BaseOrbitSpeed;
                FlyComp.currentAngle = startAngle;
            }
            else
            {
                IssaPluginPlugin.Log.LogInfo("[AC130] No prefab found, running without visual.");
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[AC130] Active for {Duration:F0}s, orbit radius {OrbitRadius:F0}m, altitude {Altitude:F0}m.");
        }

        public void Cleanup()
        {
            if (GunshipVisual != null)
                Object.Destroy(GunshipVisual);

            Object.Destroy(PivotGo);

            var playerMovement = GameManager.LocalPlayerMovement;
            if (OrbitModule != null)
            {
                if (playerMovement != null)
                    OrbitModule.SetSubject(playerMovement.transform);

                OrbitModule.SetDistanceAddition(0f);
                OrbitModule.disablePhysics = SavedDisablePhysics;
                OrbitModule.SetPitch(SavedPitch);
                OrbitModule.SetYaw(SavedYaw);
                OrbitModule.ForceUpdateModule();
            }

            InputManager.Controls.Gameplay.Enable();
        }
    }
}
