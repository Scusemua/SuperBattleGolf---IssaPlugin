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
            // Local player manages its own prefab via Update(); skip to avoid duplicates.
            if (bridge == null || bridge.isLocalPlayer)
                return;
            bridge.ClientShowEquippedPrefab();
        }

        public static void HandleEquipEnd(JetpackEquipEndMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                return;
            var bridge = identity.GetComponent<JetpackNetworkBridge>();
            if (bridge == null || bridge.isLocalPlayer)
                return;
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
        //  Equipped-prefab management (local player only)
        //
        //  OnEquip on the definition is called every frame while the item is active,
        //  but has no corresponding OnUnequip hook — so Update() is the appropriate
        //  place to manage the full show/hide lifecycle.
        // ================================================================

        // ================================================================
        //  Server equip-state tracking — broadcasts backpack visibility to all clients
        // ================================================================

        private void Update()
        {
            if (isServer)
            {
                var inv = GetComponent<PlayerInventory>();
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
                    NetworkServer.SendToAll(new JetpackEquipBeginMessage
                    {
                        PlayerNetId = netId,
                        FuelPerUse = ModConfig.Jetpack.FuelPerUse.Value,
                        ThrustForce = ModConfig.Jetpack.ThrustForce.Value,
                    });
                }
                else if (!equipped && _serverEquipped)
                {
                    _serverEquipped = false;
                    NetworkServer.SendToAll(new JetpackEquipEndMessage { PlayerNetId = netId });
                }
            }

            if (!isLocalPlayer)
                return;

            var inventory = GetComponent<PlayerInventory>();

            // Show the backpack prefab whenever the jetpack is the active item, or — when
            // UseJumpToActivate is on — whenever it is anywhere in the inventory.
            bool jetpackActive =
                inventory != null
                && (
                    ModConfig.Jetpack.UseJumpToActivate.Value
                        ? JetpackItem.FindJetpackSlot(inventory) >= 0
                        : inventory.GetEffectivelyEquippedItem(true)
                            == ItemRegistry.JetpackItemType
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
                _equippedPrefabInstance.transform.localPosition = new Vector3(0f, 0.5f, -0.2f);
                _equippedPrefabInstance.transform.localRotation = Quaternion.identity;
            }
            else if (!jetpackActive && _equippedPrefabInstance != null)
            {
                Object.Destroy(_equippedPrefabInstance);
                _equippedPrefabInstance = null;
            }

            // Jump-trigger: activate the jetpack via Space even when it isn't the equipped item.
            if (
                ModConfig.Jetpack.UseJumpToActivate.Value
                && !JetpackItem.IsFlying
                && inventory != null
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
            _equippedPrefabInstance.transform.localPosition = new Vector3(0f, 0.5f, -0.2f);
            _equippedPrefabInstance.transform.localRotation = Quaternion.identity;
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
            if (!_serverThrusting)
                return;
            NetworkServer.SendToAll(new JetpackThrustEndMessage { PlayerNetId = netId });
            _serverThrusting = false;
        }

        public override void ClientHoleCleanup()
        {
            ClientHideParticles();
            ClientDestroyEquippedPrefab();
            if (isLocalPlayer)
                JetpackItem.ResetFuel();
        }

        public override void OnStopServer()
        {
            _serverThrusting = false;
        }
    }
}
