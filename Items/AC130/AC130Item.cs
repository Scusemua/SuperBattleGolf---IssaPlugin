using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// Stateless utility class for AC130 functionality.
    ///
    /// All per-session state (active flag, force-end flag, gunship camera
    /// reference) has been moved to AC130NetworkBridge instance fields so
    /// that multiple players can run concurrent sessions without conflicts.
    public static class AC130Item
    {
        public static readonly ItemType AC130ItemType = (ItemType)103;

        // _useIndex is server-only and scoped per-rocket by PlayerId inside
        // ItemUseId, so it's safe to remain static.
        private static int _useIndex;

        /// Layer mask used for ground raycasts. Public so AC130NetworkBridge
        /// can use it without duplicating the GetMask call.
        public static readonly int GroundLayerMask = LayerMask.GetMask("Default", "Terrain");

        public static void GiveAC130ToLocalPlayer()
        {
            ItemHelper.GiveItemToLocalPlayer(AC130ItemType, Configuration.AC130Uses.Value, "AC130");
        }

        // ----------------------------------------------------------------
        //  Input handlers
        //  Called each frame by AC130NetworkBridge.RunLocalSession.
        // ----------------------------------------------------------------

        public static void HandleZoom(Mouse mouse, AC130Session session)
        {
            if (mouse == null || session.GunshipCam == null)
                return;

            float targetFov = mouse.rightButton.isPressed
                ? Configuration.AC130ZoomFov.Value
                : session.GunshipCam.baseFov;

            session.GunshipCam.SetFov(targetFov, Configuration.AC130ZoomSpeed.Value);
        }

        public static void HandleFlight(Keyboard keyboard, AC130Session s)
        {
            bool boosting = keyboard != null && keyboard[Key.LeftShift].isPressed;
            if (s.FlyComp != null)
                s.FlyComp.orbitSpeed = boosting ? s.BoostedOrbitSpeed : s.BaseOrbitSpeed;

            if (keyboard == null)
                return;

            if (keyboard[Key.Q].isPressed)
                s.AltitudeOffset -= s.AltitudeAdjustSpeed * Time.deltaTime;
            if (keyboard[Key.E].isPressed)
                s.AltitudeOffset += s.AltitudeAdjustSpeed * Time.deltaTime;

            s.AltitudeOffset = Mathf.Clamp(
                s.AltitudeOffset,
                -s.AltitudeOffsetMax,
                s.AltitudeOffsetMax
            );

            if (s.FlyComp != null)
                s.FlyComp.altitude = s.Altitude + s.AltitudeOffset;
        }

        // ----------------------------------------------------------------
        //  Server-side rocket spawning (called from AC130NetworkBridge)
        // ----------------------------------------------------------------

        public static void SpawnRocketInDirection(
            PlayerInventory inventory,
            Vector3 position,
            Quaternion worldRotation
        )
        {
            if (!NetworkServer.active)
                return;

            _useIndex++;
            var itemUseId = new ItemUseId(
                inventory.PlayerInfo.PlayerId.Guid,
                _useIndex,
                ItemType.RocketLauncher
            );

            var rocket = Object.Instantiate(
                GameManager.ItemSettings.RocketPrefab,
                position,
                worldRotation
            );

            if (rocket == null)
            {
                IssaPluginPlugin.Log.LogError("[AC130] Rocket did not instantiate.");
                return;
            }

            rocket.ServerInitialize(inventory.PlayerInfo, null, itemUseId);
            NetworkServer.Spawn(rocket.gameObject, (NetworkConnectionToClient)null);

            ExplosionScaler.Register(rocket, Configuration.AC130ExplosionScale.Value);
        }

        // ----------------------------------------------------------------
        //  Helpers
        // ----------------------------------------------------------------

        public static Vector3 ProjectAimToGround(Vector3 origin, Vector3 direction)
        {
            if (Mathf.Abs(direction.y) < 0.001f)
                return origin + direction * 500f;

            float t = -origin.y / direction.y;
            if (t < 0f)
                return origin + direction * 500f;

            return origin + direction * t;
        }

        /// <summary>
        /// Forces a server-side rocket to explode immediately in place.
        /// Used by the mayday impact to deal area damage.
        /// </summary>
        public static void ServerExplodeRocket(Rocket rocket)
        {
            if (rocket == null)
                return;

            var explodeMethod = typeof(Rocket).GetMethod(
                "ServerExplode",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
            );

            if (explodeMethod != null)
                explodeMethod.Invoke(rocket, new object[] { rocket.transform.position });
            else
                IssaPluginPlugin.Log.LogError("[AC130] Could not find ServerExplode method.");
        }
    }
}
