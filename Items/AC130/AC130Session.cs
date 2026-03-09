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

        // Scene objects
        public readonly Vector3 MapCentre;
        public readonly GameObject PivotGo;
        public readonly GameObject GunshipVisual;
        public readonly AC130FlyBehaviour FlyComp;
        public readonly AC130GunshipCamera GunshipCam;

        // Fly-in still uses OrbitModule (it's fine as a cinematic follow-cam).
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

            // Save OrbitModule state so we can restore it during Cleanup.
            CameraModuleController.TryGetOrbitModule(out OrbitModule);
            SavedPitch = OrbitModule?.Pitch ?? 0f;
            SavedYaw = OrbitModule?.Yaw ?? 0f;
            SavedDisablePhysics = OrbitModule?.disablePhysics ?? false;

            PivotGo = new GameObject("AC130Pivot");
            PivotGo.transform.position = mapCentre;

            // ----------------------------------------------------------------
            //  Spawn the gunship visual at the approach position
            // ----------------------------------------------------------------
            float startAngle = 0f;
            Vector3 orbitEntry = AC130Helpers.OrbitPosition(
                mapCentre,
                startAngle,
                OrbitRadius,
                Altitude
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

                // Add the gunship camera component to the same GameObject.
                // The camera positions itself at the gunship transform and looks
                // toward mapCentre — no altitude/radius/tilt needed here.
                GunshipCam = GunshipVisual.AddComponent<AC130GunshipCamera>();
                GunshipCam.mapCentre = mapCentre;
                GunshipCam.baseFov = Configuration.AC130BaseFov.Value;
                GunshipCam.yawLimit = Configuration.AC130YawLimit.Value;
                GunshipCam.pitchLimit = Configuration.AC130PitchLimit.Value;
                GunshipCam.mouseSensitivity = Configuration.AC130MouseSensitivity.Value;
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
        /// Switches from the fly-in OrbitModule view to the dedicated gunship camera.
        /// Called once, after the fly-in phase completes.
        /// </summary>
        public void BeginGunshipView()
        {
            // Tear down the OrbitModule follow-cam used during fly-in.
            if (OrbitModule != null)
            {
                OrbitModule.SetFovOffset(0f);
                OrbitModule.SetDistanceAddition(0f);
                OrbitModule.disablePhysics = SavedDisablePhysics;

                var playerMovement = GameManager.LocalPlayerMovement;
                if (playerMovement != null)
                    OrbitModule.SetSubject(playerMovement.transform);

                OrbitModule.SetPitch(SavedPitch);
                OrbitModule.SetYaw(SavedYaw);
                OrbitModule.ForceUpdateModule();
            }

            // Hand off to the dedicated gunship camera.
            if (GunshipCam != null)
                GunshipCam.Activate();

            IssaPluginPlugin.Log.LogInfo(
                $"[AC130] Gunship on station. Active for {Duration:F0}s, "
                    + $"orbit radius {OrbitRadius:F0}m, altitude {Altitude:F0}m."
            );
        }

        /// <summary>
        /// Restores the OrbitModule to the state it was in before the session.
        /// Guarded by a flag so it is safe to call from both BeginGunshipView
        /// and Cleanup without double-applying.
        /// </summary>
        private bool _orbitModuleRestored;

        private void RestoreOrbitModule()
        {
            if (_orbitModuleRestored || OrbitModule == null)
                return;

            OrbitModule.SetFovOffset(0f);
            OrbitModule.SetDistanceAddition(0f);
            OrbitModule.disablePhysics = SavedDisablePhysics;

            var playerMovement = GameManager.LocalPlayerMovement;
            if (playerMovement != null)
                OrbitModule.SetSubject(playerMovement.transform);

            OrbitModule.SetPitch(SavedPitch);
            OrbitModule.SetYaw(SavedYaw);
            OrbitModule.ForceUpdateModule();

            _orbitModuleRestored = true;
        }

        /// <summary>
        /// Triggers fly-out on the visual, deactivates the gunship camera,
        /// and restores normal input.
        /// </summary>
        public void Cleanup()
        {
            // Restore the OrbitModule if we're exiting during fly-in (before
            // BeginGunshipView had a chance to do it). The guard flag inside
            // RestoreOrbitModule makes this a no-op if it already ran.
            RestoreOrbitModule();

            // Deactivate the gunship camera — restores whatever cam was active before.
            GunshipCam?.Deactivate();

            if (GunshipVisual != null && FlyComp != null)
            {
                FlyComp.BeginFlyOut();
                // AC130FlyBehaviour self-destructs the GameObject once far enough away.
            }
            else if (GunshipVisual != null)
            {
                Object.Destroy(GunshipVisual);
            }

            Object.Destroy(PivotGo);

            InputManager.Controls.Gameplay.Enable();
        }
    }
}
