using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Shared server-side state for the cube-ball effect.
    ///
    /// Both ShapeShifterNetworkBridge (single target) and SuperShapeShifterNetworkBridge
    /// (all other players) call ServerApplyCube here so all duration-extension
    /// logic lives in one place.
    ///
    /// The coroutines that fire ShapeShifterEndMessage are hosted on ShapeShifterManager,
    /// a persistent singleton, so they survive any player disconnect.
    public static class ShapeShifterHelper
    {
        // ── Server-side state ─────────────────────────────────────────────────
        // Key = target player's NetworkIdentity netId.
        private static readonly Dictionary<uint, float> _cubeEndTimes =
            new Dictionary<uint, float>();
        private static readonly Dictionary<uint, Coroutine> _cubeCoroutines =
            new Dictionary<uint, Coroutine>();

        // ── Public API ────────────────────────────────────────────────────────

        /// Applies (or extends) the cube effect on the ball owned by the player
        /// with the given <paramref name="targetNetId"/>.
        ///
        /// If the ball is not already cubed: broadcasts ShapeShifterBeginMessage to
        /// all clients and starts a timeout coroutine.
        /// If it is already cubed: silently extends the end time to the later of
        /// the current end time and the new end time; no second begin message is
        /// sent (the ball is already a cube on all clients).
        public static void ServerApplyCube(uint targetNetId, float duration, bool reroll = false)
        {
            float newEnd = Time.time + duration;

            if (_cubeEndTimes.TryGetValue(targetNetId, out float existingEnd))
            {
                // Already active — extend if the new end is later.
                if (newEnd > existingEnd)
                    _cubeEndTimes[targetNetId] = newEnd;

                float remaining = _cubeEndTimes[targetNetId] - Time.time;
                NetworkServer.SendToAll(
                    new ShapeShifterBeginMessage
                    {
                        TargetNetId = targetNetId,
                        Duration = remaining,
                        Reroll = reroll,
                    }
                );
                return;
            }

            _cubeEndTimes[targetNetId] = newEnd;
            NetworkServer.SendToAll(
                new ShapeShifterBeginMessage
                {
                    TargetNetId = targetNetId,
                    Duration = duration,
                    Reroll = false,
                }
            );

            var manager = ShapeShifterManager.Instance;
            if (manager == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[ShapeShifter] ShapeShifterManager.Instance is null — cannot start timeout coroutine."
                );
                return;
            }

            var co = manager.StartCoroutine(ServerCubeTimeout(targetNetId));
            _cubeCoroutines[targetNetId] = co;

            IssaPluginPlugin.Log.LogInfo(
                $"[ShapeShifter] Cube effect started for netId={targetNetId}, duration={duration:F1}s."
            );
        }

        /// Immediately cancels the cube effect on a specific target (used by
        /// cleanup paths such as hole transitions).
        public static void ServerCancelCube(uint targetNetId)
        {
            if (!_cubeEndTimes.ContainsKey(targetNetId))
                return;

            _cubeEndTimes.Remove(targetNetId);

            if (_cubeCoroutines.TryGetValue(targetNetId, out var co))
            {
                _cubeCoroutines.Remove(targetNetId);
                ShapeShifterManager.Instance?.StopCoroutine(co);
            }

            NetworkServer.SendToAll(new ShapeShifterEndMessage { TargetNetId = targetNetId });
            IssaPluginPlugin.Log.LogInfo(
                $"[ShapeShifter] Cube effect cancelled for netId={targetNetId}."
            );
        }

        /// Cancels all active cube effects at once (hole transitions, server stop).
        public static void ServerCleanupAll()
        {
            if (_cubeEndTimes.Count == 0)
                return;

            var manager = ShapeShifterManager.Instance;

            // Copy keys before iterating to avoid modifying the collection mid-loop.
            var netIds = new List<uint>(_cubeEndTimes.Keys);
            foreach (var netId in netIds)
            {
                if (_cubeCoroutines.TryGetValue(netId, out var co))
                    manager?.StopCoroutine(co);

                NetworkServer.SendToAll(new ShapeShifterEndMessage { TargetNetId = netId });
            }

            _cubeEndTimes.Clear();
            _cubeCoroutines.Clear();

            IssaPluginPlugin.Log.LogInfo("[ShapeShifter] All cube effects cleaned up.");
        }

        // ── Shared client-side handlers (registered in NetworkManagerPatches) ─

        /// Called on every client (including the listen-server) when the server
        /// starts a cube effect.  Finds the target's GolfBall and applies
        /// ShapeShifterState to it.
        public static void HandleShapeShifterBegin(ShapeShifterBeginMessage msg)
        {
            var ball = FindBall(msg.TargetNetId);
            if (ball == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[ShapeShifter] HandleBegin: no GolfBall found for netId={msg.TargetNetId}."
                );
                return;
            }

            var existing = ball.GetComponent<ShapeShifterState>();
            if (existing != null && msg.Reroll)
            {
                // Re-randomize: revert the current shape then apply a new one.
                existing.Revert();
                existing = null;
            }

            if (existing == null)
            {
                var state = ball.gameObject.AddComponent<ShapeShifterState>();
                state.Apply();
                IssaPluginPlugin.Log.LogInfo(
                    $"[ShapeShifter] Shape applied to ball owned by netId={msg.TargetNetId} (shape={state.Shape})."
                );
            }

            // Update the local player's timer bar on every begin (including extensions).
            if (IsLocalPlayerTarget(msg.TargetNetId))
                ShapeShifterOverlay.Instance?.SetCubed(true, msg.Duration);
        }

        /// Called on every client (including the listen-server) when the server
        /// ends a cube effect.  Reverts ShapeShifterState on the target's ball.
        public static void HandleShapeShifterEnd(ShapeShifterEndMessage msg)
        {
            var ball = FindBall(msg.TargetNetId);
            var state = ball?.GetComponent<ShapeShifterState>();
            state?.Revert();

            if (IsLocalPlayerTarget(msg.TargetNetId))
                ShapeShifterOverlay.Instance?.SetCubed(false);

            if (state != null)
                IssaPluginPlugin.Log.LogInfo(
                    $"[ShapeShifter] Cube reverted for ball owned by netId={msg.TargetNetId}."
                );
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private static IEnumerator ServerCubeTimeout(uint targetNetId)
        {
            while (_cubeEndTimes.TryGetValue(targetNetId, out float endTime) && Time.time < endTime)
            {
                float remaining = endTime - Time.time;
                yield return new WaitForSeconds(remaining);
                // Loop again: endTime may have been extended while we waited.
            }

            // Effect has expired (or was cleaned up externally).
            _cubeEndTimes.Remove(targetNetId);
            _cubeCoroutines.Remove(targetNetId);
            if (NetworkServer.active)
                NetworkServer.SendToAll(new ShapeShifterEndMessage { TargetNetId = targetNetId });

            IssaPluginPlugin.Log.LogInfo(
                $"[ShapeShifter] Cube effect timed out for netId={targetNetId}."
            );
        }

        /// Returns the GolfBall owned by the player whose NetworkIdentity has
        /// the given <paramref name="netId"/>.  Works on both server and clients
        /// (uses the appropriate spawned dictionary).
        private static GolfBall FindBall(uint netId)
        {
            var dict = NetworkServer.active ? NetworkServer.spawned : NetworkClient.spawned;

            if (!dict.TryGetValue(netId, out var identity))
                return null;

            return identity.GetComponent<PlayerInventory>()?.PlayerInfo?.AsGolfer?.OwnBall;
        }

        private static bool IsLocalPlayerTarget(uint targetNetId) =>
            NetworkClient.localPlayer != null && NetworkClient.localPlayer.netId == targetNetId;
    }
}
