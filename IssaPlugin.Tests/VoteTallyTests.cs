using System.Collections.Generic;
using IssaPlugin.Network;
using NUnit.Framework;

namespace IssaPlugin.Tests
{
    /// <summary>
    /// Tests for VoteTally.Compute — the pure tallying logic extracted from
    /// VoteManager.TallyAndBroadcast.
    ///
    /// All test data uses integer item-type keys (matching the cast used in
    /// VoteManager) and a simple fallback that returns false for every key,
    /// unless a specific fallback is needed for the test under scrutiny.
    /// </summary>
    [TestFixture]
    public class VoteTallyTests
    {
        // Arbitrary item keys that parallel ItemRegistry IDs (100–117).
        private const int ItemA = 100;
        private const int ItemB = 101;
        private const int ItemC = 102;

        private static readonly int[] AllKeys = { ItemA, ItemB, ItemC };

        /// Convenience: fallback that always says "disabled".
        private static bool FallbackFalse(int _) => false;

        /// Convenience: fallback that always says "enabled".
        private static bool FallbackTrue(int _) => true;

        // ── Zero-vote edge cases ──────────────────────────────────────────────

        [Test]
        public void NoVoters_AllItemsReturnFallbackValue()
        {
            // Empty votes dict simulates a game where nobody submitted before timeout.
            var votes = new Dictionary<int, Dictionary<int, bool>>();

            var result = VoteTally.Compute(votes, AllKeys, FallbackTrue);

            foreach (int key in AllKeys)
                Assert.That(
                    result[key],
                    Is.True,
                    $"Item {key} should use fallback (true) when there are no votes."
                );
        }

        [Test]
        public void NoVoters_FallbackFalse_AllItemsDisabled()
        {
            var votes = new Dictionary<int, Dictionary<int, bool>>();

            var result = VoteTally.Compute(votes, AllKeys, FallbackFalse);

            foreach (int key in AllKeys)
                Assert.That(
                    result[key],
                    Is.False,
                    $"Item {key} should use fallback (false) when there are no votes."
                );
        }

        [Test]
        public void NoVoters_ResultContainsAllItemKeys()
        {
            var votes = new Dictionary<int, Dictionary<int, bool>>();

            var result = VoteTally.Compute(votes, AllKeys, FallbackFalse);

            Assert.That(
                result.Keys,
                Is.EquivalentTo(AllKeys),
                "Result must contain exactly the keys provided in itemKeys."
            );
        }

        // ── Unanimous votes ───────────────────────────────────────────────────

        [Test]
        public void UnanimousYes_EnablesItem()
        {
            var votes = new Dictionary<int, Dictionary<int, bool>>
            {
                [1] = new Dictionary<int, bool> { [ItemA] = true },
                [2] = new Dictionary<int, bool> { [ItemA] = true },
                [3] = new Dictionary<int, bool> { [ItemA] = true },
            };

            var result = VoteTally.Compute(votes, new[] { ItemA }, FallbackFalse);

            Assert.That(result[ItemA], Is.True, "Unanimous yes must enable the item.");
        }

        [Test]
        public void UnanimousNo_DisablesItem()
        {
            var votes = new Dictionary<int, Dictionary<int, bool>>
            {
                [1] = new Dictionary<int, bool> { [ItemA] = false },
                [2] = new Dictionary<int, bool> { [ItemA] = false },
            };

            var result = VoteTally.Compute(votes, new[] { ItemA }, FallbackTrue);

            Assert.That(result[ItemA], Is.False, "Unanimous no must disable the item.");
        }

        // ── Majority rules ────────────────────────────────────────────────────

        [Test]
        public void MajorityYes_EnablesItem()
        {
            // 2 yes, 1 no → enabled
            var votes = new Dictionary<int, Dictionary<int, bool>>
            {
                [1] = new Dictionary<int, bool> { [ItemB] = true },
                [2] = new Dictionary<int, bool> { [ItemB] = true },
                [3] = new Dictionary<int, bool> { [ItemB] = false },
            };

            var result = VoteTally.Compute(votes, new[] { ItemB }, FallbackFalse);

            Assert.That(result[ItemB], Is.True, "2-1 majority for yes must enable the item.");
        }

        [Test]
        public void MajorityNo_DisablesItem()
        {
            // 1 yes, 2 no → disabled
            var votes = new Dictionary<int, Dictionary<int, bool>>
            {
                [1] = new Dictionary<int, bool> { [ItemB] = false },
                [2] = new Dictionary<int, bool> { [ItemB] = false },
                [3] = new Dictionary<int, bool> { [ItemB] = true },
            };

            var result = VoteTally.Compute(votes, new[] { ItemB }, FallbackTrue);

            Assert.That(result[ItemB], Is.False, "2-1 majority for no must disable the item.");
        }

        // ── Tie handling ──────────────────────────────────────────────────────

        [Test]
        public void TwoPlayerTie_UsesFallback()
        {
            // 1 yes, 1 no — strict majority (yes*2 > total) is 2 > 2 = false → tie → fallback.
            var votes = new Dictionary<int, Dictionary<int, bool>>
            {
                [1] = new Dictionary<int, bool> { [ItemC] = true },
                [2] = new Dictionary<int, bool> { [ItemC] = false },
            };

            // Fallback says disabled.
            var resultFalse = VoteTally.Compute(votes, new[] { ItemC }, FallbackFalse);
            Assert.That(resultFalse[ItemC], Is.False, "Tie must respect fallback=false.");

            // Fallback says enabled.
            var resultTrue = VoteTally.Compute(votes, new[] { ItemC }, FallbackTrue);
            Assert.That(resultTrue[ItemC], Is.True, "Tie must respect fallback=true.");
        }

