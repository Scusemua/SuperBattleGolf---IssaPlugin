using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Holds all client-side per-session state for a running AC130.
    /// The gunship GameObject is spawned on the server and passed in via
    /// its NetworkIdentity — this class does NOT instantiate it.
    /// </summary>
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

        // OrbitModule — used as a cinematic follow-cam during fly-in.
        public readonly OrbitCameraModule OrbitModule;
        public readonly float SavedPitch;
        public readonly float SavedYaw;
        public readonly bool SavedDisablePhysics;

        /// <summary>
        /// Constructs a client-side session wrapping a gunship GameObject
        /// that was already spawned on the server.
        /// </summary>
        public AC130Session(PlayerInventory inventory, GameObject gunshipGo, Vector3 mapCentre)
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

            if (gunshipGo != null)
            {
                GunshipVisual = gunshipGo;
                FlyComp = gunshipGo.GetComponent<AC130FlyBehaviour>();

                // Add the gunship camera — client-only, not networked.
                GunshipCam =
                    gunshipGo.GetComponent<AC130GunshipCamera>()
                    ?? gunshipGo.AddComponent<AC130GunshipCamera>();
                GunshipCam.mapCentre = mapCentre;
                GunshipCam.baseFov = Configuration.AC130BaseFov.Value;
                GunshipCam.yawLimit = Configuration.AC130YawLimit.Value;
                GunshipCam.pitchLimit = Configuration.AC130PitchLimit.Value;
                GunshipCam.mouseSensitivity = Configuration.AC130MouseSensitivity.Value;
            }
            else
            {
                IssaPluginPlugin.Log.LogWarning("[AC130] Session started with null gunship.");
            }
        }

        /// <summary>
        /// Switches from the fly-in OrbitModule view to the dedicated gunship camera.
        /// Called once, after the fly-in phase completes.
        /// </summary>
        public void BeginGunshipView()
        {
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

            if (GunshipCam != null)
                GunshipCam.Activate();

            IssaPluginPlugin.Log.LogInfo(
                $"[AC130] Gunship on station. Active for {Duration:F0}s, "
                    + $"orbit radius {OrbitRadius:F0}m, altitude {Altitude:F0}m."
            );
        }

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
        /// Normal end: restores OrbitModule, deactivates gunship camera,
        /// triggers fly-out, restores input.
        /// </summary>
        public void Cleanup()
        {
            RestoreOrbitModule();
            GunshipCam?.Deactivate();

            if (GunshipVisual != null && FlyComp != null)
                FlyComp.BeginFlyOut();
            else if (GunshipVisual != null)
                Object.Destroy(GunshipVisual);

            Object.Destroy(PivotGo);
            InputManager.Controls.Gameplay.Enable();
        }

        /// <summary>
        /// Called when mayday begins mid-session on the owning client.
        /// Restores OrbitModule and deactivates gunship camera so
        /// AC130MaydayBehaviour can take over. Input stays disabled —
        /// mayday uses it for pull/roll.
        /// </summary>
        public void CleanupForMayday()
        {
            RestoreOrbitModule();
            GunshipCam?.Deactivate();
            Object.Destroy(PivotGo);
        }
    }
}
