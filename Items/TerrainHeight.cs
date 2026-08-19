using UnityEngine;

namespace IssaPlugin.Items
{
    /// <summary>
    /// Shared ground-height sampling for airborne items.
    ///
    /// Any item that hovers or flies at a configured altitude needs that altitude
    /// measured from the terrain, not from the world origin. Assuming y = 0 works on
    /// flat maps but puts the object underground on maps whose terrain sits well above
    /// the origin — the ice maps especially, where the Harrier was spawning inside hills.
    ///
    /// Corrections are offered at three levels, matching the flight shapes items use:
    ///   • <see cref="TrySample"/> / <see cref="GroundHeightAt"/> / <see cref="AboveGround"/>
    ///     — ground height at a single point.
    ///   • <see cref="ClearPath"/> — height that clears terrain along a whole flight path,
    ///     for items that fly level between two points and would otherwise clip a ridge
    ///     between them even when the endpoints themselves are clear.
    ///   • <see cref="HighestGroundOnRing"/> — ground height clearing a circular orbit,
    ///     which is flown entirely at a radius away from its centre.
    ///
    /// All of these correct at spawn time only. An item that holds a fixed height for its
    /// whole session can still end up too low if play moves to higher ground; continuous
    /// correction is DonutFlyBehaviour's per-frame terrain follow, not this class.
    /// </summary>
    public static class TerrainHeight
    {
        /// <summary>
        /// Height above the probe point that ground rays start from.
        ///
        /// This is a genuine trade-off. Too small and a probe taken near ground level —
        /// beside a player on a slope, say — starts inside the hillside, so the ray
        /// travels away from the surface and reports no ground at all. Too large and
        /// overhead geometry (bridges, tunnel roofs) gets mistaken for ground and pushes
        /// the item far too high.
        ///
        /// 50 units clears any hill a probe is likely to start inside while staying well
        /// under normal overhead clearance. Callers probing from cruising altitude are
        /// already above terrain, so the lift costs them nothing.
        /// </summary>
        private const float ProbeStartOffset = 50f;

        /// <summary>
        /// Maximum distance a ground probe ray travels before giving up. Generous
        /// enough to reach the ground from an item's cruising altitude.
        /// </summary>
        private const float ProbeMaxDistance = 2100f;

        /// <summary>
        /// Number of samples taken along each leg of a path in <see cref="ClearPath"/>.
        /// Enough to catch a ridge without making a summon noticeably expensive.
        /// </summary>
        private const int SamplesPerLeg = 8;

        /// <summary>
        /// Casts a ray downward from <paramref name="position"/> and reports the y
        /// coordinate of the first ground surface hit.
        ///
        /// Uses ItemHelper.GroundLayerMask ("Default" + "Terrain") — the same mask
        /// DonutFlyBehaviour already uses for long downward terrain probes. The base
        /// game's OrbitalLaserHeightSnappingMask is deliberately not used here: it is
        /// authored for a ray cast from 0.1 units above an on-ground marker, which is a
        /// different problem from probing downward from cruising altitude.
        ///
        /// Returns false when nothing is hit. That is an ordinary outcome, not an error:
        /// probes taken along a fly-in path routinely fall outside the terrain's bounds.
        ///
        /// Because the ray starts <see cref="ProbeStartOffset"/> above the point, the
        /// surface it finds may sit above the point itself. That is intended — callers
        /// want the height of the ground in that column, not only what is strictly below
        /// them — but it does mean a returned height can exceed position.y.
        /// </summary>
        public static bool TrySample(Vector3 position, out float groundY)
        {
            Vector3 origin = new Vector3(
                position.x,
                position.y + ProbeStartOffset,
                position.z
            );

            if (
                Physics.Raycast(
                    origin,
                    Vector3.down,
                    out RaycastHit hit,
                    ProbeMaxDistance,
                    ItemHelper.GroundLayerMask,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                groundY = hit.point.y;
                return true;
            }

            groundY = 0f;
            return false;
        }

        /// <summary>
        /// Ground height beneath <paramref name="position"/>, or
        /// <paramref name="fallbackY"/> when no ground is found there.
        /// </summary>
        public static float GroundHeightAt(Vector3 position, float fallbackY)
        {
            return TrySample(position, out float groundY) ? groundY : fallbackY;
        }

        /// <summary>
        /// Returns <paramref name="position"/> with its y set to
        /// <paramref name="altitude"/> above the ground beneath it. If no ground is
        /// found, the point's existing y is used as the base — a far better estimate
        /// than the world origin.
        /// </summary>
        public static Vector3 AboveGround(Vector3 position, float altitude)
        {
            position.y = GroundHeightAt(position, position.y) + altitude;
            return position;
        }

        /// <summary>
        /// Returns the y coordinate at which level flight along
        /// <paramref name="start"/> → <paramref name="via"/> → <paramref name="end"/>
        /// stays <paramref name="altitude"/> above the highest terrain anywhere on that
        /// path.
        ///
        /// Correcting only the destination is not enough: an item that approaches from
        /// far off-map at a fixed height can fly straight through a ridge while both its
        /// spawn point and its hover point are perfectly clear.
        /// </summary>
        public static float ClearPath(Vector3 start, Vector3 via, Vector3 end, float altitude)
        {
            // Seed with the ground under the waypoint itself, so a path entirely off the
            // terrain still produces a sensible height.
            float highestGround = GroundHeightAt(via, via.y - altitude);

            // Samples that hit nothing are skipped — they are off the edge of the
            // terrain, so there is no ground there to clear.
            for (int i = 1; i <= SamplesPerLeg; i++)
            {
                float t = i / (float)SamplesPerLeg;

                if (TrySample(Vector3.Lerp(start, via, t), out float inboundY))
                    highestGround = Mathf.Max(highestGround, inboundY);

                if (TrySample(Vector3.Lerp(via, end, t), out float outboundY))
                    highestGround = Mathf.Max(highestGround, outboundY);
            }

            return highestGround + altitude;
        }

        /// <summary>
        /// Returns the highest ground height found anywhere on a circle of
        /// <paramref name="radius"/> around <paramref name="center"/>, including the
        /// centre itself.
        ///
        /// Sampling the ring matters as much as sampling the centre: an orbit is flown
        /// entirely at <paramref name="radius"/> away from the centre, so terrain under
        /// the centre says little about what the item actually flies over.
        ///
        /// This returns a *ground* height, not a flight height — callers add their own
        /// altitude. Orbit code typically stores a centre whose y is a base that altitude
        /// is added to later, and returning the base directly avoids adding altitude here
        /// only for the caller to subtract it straight back off.
        /// </summary>
        public static float HighestGroundOnRing(Vector3 center, float radius)
        {
            float highestGround = GroundHeightAt(center, center.y);

            // 12 samples puts one every 30° — dense enough to catch a ridge crossing
            // the ring without making the summon expensive.
            const int RingSamples = 12;
            for (int i = 0; i < RingSamples; i++)
            {
                float rad = (i / (float)RingSamples) * Mathf.PI * 2f;
                Vector3 point = new Vector3(
                    center.x + Mathf.Cos(rad) * radius,
                    center.y,
                    center.z + Mathf.Sin(rad) * radius
                );

                if (TrySample(point, out float ringY))
                    highestGround = Mathf.Max(highestGround, ringY);
            }

            return highestGround;
        }
    }
}
