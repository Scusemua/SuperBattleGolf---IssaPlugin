using UnityEngine;

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

        // Mutable loop state
        public float AltitudeOffset;
        public float Elapsed;
        public float Cooldown;
        public float CurrentFov;

        // Scene objects
        public readonly Vector3 MapCentre;
        public readonly GameObject PivotGo;
        public readonly GameObject GunshipVisual;
        public readonly AC130FlyBehaviour FlyComp;
        public readonly OrbitCameraModule OrbitModule;
        public readonly float SavedPitch;
        public readonly float SavedYaw;
        public readonly bool SavedDisablePhysics;

        public readonly float OriginalFov;

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

            // Camera — save state but don't switch to gunship view yet
            CameraModuleController.TryGetOrbitModule(out OrbitModule);
            SavedPitch = OrbitModule?.Pitch ?? 0f;
            SavedYaw = OrbitModule?.Yaw ?? 0f;
            SavedDisablePhysics = OrbitModule?.disablePhysics ?? false;

            PivotGo = new GameObject("AC130Pivot");
            PivotGo.transform.position = mapCentre;

            OriginalFov = Camera.main != null ? Camera.main.fieldOfView : 60f;
            CurrentFov = OriginalFov;

            // Gunship visual — spawn at the approach position, not the orbit start
            float startAngle = 0f;
            Vector3 orbitEntry = AC130Helpers.OrbitPosition(
                mapCentre, startAngle, OrbitRadius, Altitude
            );
            Vector3 approachDir = AC130Helpers.OrbitTangent(startAngle);

            float approachDist = Configuration.AC130ApproachDistance.Value;
            float approachSpeed = Configuration.AC130ApproachSpeed.Value;

            Vector3 spawnPos = orbitEntry - approachDir * approachDist;

            if (AssetLoader.AC130Prefab != null)
            {
                GunshipVisual = Object.Instantiate(
                    AssetLoader.AC130Prefab,
                    spawnPos,
                    Quaternion.LookRotation(approachDir, Vector3.up)
                );

                FlyComp = GunshipVisual.AddComponent<AC130FlyBehaviour>();
                FlyComp.mapCentre = mapCentre;
                FlyComp.orbitRadius = OrbitRadius;
                FlyComp.altitude = Altitude;
                FlyComp.orbitSpeed = BaseOrbitSpeed;
                FlyComp.currentAngle = startAngle;
                FlyComp.flyTarget = orbitEntry;
                FlyComp.flySpeed = approachSpeed;
                FlyComp.mode = AC130FlightMode.FlyIn;
            }
            else
            {
                IssaPluginPlugin.Log.LogInfo("[AC130] No prefab found, running without visual.");
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[AC130] Spawned at approach distance {approachDist:F0}m, "
                + $"flying in at {approachSpeed:F0} m/s."
            );
        }

        /// <summary>
        /// Switches the camera to the gunship view. Called after fly-in completes.
        /// </summary>
        public void BeginGunshipView()
        {
            if (OrbitModule != null)
            {
                OrbitModule.SetSubject(PivotGo.transform);
                OrbitModule.SetPitch(Configuration.AC130CameraPitch.Value);
                OrbitModule.SetDistanceAddition(Configuration.AC130CameraDistance.Value);
                OrbitModule.disablePhysics = true;
                OrbitModule.ForceUpdateModule();
            }

            IssaPluginPlugin.Log.LogInfo(
                $"[AC130] Gunship on station. Active for {Duration:F0}s, "
                + $"orbit radius {OrbitRadius:F0}m, altitude {Altitude:F0}m."
            );
        }

        /// <summary>
        /// Triggers fly-out on the visual and restores the camera.
        /// The visual auto-destroys itself once it's far enough away.
        /// </summary>
        public void Cleanup()
        {
            if (GunshipVisual != null && FlyComp != null)
            {
                FlyComp.BeginFlyOut();
                // Don't destroy — the FlyOut mode handles self-destruction
            }
            else if (GunshipVisual != null)
            {
                Object.Destroy(GunshipVisual);
            }

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

            if (Camera.main != null)
                Camera.main.fieldOfView = OriginalFov;

            InputManager.Controls.Gameplay.Enable();
        }
    }
}
