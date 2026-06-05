using System.Collections;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Client-side Iron Man flight and firing logic.
    ///
    /// Flight model: while the session is active the player's Rigidbody is driven by
    /// AddForce each FixedUpdate using WASD + the camera's orientation. Gravity is
    /// counteracted so the player hovers at their current altitude when no input is given.
    /// Flight input is sent to the server at a capped rate (InputSendInterval) so the
    /// server can authorise and rebroadcast thruster VFX.
    ///
    /// Rocket firing: LMB fires one rocket per click (server-authorised via IronManFireMessage).
    /// The client sends the camera aim direction; the server spawns the rocket.
    ///
    /// Session lifecycle:
    ///   1. OnUse sends IronManActivateMessage.
    ///   2. Server validates, starts the timer, and sends IronManConfigMessage to the wielder
    ///      and IronManSuitBeginMessage to all clients.
    ///   3. IronManItem.StartSession() begins the local flight loop.
    ///   4. Session ends when the timer expires or the hole ends; server sends IronManSuitEndMessage.
    /// </summary>
    public static class IronManItem
    {
        // ── Server-synced config (set by IronManNetworkBridge.HandleConfig) ────
        internal static float  ServerDuration            = -1f;
        internal static int    ServerMaxRockets          = -1;
        internal static float  ServerFlightSpeed         = -1f;
        internal static float  ServerRocketExplosionScale = -1f;

        // ── Session state (local player only) ────────────────────────────────
        private static bool    _sessionActive;
        private static int     _rocketsRemaining;
        private static float   _sessionTimeRemaining;
        private static Coroutine _flightCoroutine;

        // ── Rate-limiting ─────────────────────────────────────────────────────
        private const float InputSendInterval = 0.05f; // 20 Hz
        private const float FireCooldown      = 0.25f; // max 4 rockets/s

        private static float _inputSendTimer;
        private static float _fireCooldownTimer;

        // ── Read by IronManOverlay ─────────────────────────────────────────────
        public static bool  SessionActive      => _sessionActive;
        public static int   RocketsRemaining   => _rocketsRemaining;
        public static float SessionTimeRemaining => _sessionTimeRemaining;

        // ── Per-hole reset ────────────────────────────────────────────────────
        public static void ResetSession()
        {
            _sessionActive         = false;
            _rocketsRemaining      = 0;
            _sessionTimeRemaining  = 0f;
            _flightCoroutine       = null;
        }

        /// <summary>
        /// Called by IronManNetworkBridge when the server confirms the session start
        /// and delivers authoritative config values.
        /// </summary>
        public static void StartSession(PlayerInventory inventory, IronManConfigMessage cfg)
        {
            if (_sessionActive)
                return;

            ServerDuration             = cfg.Duration;
            ServerMaxRockets           = cfg.MaxRockets;
            ServerFlightSpeed          = cfg.FlightSpeed;
            ServerRocketExplosionScale  = cfg.RocketExplosionScale;

            _sessionActive        = true;
            _rocketsRemaining     = cfg.MaxRockets;
            _sessionTimeRemaining = cfg.Duration;
            _inputSendTimer       = 0f;
            _fireCooldownTimer    = 0f;

            var bridge = inventory.GetComponent<IronManNetworkBridge>();
            _flightCoroutine = inventory.StartCoroutine(FlightLoop(inventory, bridge));
        }

        /// <summary>
        /// Ends the local session (called by bridge on suit-end or hole cleanup).
        /// </summary>
        public static void EndSession(PlayerInventory inventory)
        {
            if (!_sessionActive)
                return;

            _sessionActive = false;

            if (_flightCoroutine != null)
            {
                inventory.StopCoroutine(_flightCoroutine);
                _flightCoroutine = null;
            }
        }

        private static IEnumerator FlightLoop(PlayerInventory inventory, IronManNetworkBridge bridge)
        {
            Rigidbody rb = GameManager.LocalPlayerMovement?.GetComponent<Rigidbody>();
            if (rb == null)
            {
                _sessionActive = false;
                yield break;
            }

            var cam = Camera.main;
            bool wasThrusterOn = false;

            while (_sessionActive && _sessionTimeRemaining > 0f)
            {
                yield return new WaitForFixedUpdate();

                _sessionTimeRemaining -= Time.fixedDeltaTime;
                _inputSendTimer       -= Time.fixedDeltaTime;
                _fireCooldownTimer    -= Time.fixedDeltaTime;

                // ── Flight input ──────────────────────────────────────────────
                float speed = ServerFlightSpeed > 0f ? ServerFlightSpeed : ModConfig.IronMan.FlightSpeed.Value;

                // Build world-space move direction from WASD relative to camera yaw.
                Vector3 camForward = cam != null
                    ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized
                    : Vector3.forward;
                Vector3 camRight = cam != null
                    ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized
                    : Vector3.right;

                var kb = Keyboard.current;
                Vector3 horizInput = Vector3.zero;
                if (kb != null)
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed)   horizInput += camForward;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  horizInput -= camForward;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) horizInput += camRight;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  horizInput -= camRight;
                }

                // Vertical: Space = up, Left-Ctrl = down.
                float vertInput = 0f;
                if (kb != null)
                {
                    if (kb.spaceKey.isPressed)        vertInput += 1f;
                    if (kb.leftCtrlKey.isPressed)     vertInput -= 1f;
                }

                Vector3 moveDir = horizInput.normalized;
                if (vertInput != 0f)
                    moveDir = (moveDir + Vector3.up * vertInput).normalized;

                // Counteract gravity and apply directional thrust via acceleration.
                // Physics.gravity is typically -9.81 m/s², so adding the inverse keeps the
                // player hovering when moveDir is zero.
                Vector3 gravityCancel = -Physics.gravity;
                Vector3 thrust        = moveDir * speed;

                rb.AddForce(gravityCancel + thrust, ForceMode.Acceleration);

                // ── Rate-limited input send to server ─────────────────────────
                bool thrusterOn = moveDir.sqrMagnitude > 0.01f;
                bool sendInput  = _inputSendTimer <= 0f && thrusterOn;

                if (_inputSendTimer <= 0f)
                {
                    _inputSendTimer = InputSendInterval;
                    if (thrusterOn)
                        NetworkClient.Send(new IronManFlightInputMessage { MoveDirection = moveDir });
                }

                // Notify bridge of thruster state change so server can broadcast VFX.
                if (thrusterOn != wasThrusterOn)
                {
                    bridge?.ClientNotifyThrusterChange(thrusterOn);
                    wasThrusterOn = thrusterOn;
                }

                // ── Rocket firing ─────────────────────────────────────────────
                if (
                    _rocketsRemaining > 0
                    && _fireCooldownTimer <= 0f
                    && Mouse.current != null
                    && Mouse.current.leftButton.wasPressedThisFrame
                )
                {
                    Vector3 aimDir = cam != null ? cam.transform.forward : Vector3.forward;
                    NetworkClient.Send(new IronManFireMessage { AimDirection = aimDir });
                    _fireCooldownTimer = FireCooldown;
                    // Ammo is decremented when the server sends IronManAmmoMessage.
                }
            }

            // Session timer expired — end locally; server will send SuitEnd to all.
            _sessionActive = false;
            if (wasThrusterOn)
                bridge?.ClientNotifyThrusterChange(false);
        }

        /// <summary>Called by bridge when the server delivers an ammo update.</summary>
        public static void ApplyAmmoUpdate(int remaining)
        {
            _rocketsRemaining = remaining;
        }
    }
}
