using System.Collections.Generic;

namespace IssaPlugin.Network
{
    /// <summary>
    /// Pure, stateless vote-tallying logic extracted from VoteManager so it
    /// can be unit-tested without any Unity or Mirror dependencies.
    ///
    /// Rule: for each item, majority of submitted votes wins.
    ///       Tie (equal yes / no) or zero votes → <paramref name="fallback"/> is returned.
    /// </summary>
    internal static class VoteTally
    {
        /// <summary>
        /// Computes the enabled state for each item key based on received votes.
        /// </summary>
        /// <param name="votes">
        ///   Map of connectionId → (itemTypeKey → voted-enabled).
        ///   May be empty (no players voted).
        /// </param>
        /// <param name="itemKeys">
        ///   All item type keys that should appear in the result.
        /// </param>
        /// <param name="fallback">
        ///   Per-key fallback: called when a key has zero votes or a tie.
        ///   Typically returns item.Enabled (the host's current setting).
        /// </param>
        /// <returns>
        ///   Dictionary mapping each key in <paramref name="itemKeys"/> to its
        ///   post-vote enabled state.
        /// </returns>
        public static Dictionary<int, bool> Compute(
            IReadOnlyDictionary<int, Dictionary<int, bool>> votes,
            IEnumerable<int> itemKeys,
            System.Func<int, bool> fallback
        )
        {
            var results = new Dictionary<int, bool>();

            foreach (int key in itemKeys)
            {
                int yes = 0,
                    total = 0;
                foreach (var playerVotes in votes.Values)
                {
                    if (playerVotes.TryGetValue(key, out bool enabled))
                    {
                        total++;
                        if (enabled)
                            yes++;
                    }
                }

                int no = total - yes;

                // Strict majority wins; tie or zero votes → preserve host's current state.
                if (total == 0 || yes == no)
                {
                    results[key] = fallback(key);
                }
                else
                {
                    results[key] = yes > no;
                }
            }

            return results;
        }
    }
}
