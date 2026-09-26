using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IssaPlugin.Items
{
    /// Draws the rope for one player. The swing itself is simulated only on the
    /// owning client; everyone else just tracks the anchor.
    public class GrapplingHookNetworkBridge : NetworkBridgeBase
    {
        private struct ServerGrapple
        {
            public Vector3 Anchor;
            public int Token;
        }

        private static Material _ropeMaterial;
        private static bool _loggedMissingShader;

        // Server-only. Late joiners never saw the original broadcast, so the
        // anchor has to be remembered and sent when their player object starts.
        private static readonly Dictionary<uint, ServerGrapple> ServerGrapples = new();

        // Client-only. A catch-up message can arrive before that player exists locally.
        private static readonly Dictionary<uint, GrappleAnchorMessage> PendingAnchors = new();

        // Same value on every peer. Hole changes and the results screen bump it so a
        // message still in flight from the previous hole is ignored. A late joiner
        // adopts whatever generation the server is already on.
        private static int _generation = 1;
        private static int _lastClosedGeneration = 1;
        private static readonly List<uint> StalePending = new();

        // isLocalPlayer is already false by the time the object is torn down.
        private bool _wasOwned;

        private LineRenderer _line;
        private Vector3 _anchor;
        private int _shownToken;
        private int _shownGeneration;
        private bool _showing;
        private PlayerMovement _movement;

        public static void ShowLocalRope(Vector3 anchor) =>
            ShowLocalRope(anchor, GrapplingHookSession.Token);

        // Token is the shot the server still has. A rejected re-anchor restores
        // the previous one, which is no longer GrapplingHookSession.Token.
        internal static void ShowLocalRope(Vector3 anchor, int token)
        {
            LocalBridge()?.ShowRope(anchor, token);
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

        /// Results screen. The last hole does not pass through HoleOverview.
        public static void EndMatch()
        {
            AdvanceGeneration();
            if (!NetworkServer.active)
                return;

            var bridges = FindObjectsByType<GrapplingHookNetworkBridge>(FindObjectsSortMode.None);
            for (int i = 0; i < bridges.Length; i++)
                bridges[i].ServerHoleCleanup();
        }

        public static void AdvanceGeneration()
        {
            // A newer anchor can already have been applied if this peer learned
            // about the hole change late. Closing again would skip a generation
            // and ignore the rest of the hole.
            if (_generation == _lastClosedGeneration)
                _generation++;

            _lastClosedGeneration = _generation;
            DropStaleRopes();
        }

        public override void OnStartServer()
        {
            if (connectionToClient == null || ServerGrapples.Count == 0)
                return;

            foreach (var pair in ServerGrapples)
            {
                connectionToClient.Send(
                    new GrappleAnchorMessage
                    {
                        PlayerNetId = pair.Key,
                        Anchor = pair.Value.Anchor,
                        Token = pair.Value.Token,
                        Generation = _generation,
                    }
                );
            }
        }

        public override void OnStartClient()
        {
            _wasOwned = isOwned;
            if (!PendingAnchors.TryGetValue(netId, out var msg))
                return;

            PendingAnchors.Remove(netId);
            ApplyAnchor(msg);
        }

        public override void OnStopServer()
        {
            // Hole changes keep the player object. This is a real disconnect.
            Forget(broadcast: true);
        }

        public override void OnStopClient() => StopOwnedSession();

        private void OnDestroy() => StopOwnedSession();

        private void StopOwnedSession()
        {
            if (!_wasOwned && !isOwned)
                return;

            // The session is static and would otherwise swing the next life.
            _wasOwned = false;
            PendingAnchors.Remove(netId);
            GrapplingHookSession.ForceStop(false);
        }

        public void ServerHandleFire(Vector3 anchor, int slotIndex, int token)
        {
            if (!isServer)
                return;

            // A click from the previous hole can still be in the queue. Accepting
            // it would spend a use and leave other clients with a rope the owner
            // has already dropped.
            if (
                !SingletonBehaviour<DrivingRangeManager>.HasInstance
                && (
                    CourseManager.MatchState <= MatchState.TeeOff
                    || CourseManager.MatchState == MatchState.Ended
                )
            )
            {
                Reject(token);
                return;
            }

            var inventory = CachedInventory;
            if (
                inventory == null
                || ItemRegistry.GetItemTypeAtSlot(inventory, slotIndex)
                    != ItemRegistry.GrapplingHookItemType
                || ItemRegistry.GetRemainingUsesAtSlot(inventory, slotIndex) <= 0
                || !IsFinite(anchor)
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
            ServerGrapples[netId] = new ServerGrapple { Anchor = anchor, Token = token };
            NetworkServer.SendToAll(
                new GrappleAnchorMessage
                {
                    PlayerNetId = netId,
                    Anchor = anchor,
                    Token = token,
                    Generation = _generation,
                }
            );
        }

        public void ServerHandleRelease()
        {
            if (!isServer)
                return;

            Forget(broadcast: true);
        }

        public static void HandleAnchor(GrappleAnchorMessage msg)
        {
            if (!IsFinite(msg.Anchor) || !AcceptGeneration(msg.Generation, out bool raised))
                return;

            if (!NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                PendingAnchors[msg.PlayerNetId] = msg;
            else
            {
                PendingAnchors.Remove(msg.PlayerNetId);
                identity.GetComponent<GrapplingHookNetworkBridge>()?.ApplyAnchor(msg);
            }

            // Adopted a generation this peer had not closed itself. Older lines
            // would otherwise stay up until the next hole event.
            if (raised)
                DropStaleRopes();
        }

        public static void HandleReject(GrappleRejectMessage msg)
        {
            GrapplingHookSession.Reject(msg.Token);
        }

        public static void HandleClear(GrappleClearMessage msg)
        {
            if (!AcceptGeneration(msg.Generation, out bool raised))
                return;

            if (
                PendingAnchors.TryGetValue(msg.PlayerNetId, out var pending)
                && pending.Token == msg.Token
            )
                PendingAnchors.Remove(msg.PlayerNetId);

            if (NetworkClient.spawned.TryGetValue(msg.PlayerNetId, out var identity))
                identity.GetComponent<GrapplingHookNetworkBridge>()?.HideRope(msg.Token);

            if (raised)
                DropStaleRopes();
        }

        public override void ServerHoleCleanup()
        {
            // Each client tears its own ropes down. A broadcast here can land on
            // the next hole and hide a rope that was just fired.
            Forget(broadcast: false);
        }

        public override void ClientHoleCleanup()
        {
            // AdvanceGeneration already dropped ropes from the closed hole.
        }

        // The last use removes the item, and the aim marker with it. Reel and
        // release stay here so the rope can still be let go.
        private void Update()
        {
            if (!isLocalPlayer || !GrapplingHookSession.IsAttached)
                return;

            var keyboard = Keyboard.current;
            GrapplingHookSession.IsReeling = keyboard != null && keyboard.rKey.isPressed;

            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
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

        private void ApplyAnchor(GrappleAnchorMessage msg)
        {
            // The owner already drew the rope when they fired. An anchor message
            // that arrives after they let go, or for a shot they have since
            // replaced, must not put that rope back.
            if (isLocalPlayer)
            {
                if (!GrapplingHookSession.IsAttached || msg.Token != GrapplingHookSession.Token)
                    return;

                GrapplingHookSession.Confirm(msg.Token);
            }

            ShowRope(msg.Anchor, msg.Token);
        }

        private void ShowRope(Vector3 anchor, int token)
        {
            _anchor = anchor;
            _shownToken = token;
            _shownGeneration = _generation;
            _showing = true;
            if (_line == null)
                _line = CreateLine();
            if (_line != null)
                _line.enabled = true;
        }

        private void HideRope()
        {
            _showing = false;
            _shownToken = 0;
            _shownGeneration = 0;
            if (_line != null)
                _line.enabled = false;
        }

        private void HideRope(int token)
        {
            if (_shownToken != token)
                return;

            HideRope();
        }

        private void Forget(bool broadcast)
        {
            if (!ServerGrapples.TryGetValue(netId, out var state))
                return;

            int token = state.Token;
            ServerGrapples.Remove(netId);

            if (!broadcast)
                return;

            NetworkServer.SendToAll(
                new GrappleClearMessage
                {
                    PlayerNetId = netId,
                    Token = token,
                    Generation = _generation,
                }
            );
        }

        private static bool AcceptGeneration(int generation, out bool raised)
        {
            raised = false;
            if (generation < _generation)
                return false;

            // A player who joined mid-match has not seen the earlier hole changes.
            if (generation > _generation)
            {
                _generation = generation;
                raised = true;
            }

            return true;
        }

        private static void DropStaleRopes()
        {
            StalePending.Clear();
            foreach (var pair in PendingAnchors)
            {
                if (pair.Value.Generation < _generation)
                    StalePending.Add(pair.Key);
            }

            for (int i = 0; i < StalePending.Count; i++)
                PendingAnchors.Remove(StalePending[i]);

            bool stopLocal = false;
            var bridges = FindObjectsByType<GrapplingHookNetworkBridge>(FindObjectsSortMode.None);
            for (int i = 0; i < bridges.Length; i++)
            {
                var bridge = bridges[i];
                if (bridge._showing && bridge._shownGeneration >= _generation)
                    continue;

                bridge.HideRope();
                if (bridge.isLocalPlayer || bridge._wasOwned)
                    stopLocal = true;
            }

            if (stopLocal)
                GrapplingHookSession.ForceStop(false);
        }

        private void Reject(int token)
        {
            var msg = new GrappleRejectMessage { Token = token };
            if (isLocalPlayer)
                HandleReject(msg);
            else if (connectionToClient != null)
                connectionToClient.Send(msg);
        }

        private static bool IsFinite(Vector3 v) =>
            float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

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
