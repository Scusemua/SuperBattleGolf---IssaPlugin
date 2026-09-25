using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// Draws the rope for one player. The swing itself is simulated only on the
    /// owning client; everyone else just tracks the anchor.
    public class GrapplingHookNetworkBridge : NetworkBridgeBase
    {
        private static Material _ropeMaterial;

        private bool _serverAttached;
        private LineRenderer _line;
        private Vector3 _anchor;
        private bool _showing;
        private PlayerMovement _movement;
        private static bool _loggedMissingShader;

        public static void ShowLocalRope(Vector3 anchor)
        {
            LocalBridge()?.ShowRope(anchor);
        }

        public static void HideLocalRope()
        {
            LocalBridge()?.HideRope();
        }

        public static void ReleaseLocalRope()
        {
            var bridge = LocalBridge();
            if (bridge == null)
                return;

            bridge.HideRope();
            if (NetworkClient.active)
                NetworkClient.Send(new GrappleReleaseMessage());
        }

        public static void SendFire(Vector3 anchor, int slotIndex, int token)
        {
            if (!NetworkClient.active)
                return;

            NetworkClient.Send(
                new GrappleFireMessage
                {
                    Anchor = anchor,
                    SlotIndex = slotIndex,
                    Token = token,
                }
            );
        }

        public void ServerHandleFire(Vector3 anchor, int slotIndex, int token)
        {
            if (!isServer)
                return;

            var inventory = CachedInventory;
            if (
                inventory == null
                || ItemRegistry.GetItemTypeAtSlot(inventory, slotIndex)
                    != ItemRegistry.GrapplingHookItemType
                || ItemRegistry.GetRemainingUsesAtSlot(inventory, slotIndex) <= 0
            )
            {
                Reject(token);
                return;
            }

            // The server's copy of the player lags the client that picked the
            // anchor, so the slack has to cover a fast swing, not just error.
            float maxRange = Mathf.Max(1f, ModConfig.GrapplingHook.MaxRange.Value) + 20f;
            if ((anchor - transform.position).sqrMagnitude > maxRange * maxRange)
            {
                Reject(token);
                return;
            }

            ItemHelper.ConsumeItemAtSlot(inventory, slotIndex);
            _serverAttached = true;
            NetworkServer.SendToAll(
                new GrappleAnchorMessage
                {
                    PlayerNetId = netId,
                    Anchor = anchor,
                    Token = token,
                }
            );
        }

        public void ServerHandleRelease()
        {
            if (!isServer || !_serverAttached)
                return;

            _serverAttached = false;
            NetworkServer.SendToAll(new GrappleClearMessage { PlayerNetId = netId });
        }

        public static void HandleAnchor(GrappleAnchorMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                return;

            var bridge = identity.GetComponent<GrapplingHookNetworkBridge>();
            if (bridge == null)
                return;

            bridge.ShowRope(msg.Anchor);
            if (bridge.isLocalPlayer)
                GrapplingHookSession.Confirm(msg.Token);
        }

        public static void HandleReject(GrappleRejectMessage msg)
        {
            GrapplingHookSession.Reject(msg.Token);
        }

        public static void HandleClear(GrappleClearMessage msg)
        {
            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                return;

            identity.GetComponent<GrapplingHookNetworkBridge>()?.HideRope();
        }

        public override void ServerHoleCleanup()
        {
            if (!_serverAttached)
                return;

            _serverAttached = false;
            NetworkServer.SendToAll(new GrappleClearMessage { PlayerNetId = netId });
        }

        public override void ClientHoleCleanup()
        {
            HideRope();
            if (isLocalPlayer)
                GrapplingHookSession.ForceStop(false);
        }

        private void Update()
        {
            if (!isLocalPlayer || !GrapplingHookSession.IsAttached)
                return;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
                GrapplingHookSession.Release();
        }

        private void LateUpdate()
        {
            if (!_showing || _line == null)
                return;

            float scale = Movement != null ? Movement.CharacterScale : 1f;
            Vector3 start = transform.position + Vector3.up * (1.2f * scale);
            _line.SetPosition(0, start);
            _line.SetPosition(1, _anchor);
        }

        private void ShowRope(Vector3 anchor)
        {
            _anchor = anchor;
            _showing = true;
            if (_line == null)
                _line = CreateLine();
            if (_line != null)
                _line.enabled = true;
        }

        private void HideRope()
        {
            _showing = false;
            if (_line != null)
                _line.enabled = false;
        }

        private void Reject(int token)
        {
            var msg = new GrappleRejectMessage { Token = token };
            if (isLocalPlayer)
                HandleReject(msg);
            else if (connectionToClient != null)
                connectionToClient.Send(msg);
        }

        private LineRenderer CreateLine()
        {
            var material = RopeMaterial;
            if (material == null)
            {
                if (!_loggedMissingShader)
                {
                    _loggedMissingShader = true;
                    IssaPluginPlugin.Log.LogWarning(
                        "[Grapple] No line shader — rope will be invisible."
                    );
                }
                return null;
            }

            var go = new GameObject("GrappleRope");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = 0.045f;
            line.endWidth = 0.045f;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.material = material;
            line.startColor = new Color(0.85f, 0.92f, 1f, 1f);
            line.endColor = new Color(0.85f, 0.92f, 1f, 1f);
            return line;
        }

        private static Material RopeMaterial
        {
            get
            {
                if (_ropeMaterial != null)
                    return _ropeMaterial;

                Shader shader =
                    Shader.Find("Sprites/Default")
                    ?? Shader.Find("UI/Default")
                    ?? Shader.Find("Unlit/Color");
                if (shader == null)
                    return null;

                _ropeMaterial = new Material(shader);
                return _ropeMaterial;
            }
        }

        private PlayerMovement Movement
        {
            get
            {
                if (_movement == null)
                    _movement = GetComponent<PlayerMovement>();
                return _movement;
            }
        }

        private static GrapplingHookNetworkBridge LocalBridge()
        {
            return GameManager.LocalPlayerInventory?.GetComponent<GrapplingHookNetworkBridge>();
        }
    }
}
