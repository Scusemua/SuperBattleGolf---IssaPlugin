using System.Collections.Generic;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Manages target selection for a single bear with hysteresis to prevent
    /// ping-ponging between nearby players.
    ///
    /// Rules:
    ///   1. Once a target is locked, the bear keeps it for at least LockDuration seconds.
    ///   2. After LockDuration, the bear only switches if a different player is at least
    ///      StealDistanceThreshold units closer than the current target.
    ///   3. The current target is dropped immediately if they move beyond AbandonDistance.
    ///   4. The current target is dropped immediately if they are dead / null.
    ///   5. If the bear was attacked by a specific player, that player gets elevated
    ///      priority (aggro) and can steal the lock below the normal steal threshold.
    ///
    /// This class is server-only — it runs inside BearBehaviour.FixedUpdate.
    /// </summary>
    public class BearTargetSelector
    {
        // ── Tuning constants ──────────────────────────────────────────────────

        /// Minimum seconds between target re-evaluations after a lock is established.
        private const float LockDuration = 8f;

        /// A new candidate must be this many units closer than the current target
        /// to steal the lock when the timer has expired.
        private const float StealDistanceThreshold = 12f;

        /// If the locked target moves beyond this distance the lock is dropped immediately,
        /// regardless of how much time remains on the timer.
        private const float AbandonDistance = 65f;

        /// An aggro target (one that hit the bear) can steal the lock if they are at
        /// least this many units closer — a lower bar than the normal steal threshold.
        private const float AggroStealThreshold = 4f;

        /// How long aggro on a specific player lasts after they hit the bear.
        private const float AggroDuration = 15f;

        // ── State ─────────────────────────────────────────────────────────────

        private PlayerInfo _lockedTarget;
        private float      _lockTimer;

        private PlayerInfo _aggroTarget;
        private float      _aggroTimer;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Call from BearBehaviour.FixedUpdate (server only).
        /// Returns the current best target, updating internal state as needed.
        /// </summary>
        public PlayerInfo SelectTarget(Vector3 bearPos, List<PlayerInfo> players)
        {
            if (players == null || players.Count == 0)
            {
                _lockedTarget = null;
                return null;
            }

            // Tick timers
            _lockTimer  -= Time.fixedDeltaTime;
            _aggroTimer -= Time.fixedDeltaTime;

            if (_aggroTimer <= 0f)
                _aggroTarget = null;

            // Validate current lock
            if (_lockedTarget != null)
            {
                if (!IsValidTarget(_lockedTarget))
                {
                    _lockedTarget = null;
                }
                else
                {
                    float distToLocked = Vector3.Distance(bearPos, _lockedTarget.transform.position);

                    // Drop if target ran too far away
                    if (distToLocked > AbandonDistance)
                    {
                        _lockedTarget = null;
                    }
                    // Possibly switch targets if timer has elapsed
                    else if (_lockTimer <= 0f)
                    {
                        TryStealLock(bearPos, players, distToLocked);
                        _lockTimer = LockDuration;
                    }
                }
            }

            // Pick a fresh target if we have none
            if (_lockedTarget == null)
            {
                _lockedTarget = GetNearest(bearPos, players);
                _lockTimer    = LockDuration;
            }

            return _lockedTarget;
        }

        /// <summary>
        /// Notify the selector that a specific player hit this bear.
        /// That player becomes the aggro target and gets priority for stealing
        /// the lock (subject to a reduced distance threshold).
        /// </summary>
        public void NotifyHitBy(PlayerInfo attacker)
        {
            if (attacker == null) return;
            _aggroTarget = attacker;
            _aggroTimer  = AggroDuration;
        }

        /// <summary>
        /// Forcibly clear the current lock. Used when the bear enters states where
        /// it should re-evaluate freely (e.g. after coming out of a long stun).
        /// </summary>
        public void ClearLock()
        {
            _lockedTarget = null;
            _lockTimer    = 0f;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private void TryStealLock(Vector3 bearPos, List<PlayerInfo> players, float distToLocked)
        {
            // Check aggro target first (lower steal bar)
            if (_aggroTarget != null
                && _aggroTarget != _lockedTarget
                && IsValidTarget(_aggroTarget))
            {
                float distToAggro = Vector3.Distance(bearPos, _aggroTarget.transform.position);
                if (distToLocked - distToAggro > AggroStealThreshold)
                {
                    _lockedTarget = _aggroTarget;
                    return;
                }
            }

            // Then check all players for the normal steal threshold
            PlayerInfo nearest     = GetNearest(bearPos, players);
            float      distNearest = Vector3.Distance(bearPos, nearest.transform.position);

            if (nearest != _lockedTarget && distToLocked - distNearest > StealDistanceThreshold)
                _lockedTarget = nearest;
        }

        private static PlayerInfo GetNearest(Vector3 bearPos, List<PlayerInfo> players)
        {
            PlayerInfo best     = null;
            float      bestDist = float.MaxValue;

            foreach (var p in players)
            {
                if (!IsValidTarget(p)) continue;
                float d = Vector3.Distance(bearPos, p.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best     = p;
                }
            }

            return best;
        }

        private static bool IsValidTarget(PlayerInfo p)
        {
            // Null, destroyed, or disconnected player objects
            if (p == null || p.gameObject == null) return false;

            // Don't chase a player who is already dead / eliminated
            // (adjust this check based on what the base game exposes)
            return p.gameObject.activeInHierarchy;
        }
    }
}
