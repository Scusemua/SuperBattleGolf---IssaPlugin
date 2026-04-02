using System.Collections.Generic;
using System.Linq;
using IssaPlugin.Items;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Network
{
    /// Manages the custom-item enable/disable vote lifecycle.
    /// Runs on the Plugin GameObject on every connected client (including the host).
    /// All registration of Mirror message handlers for this class is done in
    /// NetworkManagerPatches.NetworkManagerRegisterPrefabsPatch so that handlers
    /// are registered on the correct NetworkServer / NetworkClient objects.
    public class VoteManager : MonoBehaviour
    {
        public static VoteManager Instance { get; private set; }

        // ── Server state ────────────────────────────────────────────────────────
        private bool _voteInProgress;
        private float _voteEndTime;

        // connectionId → (itemType int → voted enabled)
        private readonly Dictionary<int, Dictionary<int, bool>> _receivedVotes = new();
        private readonly HashSet<int> _pendingConnections = new();

        // ── Shared state (read by VoteOverlay on all clients) ───────────────────
        public static bool VoteActive { get; private set; }
        public static float VoteTimeRemaining { get; private set; }
        public static bool LocalVoteSubmitted { get; private set; }

        // Current selections shown in the overlay (itemType int → enabled)
        public static readonly Dictionary<int, bool> LocalVotes = new();

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            if (VoteActive)
                VoteTimeRemaining = Mathf.Max(0f, _voteEndTime - Time.time);

            if (NetworkServer.active && _voteInProgress && Time.time >= _voteEndTime)
                TallyAndBroadcast();
        }

        // ── Host API ─────────────────────────────────────────────────────────────

        /// Called by the /vote console command. Only works on the host.
        public void StartVote(float timeout)
        {
            if (!NetworkServer.active)
            {
                IssaPluginPlugin.Log.LogWarning("[Vote] Only the host can start a vote.");
                return;
            }
            if (_voteInProgress)
            {
                IssaPluginPlugin.Log.LogWarning("[Vote] A vote is already in progress.");
                return;
            }

            _voteInProgress = true;
            _voteEndTime = Time.time + timeout;
            _receivedVotes.Clear();
            _pendingConnections.Clear();

            foreach (var conn in NetworkServer.connections.Values)
                _pendingConnections.Add(conn.connectionId);

            IssaPluginPlugin.Log.LogInfo(
                $"[Vote] Starting with {timeout}s timeout, {_pendingConnections.Count} player(s)."
            );

            NetworkServer.SendToAll(new VoteStartMessage { TimeoutSeconds = timeout });
        }

        // ── Server-side handlers ─────────────────────────────────────────────────

        internal void HandleVoteSubmit(NetworkConnectionToClient conn, VoteSubmitMessage msg)
        {
            if (!_voteInProgress)
                return;

            int connId = conn.connectionId;
            _receivedVotes[connId] = msg.Votes;
            _pendingConnections.Remove(connId);

            IssaPluginPlugin.Log.LogInfo(
                $"[Vote] Vote from connection {connId} received. Pending: {_pendingConnections.Count}"
            );

            if (_pendingConnections.Count == 0)
                TallyAndBroadcast();
        }

        private void TallyAndBroadcast()
        {
            if (!_voteInProgress)
                return;
            _voteInProgress = false;

            var results = VoteTally.Compute(
                _receivedVotes,
                ItemRegistry.AllItems.Select(i => (int)i.ItemType),
                key => ItemRegistry.GetDefinition((ItemType)key)?.Enabled ?? false
            );

            ApplyResults(results);

            // Rebuild server item-box pools now that Enabled states have changed.
            // SpawnWeightsSyncer won't do this automatically unless a weight value also changed.
            SpawnWeightsSyncer.ForceServerSync();

            // Close the host's overlay immediately. The listen-server host runs this method
            // directly (not via a network message), so we can't rely on HandleVoteResults
            // being called on the host's NetworkClient in time.
            VoteActive = false;
            LocalVoteSubmitted = false;

            NetworkServer.SendToAll(new VoteResultsMessage { Results = results });
            IssaPluginPlugin.Log.LogInfo("[Vote] Tallied and broadcast results.");
        }

        // ── Client-side handlers (called from NetworkManagerPatches) ─────────────

        internal static void HandleVoteStart(VoteStartMessage msg)
        {
            // Seed the overlay selections from each client's current local enabled state
            LocalVotes.Clear();
            foreach (var item in ItemRegistry.AllItems)
                LocalVotes[(int)item.ItemType] = item.Enabled;

            LocalVoteSubmitted = false;
            VoteActive = true;

            // On a listen-server the host already set _voteEndTime in StartVote; only
            // update it here on pure clients so their countdown display is accurate.
            if (Instance != null && !NetworkServer.active)
                Instance._voteEndTime = Time.time + msg.TimeoutSeconds;

            IssaPluginPlugin.Log.LogInfo($"[Vote] Vote started! Timeout: {msg.TimeoutSeconds}s");
        }

        internal static void HandleVoteResults(VoteResultsMessage msg)
        {
            VoteActive = false;
            LocalVoteSubmitted = false;

            // Host already applied results in TallyAndBroadcast; clients apply here
            if (!NetworkServer.active)
                ApplyResults(msg.Results);

            IssaPluginPlugin.Log.LogInfo("[Vote] Results received and applied.");
        }

        private static void ApplyResults(Dictionary<int, bool> results)
        {
            foreach (var (key, enabled) in results)
            {
                ModConfig.SetItemEnabled((ItemType)key, enabled);
                IssaPluginPlugin.Log.LogInfo($"[Vote] Item {key} → enabled={enabled}");
            }
        }

        // ── VoteOverlay API ──────────────────────────────────────────────────────

        /// Called by VoteOverlay when the local player clicks Submit.
        public static void SubmitLocalVotes()
        {
            if (!VoteActive || LocalVoteSubmitted)
                return;
            LocalVoteSubmitted = true;
            NetworkClient.Send(
                new VoteSubmitMessage { Votes = new Dictionary<int, bool>(LocalVotes) }
            );
        }
    }
}
