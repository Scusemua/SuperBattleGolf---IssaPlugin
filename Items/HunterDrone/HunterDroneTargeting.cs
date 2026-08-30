using System.Collections.Generic;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Shared aim-based target selection for the Hunter Drone.
    ///
    /// Both the client (as a pre-check, so a bad aim does not waste the item) and the
    /// server (authoritatively, before consuming the item and spawning) run
    /// <see cref="SelectTarget"/> against the same aim ray so their answers agree.
    ///
    /// Selection rule: a player is a candidate when they are a valid target, lie within
    /// MaxAimAngle of the aim ray, and are no further than MaxTargetDistance away.
    /// The candidate with the smallest angle to the ray wins; candidates whose angles are
    /// within AmbiguousAngleEpsilon of the best (the "aiming at two players at once" case)
    /// are tie-broken by distance from the aim origin, so the nearer one is chosen.
    /// </summary>
    public static class HunterDroneTargeting
    {
        /// Angles this close together are treated as equally-well aimed at, and are
        /// resolved by picking the closer player instead.
        private const float AmbiguousAngleEpsilon = 3f;

        /// Aim at a player's chest rather than their feet, so looking at a player's body
        /// registers as aiming at them.
        private const float TargetHeightOffset = 1.0f;

        /// <summary>
        /// Picks the player the aim ray is pointed most directly at, or null when the
        /// player is not aiming close enough to anyone.
        /// </summary>
        /// <param name="origin">World-space origin of the aim ray (the camera).</param>
        /// <param name="direction">Aim direction; need not be normalized.</param>
        /// <param name="thrower">The player using the item; used for friendly-fire filtering.</param>
        /// <param name="scratch">Reusable candidate buffer. Cleared before use.</param>
        public static PlayerInfo SelectTarget(
            Vector3 origin,
            Vector3 direction,
            PlayerInfo thrower,
            List<(PlayerInfo player, float angle, float sqDist)> scratch
        )
        {
            scratch.Clear();

            if (direction.sqrMagnitude < 0.0001f)
                return null;

            Vector3 aimDir = direction.normalized;
            float maxAngle = ModConfig.HunterDrone.MaxAimAngle.Value;
            float maxDist = ModConfig.HunterDrone.MaxTargetDistance.Value;
            float maxSqDist = maxDist * maxDist;

            bool friendlyFire = ModConfig.HunterDrone.FriendlyFire.Value;
            bool attackFinished = ModConfig.HunterDrone.AttackFinishedPlayers.Value;

            Consider(GameManager.LocalPlayerInfo);

            var remotes = GameManager.RemotePlayers;
            if (remotes != null)
            {
                foreach (var p in remotes)
                    Consider(p);
            }

            if (scratch.Count == 0)
                return null;

            int bestIdx = 0;
            for (int i = 1; i < scratch.Count; i++)
            {
                float angleDelta = scratch[i].angle - scratch[bestIdx].angle;

                if (angleDelta < -AmbiguousAngleEpsilon)
                {
                    // Clearly better aimed at.
                    bestIdx = i;
                }
                else if (angleDelta <= AmbiguousAngleEpsilon)
                {
                    // Ambiguous — the player is aiming at both. Take the closer one.
                    if (scratch[i].sqDist < scratch[bestIdx].sqDist)
                        bestIdx = i;
                }
            }

            return scratch[bestIdx].player;

            void Consider(PlayerInfo player)
            {
                if (!IsValidTarget(player, thrower, friendlyFire, attackFinished))
                    return;

                Vector3 toPlayer =
                    player.transform.position + Vector3.up * TargetHeightOffset - origin;

                float sqDist = toPlayer.sqrMagnitude;
                if (sqDist < 0.0001f || sqDist > maxSqDist)
                    return;

                float angle = Vector3.Angle(aimDir, toPlayer);
                if (angle > maxAngle)
                    return;

                scratch.Add((player, angle, sqDist));
            }
        }

        /// <summary>
        /// Shared validity filter, matching <c>HunterDroneBehaviour.IsValidTarget</c>.
        /// </summary>
        public static bool IsValidTarget(
            PlayerInfo player,
            PlayerInfo thrower,
            bool friendlyFire,
            bool attackFinishedPlayers
        )
        {
            if (player == null || !player.gameObject.activeInHierarchy)
                return false;

            if (!friendlyFire && thrower != null && player.PlayerId.Guid == thrower.PlayerId.Guid)
                return false;

            if (
                !attackFinishedPlayers
                && player.AsGolfer?.MatchResolution == PlayerMatchResolution.Scored
            )
                return false;

            return true;
        }
    }
}
