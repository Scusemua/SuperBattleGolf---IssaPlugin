using System.Collections;
using System.Reflection;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    // This class bridges the client-side overlay/input logic with the server-side session management.
    public class MissileNetworkBridge : NetworkBehaviour
    {
        private Rocket _activeRocket;
        private bool _isSteering;

        /// True on the server while this player's missile routine is running.
        private bool _serverMissileActive;

        public bool IsSteering => _isSteering;

        // ================================================================
        //  Client → Server
        // ================================================================

        public void ServerRequestMissile()
        {
            // Per-instance guard — prevents this player stacking multiple
            // missile routines without blocking other players.
            if (_serverMissileActive)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Missile] Missile already active for this player."
                );
                return;
            }

            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogError("[Missile] No PlayerInventory on bridge object.");
                return;
            }

            _serverMissileActive = true;
            StartCoroutine(PredatorMissileItem.ServerMissileRoutine(inventory, this));
        }

        public void ServerSetMissileVelocity(Vector3 velocity)
        {
            if (_activeRocket == null)
                return;
            var rb = _activeRocket.GetComponent<Rigidbody>();
            if (rb != null)
                rb.linearVelocity = velocity;
        }

        public void ServerDetonateMissile()
        {
            if (_activeRocket == null)
                return;
            PredatorMissileItem.ServerExplode(_activeRocket);
            _activeRocket = null;
        }

        // ================================================================
        //  Server → Client
        // ================================================================

        /// <summary>
        /// Called by the server on the specific client who fired the missile.
        /// Passes the rocket's netId so the client can find it in NetworkClient.spawned.
        /// </summary>
        public void ClientBeginSteering(uint rocketNetId)
        {
            if (!NetworkClient.spawned.TryGetValue(rocketNetId, out var ni) || ni == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    "[Missile] ClientBeginSteering: rocket not found in spawned."
                );
                return;
            }
            _activeRocket = ni.GetComponent<Rocket>();
            StartCoroutine(LocalSteeringCoroutine());
        }

        /// <summary>
        /// Called by the server when the missile has exploded or timed out,
        /// to clean up client state even if the client didn't trigger it.
        /// </summary>
        public void ClientEndSteering()
        {
            _isSteering = false;
            _activeRocket = null;
        }

        /// <summary>Server calls this to clear its own references after the routine ends.</summary>
        public void ServerClearSteering()
        {
            _activeRocket = null;
            _serverMissileActive = false;
        }

        // ================================================================
        //  Local client steering loop
        // ================================================================

        private IEnumerator LocalSteeringCoroutine()
        {
            if (_activeRocket == null)
                yield break;

            _isSteering = true;
            InputManager.Controls.Gameplay.Disable();

            OrbitCameraModule orbitModule = null;
            CameraModuleController.TryGetOrbitModule(out orbitModule);

            float savedPitch = orbitModule?.Pitch ?? 0f;
            float savedYaw = orbitModule?.Yaw ?? 0f;

            if (orbitModule != null)
            {
                orbitModule.SetSubject(_activeRocket.transform);
                orbitModule.SetPitch(80f);
                orbitModule.ForceUpdateModule();
            }

            float fallSpeed = Configuration.MissileFallSpeed.Value;
            float steerSpeed = Configuration.MissileSteerSpeed.Value;

            while (_isSteering && _activeRocket != null && _activeRocket.gameObject != null)
            {
                var keyboard = Keyboard.current;
                var mouse = Mouse.current;

                // Manual detonate
                if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                {
                    NetworkClient.Send(new MissileDetonateMessage());
                    break;
                }

                float inputX = 0f,
                    inputZ = 0f;
                if (keyboard != null)
                {
                    if (keyboard[Key.W].isPressed || keyboard[Key.UpArrow].isPressed)
                        inputZ += 1f;
                    if (keyboard[Key.S].isPressed || keyboard[Key.DownArrow].isPressed)
                        inputZ -= 1f;
                    if (keyboard[Key.A].isPressed || keyboard[Key.LeftArrow].isPressed)
                        inputX -= 1f;
                    if (keyboard[Key.D].isPressed || keyboard[Key.RightArrow].isPressed)
                        inputX += 1f;
                }

                Vector3 camForward = Vector3.forward;
                Vector3 camRight = Vector3.right;
                if (orbitModule != null)
                {
                    float yawRad = orbitModule.Yaw * Mathf.Deg2Rad;
                    camForward = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
                    camRight = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));
                }

                Vector3 steer = (camRight * inputX + camForward * inputZ) * steerSpeed;
                NetworkClient.Send(
                    new MissileSetVelocityMessage
                    {
                        Velocity = new Vector3(steer.x, -fallSpeed, steer.z),
                    }
                );

                yield return null;
            }

            // Restore camera and input
            if (orbitModule != null)
            {
                var playerMovement = GameManager.LocalPlayerMovement;
                if (playerMovement != null)
                    orbitModule.SetSubject(playerMovement.transform);

                orbitModule.SetPitch(savedPitch);
                orbitModule.SetYaw(savedYaw);
                orbitModule.ForceUpdateModule();
            }

            InputManager.Controls.Gameplay.Enable();
            _isSteering = false;
            _activeRocket = null;

            IssaPluginPlugin.Log.LogInfo("[Missile] Client steering ended.");
        }
    }
}
