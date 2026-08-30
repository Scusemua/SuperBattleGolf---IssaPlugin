using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Server-side aim assist for AC130 rockets.
    ///
    /// Picks the enemy player sitting closest to the line the shot was fired along —
    /// smallest angular deviation from the aim direction, not smallest distance — and
    /// rotates the fire direction part of the way toward them. The correction is a
    /// fraction of the way there rather than a full lock, so the shot still lands where
    /// the player pointed and merely drifts toward the target.
    ///
    /// This runs only on the server, inside AC130NetworkBridge.ServerFireAC130, and
    /// adjusts the rotation the rocket is spawned with. Rockets are simulated locally on
    /// each peer from the spawn position and rotation Mirror replicates, so correcting
    /// the direction before the spawn makes the assist appear identically for every
    /// player with no extra messages and no client-side prediction to reconcile.
    /// </summary>
    public static class AC130AimAssist
    {
        /// <summary>
        /// Returns <paramref name="aimDirection"/> nudged toward the best aim-assist
        /// target, or unchanged when assist is disabled or no target qualifies.
        /// </summary>
        /// <param name="origin">World position the rocket is fired from.</param>
        /// <param name="aimDirection">Normalised direction the player aimed.</param>
        /// <param name="shooter">
        /// The firing player, used to skip self-targeting. May be null.
        /// </param>
        /// <param name="isHeavy">Whether this is a heavy (big shot) rocket.</param>
        public static Vector3 Apply(
            Vector3 origin,
            Vector3 aimDirection,
            PlayerInfo shooter,
            bool isHeavy
        )
        {
            if (!ModConfig.AC130.AimAssistEnabled.Value)
                return aimDirection;

            if (isHeavy && !ModConfig.AC130.AimAssistAffectsHeavy.Value)
                return aimDirection;

            float strength = Mathf.Clamp01(ModConfig.AC130.AimAssistStrength.Value);
            if (strength <= 0f)
                return aimDirection;

            Vector3 aim = aimDirection.normalized;
            if (aim.sqrMagnitude < 0.0001f)
                return aimDirection;

            Vector3 targetPoint;
            if (!TryFindTarget(origin, aim, shooter, out targetPoint))
                return aimDirection;

            Vector3 toTarget = targetPoint - origin;
            if (toTarget.sqrMagnitude < 0.0001f)
                return aimDirection;

            // Slerp rather than Lerp so the correction is a constant fraction of the
            // angular error regardless of how far away the target is.
            return Vector3.Slerp(aim, toTarget.normalized, strength).normalized;
        }

        /// <summary>
        /// Finds the valid player with the smallest angle between the aim direction and
        /// the direction from <paramref name="origin"/> to that player, within the
        /// configured cone and range.
        /// </summary>
        private static bool TryFindTarget(
            Vector3 origin,
            Vector3 aim,
            PlayerInfo shooter,
            out Vector3 targetPoint
        )
        {
            targetPoint = Vector3.zero;

            float maxAngle = ModConfig.AC130.AimAssistMaxAngle.Value;
            if (maxAngle <= 0f)
                return false;

            float maxDistance = ModConfig.AC130.AimAssistMaxDistance.Value;
            float maxDistanceSq = maxDistance * maxDistance;
            float heightOffset = ModConfig.AC130.AimAssistTargetHeightOffset.Value;

            // Server-side enumeration: FindObjectsByType covers every player on the
            // host, unlike GameManager.RemotePlayers which is relative to the local
            // client and would miss the host's own golfer.
            var inventories = Object.FindObjectsByType<PlayerInventory>(FindObjectsSortMode.None);

            float bestAngle = maxAngle;
            bool found = false;

            foreach (var inventory in inventories)
            {
                if (inventory == null)
                    continue;

                var info = inventory.PlayerInfo;
                if (!IsValidTarget(info, shooter))
                    continue;

                Vector3 point = info.transform.position + Vector3.up * heightOffset;
                Vector3 toPoint = point - origin;

                float distSq = toPoint.sqrMagnitude;
                if (distSq < 0.0001f || distSq > maxDistanceSq)
                    continue;

                float angle = Vector3.Angle(aim, toPoint);
                if (angle >= bestAngle)
                    continue;

                bestAngle = angle;
                targetPoint = point;
                found = true;
            }

            return found;
        }

        private static bool IsValidTarget(PlayerInfo player, PlayerInfo shooter)
        {
            if (player == null || player.gameObject == null)
                return false;

            // Skips players who are dead, eliminated, or otherwise despawned.
            if (!player.gameObject.activeInHierarchy)
                return false;

            // Spectators keep an active GameObject — PlayerSpectator never deactivates
            // it — so the check above does not catch them. Without this, assist would
            // curve shots toward players who are out of play.
            if (player.AsSpectator != null && player.AsSpectator.IsInSpectatorMode)
                return false;

            // Never let assist home in on a smoke-bomb-invisible player: the base game
            // deliberately drops rocket homing on them (Rocket.ApplyHoming), and
            // silently curving toward one would give away a position the item is meant
            // to hide. The shooter's own teammates are exempt from the invisibility in
            // team modes, matching PlayerInfo.CanDetectSmokeBombInvisiblePlayer.
            if (shooter != null && !shooter.CanDetectSmokeBombInvisiblePlayer(player))
                return false;

            if (
                !ModConfig.AC130.AimAssistTargetsSelf.Value
                && shooter != null
                && player.PlayerId.Guid == shooter.PlayerId.Guid
            )
                return false;

            if (
                !ModConfig.AC130.AimAssistTargetsFinishedPlayers.Value
                && player.AsGolfer != null
                && player.AsGolfer.MatchResolution == PlayerMatchResolution.Scored
            )
                return false;

            return true;
        }
    }
}