        [Test]
        public void FourPlayerTie_UsesFallback()
        {
            // 2 yes, 2 no — yes*2 = 4, total = 4, 4 > 4 is false → fallback.
            var votes = new Dictionary<int, Dictionary<int, bool>>
            {
                [1] = new Dictionary<int, bool> { [ItemA] = true },
                [2] = new Dictionary<int, bool> { [ItemA] = true },
                [3] = new Dictionary<int, bool> { [ItemA] = false },
                [4] = new Dictionary<int, bool> { [ItemA] = false },
            };

            var result = VoteTally.Compute(votes, new[] { ItemA }, FallbackTrue);

            Assert.That(
                result[ItemA],
                Is.True,
                "4-player 2-2 tie must fall back to the host's current state."
            );
        }

        // ── Partial participation (voter skipped an item) ─────────────────────

        [Test]
        public void VoterDidNotVoteForItem_IsExcludedFromTally()
        {
            // Player 2 submitted a ballot that doesn't include ItemB.
            // Only player 1's vote for ItemB should count.
            var votes = new Dictionary<int, Dictionary<int, bool>>
            {
                [1] = new Dictionary<int, bool> { [ItemA] = true, [ItemB] = true },
                [2] = new Dictionary<int, bool> { [ItemA] = false }, // no ItemB vote
            };

            var result = VoteTally.Compute(votes, new[] { ItemA, ItemB }, FallbackFalse);

            // ItemA: 1 yes, 1 no → tie → fallback (false)
            Assert.That(result[ItemA], Is.False, "ItemA should be a tie → fallback.");
            // ItemB: 1 yes, 0 no from a single voter → majority yes
            Assert.That(
                result[ItemB],
                Is.True,
                "ItemB with only one voter (yes) should be enabled."
            );
        }

        // ── Multi-item independence ───────────────────────────────────────────

        [Test]
        public void MultipleItems_TalliedIndependently()
        {
            // ItemA: 3 yes → enabled
            // ItemB: 3 no  → disabled
            // ItemC: 1 yes, 2 no → majority no → disabled
            var votes = new Dictionary<int, Dictionary<int, bool>>
            {
                [1] = new Dictionary<int, bool>
                {
                    [ItemA] = true,
                    [ItemB] = false,
                    [ItemC] = true,
                },
                [2] = new Dictionary<int, bool>
                {
                    [ItemA] = true,
                    [ItemB] = false,
                    [ItemC] = false,
                },
                [3] = new Dictionary<int, bool>
                {
                    [ItemA] = true,
                    [ItemB] = false,
                    [ItemC] = false,
                },
            };

            var result = VoteTally.Compute(votes, AllKeys, FallbackFalse);

            Assert.That(result[ItemA], Is.True, "ItemA unanimous yes → enabled.");
            Assert.That(result[ItemB], Is.False, "ItemB unanimous no → disabled.");
            Assert.That(result[ItemC], Is.False, "ItemC 1-2 majority no → disabled.");
        }

        // ── Result completeness ───────────────────────────────────────────────

        [Test]
        public void Result_ContainsExactlyTheRequestedKeys()
        {
            // Even if voters only submitted votes for a subset of keys,
            // the result must have an entry for every key in itemKeys.
            var votes = new Dictionary<int, Dictionary<int, bool>>
            {
                [1] = new Dictionary<int, bool> { [ItemA] = true }, // only voted on ItemA
            };

            var result = VoteTally.Compute(votes, AllKeys, FallbackFalse);

            Assert.That(
                result.Keys,
                Is.EquivalentTo(AllKeys),
                "Result must contain exactly the keys provided in itemKeys."
            );
        }

        // ── Single-voter edge case ────────────────────────────────────────────

        [Test]
        public void SingleVoterYes_EnablesItem()
        {
            var votes = new Dictionary<int, Dictionary<int, bool>>
            {
                [1] = new Dictionary<int, bool> { [ItemA] = true },
            };

            var result = VoteTally.Compute(votes, new[] { ItemA }, FallbackFalse);

            // yes=1, total=1 → 1*2 > 1 → 2 > 1 → true
            Assert.That(result[ItemA], Is.True, "Single yes vote should enable the item.");
        }

        [Test]
        public void SingleVoterNo_DisablesItem()
        {
            var votes = new Dictionary<int, Dictionary<int, bool>>
            {
                [1] = new Dictionary<int, bool> { [ItemA] = false },
            };

            var result = VoteTally.Compute(votes, new[] { ItemA }, FallbackTrue);

            // yes=0, total=1 → 0*2 > 1 → 0 > 1 → false
            Assert.That(result[ItemA], Is.False, "Single no vote should disable the item.");
        }

        // ── Fallback is per-key ───────────────────────────────────────────────

        [Test]
        public void PerKeyFallback_UsedForEachTiedOrUnvotedItem()
        {
            // No votes at all; fallback returns true for ItemA, false for ItemB.
            var votes = new Dictionary<int, Dictionary<int, bool>>();

            var result = VoteTally.Compute(
                votes,
                new[] { ItemA, ItemB },
                key => key == ItemA // true for ItemA, false for ItemB
            );

            Assert.That(result[ItemA], Is.True, "ItemA fallback should be true.");
            Assert.That(result[ItemB], Is.False, "ItemB fallback should be false.");
        }
    }
}
