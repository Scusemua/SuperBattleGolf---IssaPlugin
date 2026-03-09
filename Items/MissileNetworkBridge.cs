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

        public bool IsSteering => _isSteering;

        /// Static accessor for overlays — true when the local player is steering a missile.
        public static bool IsAnySteering { get; private set; }

        // ================================================================
        //  Client → Server
        // ================================================================

        [Command]
        public void CmdRequestMissile()
        {
            var inventory = GetComponent<PlayerInventory>();
            if (inventory == null)
            {
                IssaPluginPlugin.Log.LogError("[Missile] No PlayerInventory on bridge object.");
                return;
            }
            StartCoroutine(PredatorMissileItem.ServerMissileRoutine(inventory, this));
        }

        [Command]
        public void CmdSetMissileVelocity(Vector3 velocity)
        {
            if (_activeRocket == null)
                return;
            var rb = _activeRocket.GetComponent<Rigidbody>();
            if (rb != null)
                rb.linearVelocity = velocity;
        }

        [Command]
        public void CmdDetonateMissile()
        {
            if (_activeRocket == null)
                return;
            PredatorMissileItem.ServerExplode(_activeRocket);
            _activeRocket = null;
        }

        // ================================================================
        //  Server → Client
        // ================================================================

        /// Called by the server on the specific client who fired the missile.
        /// Passes the rocket's NetworkIdentity so the client can find it locally.
        [TargetRpc]
        public void TargetBeginSteering(NetworkConnection target, NetworkIdentity rocketIdentity)
        {
            if (rocketIdentity == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[Missile] TargetBeginSteering: rocketIdentity is null."
                );
                return;
            }

            _activeRocket = rocketIdentity.GetComponent<Rocket>();
            StartCoroutine(LocalSteeringCoroutine());
        }

        /// Called by the server when the missile has exploded or timed out,
        /// to clean up client state even if the client didn't trigger it.
        [TargetRpc]
        public void TargetEndSteering(NetworkConnection target)
        {
            _isSteering = false;
            IsAnySteering = false;
            _activeRocket = null;
        }

        /// Server calls this to clear its own reference after the routine ends.
        public void ServerClearSteering()
        {
            _activeRocket = null;
        }

        // ================================================================
        //  Local client steering loop
        // ================================================================

        private IEnumerator LocalSteeringCoroutine()
        {
            if (_activeRocket == null)
                yield break;

            _isSteering = true;
            IsAnySteering = true;
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
                    CmdDetonateMissile();
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
                CmdSetMissileVelocity(new Vector3(steer.x, -fallSpeed, steer.z));

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
            IsAnySteering = false;
            _activeRocket = null;

            IssaPluginPlugin.Log.LogInfo("[Missile] Client steering ended.");
        }
    }
}
