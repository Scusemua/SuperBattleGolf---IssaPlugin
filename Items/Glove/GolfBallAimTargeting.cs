using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Hunter-Drone-style aim-cone selection for golf balls (Evil Glove).
    ///
    /// Candidates are players who have a valid OwnBall; the aim sample point is the
    /// ball's world position (not the player's chest). Smallest angle wins;
    /// near-ties break by distance.
    ///
    /// Runs on both client (lock-on / miss gate) and server (authoritative pickup).
    /// </summary>
    public static class GolfBallAimTargeting
    {
        private const float AmbiguousAngleEpsilon = 3f;

        private static readonly FieldInfo IsInHoleField = AccessTools.Field(
            typeof(GolfBall),
            "isInHole"
        );

        /// <summary>
        /// Picks the ball-owner the aim ray points at most directly, or null.
        /// </summary>
        /// <param name="origin">Camera / aim origin.</param>
        /// <param name="direction">Aim direction (need not be normalized).</param>
        /// <param name="maxAimAngleDeg">Half-cone style max angle from aim ray.</param>
        /// <param name="maxTargetDistance">Max distance from origin to ball.</param>
        /// <param name="scratch">Reusable buffer; cleared before use.</param>
        /// <param name="isBallBusy">Optional: reject balls already in a glove hold.</param>
        public static PlayerInfo SelectBallOwner(
            Vector3 origin,
            Vector3 direction,
            float maxAimAngleDeg,
            float maxTargetDistance,
            List<(PlayerInfo owner, float angle, float sqDist)> scratch,
            System.Func<uint, bool> isBallBusy = null
        )
        {
            scratch.Clear();

            if (direction.sqrMagnitude < 0.0001f)
                return null;

            Vector3 aimDir = direction.normalized;
            float maxSqDist = maxTargetDistance * maxTargetDistance;

            // Same enumeration as HunterDroneTargeting (listen-host + dedicated).
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
                    bestIdx = i;
                else if (
                    angleDelta <= AmbiguousAngleEpsilon
                    && scratch[i].sqDist < scratch[bestIdx].sqDist
                )
                    bestIdx = i;
            }

            return scratch[bestIdx].owner;

            void Consider(PlayerInfo owner)
            {
                if (owner == null || !owner.gameObject.activeInHierarchy)
                    return;

                var ball = owner.AsGolfer?.OwnBall;
                if (ball == null || !IsBallEligible(ball))
                    return;

                uint ownerNetId = PlayerBallResolver.GetPlayerNetId(owner);
                if (ownerNetId == 0)
                    return;
                if (isBallBusy != null && isBallBusy(ownerNetId))
                    return;

                Vector3 toBall = ball.transform.position - origin;
                float sqDist = toBall.sqrMagnitude;
                if (sqDist < 0.0001f || sqDist > maxSqDist)
                    return;

                float angle = Vector3.Angle(aimDir, toBall);
                if (angle > maxAimAngleDeg)
                    return;

                scratch.Add((owner, angle, sqDist));
            }
        }

        /// <summary>
        /// Client/server pre-filter matching <c>GloveNetworkBridge</c> hold eligibility
        /// (hidden / OOB / in-hole). Full server checks still run after selection.
        /// </summary>
        public static bool IsBallEligible(GolfBall ball)
        {
            if (ball == null)
                return false;
            if (ball.IsHidden)
                return false;
            if (ball.OutOfBoundsReturnState != BallOutOfBoundsReturnState.None)
                return false;
            if (IsInHoleField != null && (bool)IsInHoleField.GetValue(ball))
                return false;
            return true;
        }
    }
}
