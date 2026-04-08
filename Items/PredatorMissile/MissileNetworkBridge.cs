using System.Collections;
using System.Reflection;
using IssaPlugin.Network;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    // This class bridges the client-side overlay/input logic with the server-side session management.
    public class MissileNetworkBridge : NetworkBridgeBase
    {
        private Rocket _activeRocket;
        private Rigidbody _activeRocketRigidbody;
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
            ItemWarningBroadcaster.Broadcast(
                inventory.PlayerInfo.PlayerId.PlayerName,
                ItemRegistry.PredatorMissileItemType,
                "Predator Missile",
                senderNetId: netId
            );
            StartCoroutine(PredatorMissileItem.ServerMissileRoutine(inventory, this));
        }

        public void ServerSetMissileVelocity(Vector3 velocity)
        {
            if (_activeRocket == null || _activeRocketRigidbody == null)
                return;
            // Clamp magnitude so a modified client cannot teleport the rocket
            // by sending an arbitrarily large velocity vector.
            float maxSpeed =
                ModConfig.PredatorMissile.FallSpeed.Value
                + ModConfig.PredatorMissile.SteerSpeed.Value * 1.5f;
            _activeRocketRigidbody.linearVelocity = Vector3.ClampMagnitude(velocity, maxSpeed);
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

        /// <summary>Called by the server coroutine to register the spawned rocket so that
        /// velocity and detonate messages can be forwarded to it.</summary>
        public void ServerSetActiveRocket(Rocket rocket)
        {
            _activeRocket = rocket;
            _activeRocketRigidbody = rocket?.GetComponent<Rigidbody>();
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

            float fallSpeed = ModConfig.PredatorMissile.FallSpeed.Value;
            float steerSpeed = ModConfig.PredatorMissile.SteerSpeed.Value;

            // Rate-limit velocity sends to 20 Hz and skip when velocity hasn't changed,
            // avoiding a packet every frame over the internet.
            const float SendInterval = 0.05f;
            float sendTimer = 0f;
            Vector3 lastSentVelocity = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);

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
                Vector3 desiredVelocity = new Vector3(steer.x, -fallSpeed, steer.z);

                sendTimer -= Time.deltaTime;

                // The timer is the sole gate for network sends (hard 20 Hz cap).
                // Previously the code used `velocityChanged || sendTimer <= 0f`, which
                // meant the timer was bypassed entirely whenever velocity was changing —
                // which is exactly always during flight, because the camera yaw updates
                // each frame as the rocket moves, making desiredVelocity shift slightly
                // each frame and keeping velocityChanged perpetually true. That caused
                // a send every single frame (~60-120/s), flooding the Kcp queue.
                if (sendTimer <= 0f)
                {
                    bool velocityChanged =
                        (desiredVelocity - lastSentVelocity).sqrMagnitude > 0.01f;
                    if (velocityChanged)
                    {
                        NetworkClient.Send(
                            new MissileSetVelocityMessage { Velocity = desiredVelocity }
                        );
                        lastSentVelocity = desiredVelocity;
                    }
                    // Always reset the timer whether or not we sent, so it never goes
                    // negative and the 20 Hz cadence is maintained precisely.
                    sendTimer = SendInterval;
                }

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

        // ── Hole transition cleanup ───────────────────────────────────────────

        public override void ServerHoleCleanup() /* Runs on server */
        { }

        public override void ClientHoleCleanup() /* Runs on client */
        { }
    }
}
