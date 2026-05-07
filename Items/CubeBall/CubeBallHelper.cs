using System.Collections;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// Shared server-side state for the cube-ball effect.
    ///
    /// Both CubeBallNetworkBridge (single target) and SuperCubeBallNetworkBridge
    /// (all other players) call ServerApplyCube here so all duration-extension
    /// logic lives in one place.
    ///
    /// The coroutines that fire CubeBallEndMessage are hosted on CubeBallManager,
    /// a persistent singleton, so they survive any player disconnect.
    public static class CubeBallHelper
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
        /// If the ball is not already cubed: broadcasts CubeBallBeginMessage to
        /// all clients and starts a timeout coroutine.
        /// If it is already cubed: silently extends the end time to the later of
        /// the current end time and the new end time; no second begin message is
        /// sent (the ball is already a cube on all clients).
        public static void ServerApplyCube(uint targetNetId, float duration)
        {
            float newEnd = Time.realtimeSinceStartup + duration;

            if (_cubeEndTimes.TryGetValue(targetNetId, out float existingEnd))
            {
                // Already cubed — just extend if the new end is later.
                if (newEnd > existingEnd)
                    _cubeEndTimes[targetNetId] = newEnd;
                return;
            }

            _cubeEndTimes[targetNetId] = newEnd;
            NetworkServer.SendToAll(new CubeBallBeginMessage { TargetNetId = targetNetId });

            var manager = CubeBallManager.Instance;
            if (manager == null)
            {
                IssaPluginPlugin.Log.LogError(
                    "[CubeBall] CubeBallManager.Instance is null — cannot start timeout coroutine."
                );
                return;
            }

            var co = manager.StartCoroutine(ServerCubeTimeout(targetNetId));
            _cubeCoroutines[targetNetId] = co;

            IssaPluginPlugin.Log.LogInfo(
                $"[CubeBall] Cube effect started for netId={targetNetId}, duration={duration:F1}s."
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
                CubeBallManager.Instance?.StopCoroutine(co);
            }

            NetworkServer.SendToAll(new CubeBallEndMessage { TargetNetId = targetNetId });
            IssaPluginPlugin.Log.LogInfo(
                $"[CubeBall] Cube effect cancelled for netId={targetNetId}."
            );
        }

        /// Cancels all active cube effects at once (hole transitions, server stop).
        public static void ServerCleanupAll()
        {
            if (_cubeEndTimes.Count == 0)
                return;

            var manager = CubeBallManager.Instance;

            // Copy keys before iterating to avoid modifying the collection mid-loop.
            var netIds = new List<uint>(_cubeEndTimes.Keys);
            foreach (var netId in netIds)
            {
                if (_cubeCoroutines.TryGetValue(netId, out var co))
                    manager?.StopCoroutine(co);

                NetworkServer.SendToAll(new CubeBallEndMessage { TargetNetId = netId });
            }

            _cubeEndTimes.Clear();
            _cubeCoroutines.Clear();

            IssaPluginPlugin.Log.LogInfo("[CubeBall] All cube effects cleaned up.");
        }

        // ── Shared client-side handlers (registered in NetworkManagerPatches) ─

        /// Called on every client (including the listen-server) when the server
        /// starts a cube effect.  Finds the target's GolfBall and applies
        /// CubeBallState to it.
        public static void HandleCubeBallBegin(CubeBallBeginMessage msg)
        {
            var ball = FindBall(msg.TargetNetId);
            if (ball == null)
            {
                IssaPluginPlugin.Log.LogWarning(
                    $"[CubeBall] HandleBegin: no GolfBall found for netId={msg.TargetNetId}."
                );
                return;
            }

            if (ball.GetComponent<CubeBallState>() != null)
                return; // Already cubed — server extended the timer; no visual change needed.

            var state = ball.gameObject.AddComponent<CubeBallState>();
            state.Apply();

            IssaPluginPlugin.Log.LogInfo(
                $"[CubeBall] Cube applied to ball owned by netId={msg.TargetNetId}."
            );
        }

        /// Called on every client (including the listen-server) when the server
        /// ends a cube effect.  Reverts CubeBallState on the target's ball.
        public static void HandleCubeBallEnd(CubeBallEndMessage msg)
        {
            var ball = FindBall(msg.TargetNetId);
            if (ball == null)
                return;

            ball.GetComponent<CubeBallState>()?.Revert();

            IssaPluginPlugin.Log.LogInfo(
                $"[CubeBall] Cube reverted for ball owned by netId={msg.TargetNetId}."
            );
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private static IEnumerator ServerCubeTimeout(uint targetNetId)
        {
            while (
                _cubeEndTimes.TryGetValue(targetNetId, out float endTime)
                && Time.realtimeSinceStartup < endTime
            )
            {
                float remaining = endTime - Time.realtimeSinceStartup;
                yield return new WaitForSeconds(remaining);
                // Loop again: endTime may have been extended while we waited.
            }

            // Effect has expired (or was cleaned up externally).
            _cubeEndTimes.Remove(targetNetId);
            _cubeCoroutines.Remove(targetNetId);
            NetworkServer.SendToAll(new CubeBallEndMessage { TargetNetId = targetNetId });

            IssaPluginPlugin.Log.LogInfo(
                $"[CubeBall] Cube effect timed out for netId={targetNetId}."
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
    }
}
