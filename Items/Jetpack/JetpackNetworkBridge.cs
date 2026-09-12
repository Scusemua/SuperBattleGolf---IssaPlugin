using IssaPlugin.Overlays;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Attached to every player object via NetworkBridgePatches.
    ///
    /// Particle VFX flow (networked — visible to all clients):
    ///   1. Local player holds LMB with jetpack equipped → JetpackItem.FireLoop calls
    ///      ClientNotifyThrustStart().
    ///   2. Client sends JetpackThrustStartMessage to the server.
    ///   3. Server broadcasts JetpackThrustBeginMessage(netId) to all clients.
    ///   4. Each client instantiates the particle prefab locally and parents it to that
    ///      player's transform.
    ///   5. On LMB release or fuel exhaustion, FireLoop calls ClientNotifyThrustStop().
    ///   6. Server broadcasts JetpackThrustEndMessage; all clients destroy their local
    ///      particle instance.
    ///
    /// Equipped-prefab flow (local player only):
    ///   Update() polls GetEffectivelyEquippedItem each frame. Shows the backpack prefab
    ///   while the jetpack is the local player's active item; destroys it otherwise.
    ///   Only runs on the local player's bridge (isLocalPlayer guard); remote bridges
    ///   are a no-op each frame.
    /// </summary>
    public class JetpackNetworkBridge : NetworkBridgeBase
    {
        // ── Server state ──────────────────────────────────────────────────────────
        private bool _serverThrusting;
        private bool _serverEquipped;

        // ── Client state ─────────────────────────────────────────────────────────
        private GameObject _particles;
        private GameObject _equippedPrefabInstance;

        // Local offset of the backpack on the player's back, at character scale 1.
        // Scaled by the live character scale so the pack stays seated on the back when
        // Jumbo Burger grows the player.
        private static readonly Vector3 EquippedLocalOffset = new Vector3(0f, 0.5f, -0.2f);

        private PlayerMovement _cachedMovement;
        private bool _movementLookupDone;

        /// <summary>
        /// The PlayerMovement on this bridge's player object, resolved once.
        /// Used only to read <see cref="PlayerMovement.CharacterScale"/>.
        /// </summary>
        private PlayerMovement CachedMovement
        {
            get
            {
                if (!_movementLookupDone)
                {
                    _cachedMovement = GetComponent<PlayerMovement>();
                    _movementLookupDone = true;
                }
                return _cachedMovement;
            }
        }

        /// <summary>
        /// Current character scale for this player, or 1 if movement is unavailable.
        /// Jumbo Burger scales the player by setting PlayerInfo.BonesParent's local
        /// scale; the jetpack visuals are parented to the player root instead, so they
        /// do not inherit that and must be scaled here.
        /// </summary>
        private float CharacterScale
        {
            get
            {
                var movement = CachedMovement;
                return movement == null ? 1f : movement.CharacterScale;
            }
        }

        /// <summary>
        /// Re-applies scale and back offset to the jetpack visuals. Called every frame
        /// because the giant-form scale is animated over time rather than set once.
        /// </summary>
        private void ApplyCharacterScaleToVisuals()
        {
            float scale = CharacterScale;

            if (_equippedPrefabInstance != null)
            {
                _equippedPrefabInstance.transform.localScale = Vector3.one * scale;
                _equippedPrefabInstance.transform.localPosition = EquippedLocalOffset * scale;
            }

            if (_particles != null)
                _particles.transform.localScale = Vector3.one * scale;
        }

        // ================================================================
        //  Client → Server
        // ================================================================

        public void ClientNotifyThrustStart() =>
            NetworkClient.Send(new JetpackThrustStartMessage());

        public void ClientNotifyThrustStop() => NetworkClient.Send(new JetpackThrustStopMessage());

        // ================================================================
        //  Server handlers — registered in NetworkManagerPatches
        // ================================================================

        public void ServerHandleThrustStart()
        {
            if (!isServer)
                return;

            // Ignore duplicate starts — particles are already showing on all clients.
            if (_serverThrusting)
                return;

            _serverThrusting = true;
            NetworkServer.SendToAll(new JetpackThrustBeginMessage { PlayerNetId = netId });
        }

        public void ServerHandleThrustStop()
        {
            if (!isServer)
                return;

            if (!_serverThrusting)
                return;

            _serverThrusting = false;
            NetworkServer.SendToAll(new JetpackThrustEndMessage { PlayerNetId = netId });
        }

        // ================================================================
        //  Client handlers (static) — registered in NetworkManagerPatches
        // ================================================================

        public static void HandleThrustBegin(JetpackThrustBeginMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                return;
            identity.GetComponent<JetpackNetworkBridge>()?.ClientShowParticles();
        }

        public static void HandleThrustEnd(JetpackThrustEndMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                return;
            identity.GetComponent<JetpackNetworkBridge>()?.ClientHideParticles();
        }

        public static void HandleEquipBegin(JetpackEquipBeginMessage msg)
        {
            // Cache host config so FireLoop uses authoritative values, not local defaults.
            // Skip on the listen-server host; it already has the correct values.
            if (!NetworkServer.active)
            {
                JetpackItem.ServerFuelPerUse = msg.FuelPerUse;
                JetpackItem.ServerThrustForce = msg.ThrustForce;
            }

            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                return;
            var bridge = identity.GetComponent<JetpackNetworkBridge>();
            if (bridge == null)
                return;

            if (bridge.isLocalPlayer)
            {
                // Notify overlay so it shows for the local player on all clients (including host).
                JetpackOverlay.OnLocalEquip();
                return;
            }

            bridge.ClientShowEquippedPrefab();
        }

        public static void HandleEquipEnd(JetpackEquipEndMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                return;
            var bridge = identity.GetComponent<JetpackNetworkBridge>();
            if (bridge == null)
                return;

            if (bridge.isLocalPlayer)
            {
                JetpackOverlay.OnLocalUnequip();
                return;
            }

            bridge.ClientDestroyEquippedPrefab();
        }

        // ================================================================
        //  Per-client particle management
        // ================================================================

        private void ClientShowParticles()
        {
            ClientHideParticles();

            if (AssetLoader.JetpackParticlePrefab == null)
                return;

            _particles = Object.Instantiate(AssetLoader.JetpackParticlePrefab);
            _particles.transform.SetParent(transform, false);
            _particles.transform.localPosition = Vector3.zero;
            _particles.transform.localRotation = Quaternion.identity;
            _particles.transform.localScale = Vector3.one * CharacterScale;
            _particles.SetActive(true);
        }

        private void ClientHideParticles()
        {
            if (_particles == null)
                return;
            Object.Destroy(_particles);
            _particles = null;
        }

        // ================================================================
        //  Equipped-prefab management + server equip-state tracking
        //
        //  OnEquip on the definition is called every frame while the item is active,
        //  but has no corresponding OnUnequip hook — so Update() is the appropriate
        //  place to manage the full show/hide lifecycle.
        //
        //  The server block polls inventory every frame and broadcasts
        //  JetpackEquipBeginMessage / JetpackEquipEndMessage so all clients can show
        //  the backpack prefab on remote players.
        // ================================================================

        private void Update()
        {
            // Runs for every bridge, including remote players on a client, because the
            // backpack is shown on remote players too. Jumbo Burger animates the scale
            // over time rather than setting it once, so it has to be tracked per frame
            // while a visual exists — the null checks make this free otherwise.
            if (_equippedPrefabInstance != null || _particles != null)
                ApplyCharacterScaleToVisuals();

            // Neither block below applies to a remote player's bridge on a client.
            // Without this the host runs the whole method once per player per frame.
            if (!isServer && !isLocalPlayer)
                return;

            if (isServer)
            {
                var inv = CachedInventory;
                bool equipped =
                    inv != null
                    && (
                        ModConfig.Jetpack.UseJumpToActivate.Value
                            ? JetpackItem.FindJetpackSlot(inv) >= 0
                            : inv.GetEffectivelyEquippedItem(true) == ItemRegistry.JetpackItemType
                    );

                if (equipped && !_serverEquipped)
                {
                    _serverEquipped = true;
                    NetworkServer.SendToAll(
                        new JetpackEquipBeginMessage
                        {
                            PlayerNetId = netId,
                            FuelPerUse = ModConfig.Jetpack.FuelPerUse.Value,
                            ThrustForce = ModConfig.Jetpack.ThrustForce.Value,
                        }
                    );
                }
                else if (!equipped && _serverEquipped)
                {
                    _serverEquipped = false;
                    NetworkServer.SendToAll(new JetpackEquipEndMessage { PlayerNetId = netId });
                }
            }

            if (!isLocalPlayer)
                return;

            var inventory = CachedInventory;

            // Show the backpack prefab whenever the jetpack is the active item, or — when
            // UseJumpToActivate is on — whenever it is anywhere in the inventory.
            bool jetpackActive =
                inventory != null
                && (
                    ModConfig.Jetpack.UseJumpToActivate.Value
                        ? JetpackItem.FindJetpackSlot(inventory) >= 0
                        : inventory.GetEffectivelyEquippedItem(true) == ItemRegistry.JetpackItemType
                );

            if (
                jetpackActive
                && _equippedPrefabInstance == null
                && AssetLoader.JetpackEquippedPrefab != null
            )
            {
                _equippedPrefabInstance = Object.Instantiate(AssetLoader.JetpackEquippedPrefab);
                var rb = _equippedPrefabInstance.gameObject.GetComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                _equippedPrefabInstance.transform.SetParent(transform, false);
                _equippedPrefabInstance.transform.localRotation = Quaternion.identity;
                ApplyCharacterScaleToVisuals();
            }
            else if (!jetpackActive && _equippedPrefabInstance != null)
            {
                Object.Destroy(_equippedPrefabInstance);
                _equippedPrefabInstance = null;
            }

            // Jump-trigger: activate the jetpack via Space even when it isn't the equipped item.
            // This path never goes through PlayerInventory.TryUseItem, so it must repeat the
            // tee-off gate that TryUseItemPatch applies to every other custom item — otherwise
            // the jetpack can be flown during the pre-hit countdown while all other movement
            // and item use is locked out.
            if (
                ModConfig.Jetpack.UseJumpToActivate.Value
                && !JetpackItem.IsFlying
                && inventory != null
                && (
                    SingletonBehaviour<DrivingRangeManager>.HasInstance
                    || CourseManager.MatchState > MatchState.TeeOff
                )
                && Keyboard.current?.spaceKey.wasPressedThisFrame == true
            )
            {
                int jetpackSlot = JetpackItem.FindJetpackSlot(inventory);
                if (jetpackSlot >= 0)
                    inventory.StartCoroutine(
                        JetpackItem.FireLoop(inventory, fromJump: true, slotOverride: jetpackSlot)
                    );
            }
        }

        private void ClientShowEquippedPrefab()
        {
            if (_equippedPrefabInstance != null || AssetLoader.JetpackEquippedPrefab == null)
                return;
            _equippedPrefabInstance = Object.Instantiate(AssetLoader.JetpackEquippedPrefab);
            var rb = _equippedPrefabInstance.GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            _equippedPrefabInstance.transform.SetParent(transform, false);
            _equippedPrefabInstance.transform.localRotation = Quaternion.identity;
            ApplyCharacterScaleToVisuals();
        }

        private void ClientDestroyEquippedPrefab()
        {
            if (_equippedPrefabInstance == null)
                return;
            Object.Destroy(_equippedPrefabInstance);
            _equippedPrefabInstance = null;
        }

        // ================================================================
        //  Hole cleanup
        // ================================================================

        public override void ServerHoleCleanup()
        {
            if (_serverThrusting)
            {
                NetworkServer.SendToAll(new JetpackThrustEndMessage { PlayerNetId = netId });
                _serverThrusting = false;
            }

            // Reset so Update() re-broadcasts equip state on the next hole.
            // ClientHoleCleanup always destroys the backpack prefab on clients, so
            // they need a fresh JetpackEquipBeginMessage even if the player kept the jetpack.
            _serverEquipped = false;
        }

        public override void ClientHoleCleanup()
        {
            ClientHideParticles();
            ClientDestroyEquippedPrefab();
            if (isLocalPlayer)
            {
                JetpackItem.ResetFuel();
                JetpackOverlay.OnLocalUnequip();
            }
        }

        public override void OnStopServer()
        {
            _serverThrusting = false;
            _serverEquipped = false;
        }
    }
}
