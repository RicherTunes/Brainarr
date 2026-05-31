using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using Xunit;

namespace Brainarr.Tests.Services.Prompting
{
    /// <summary>
    /// Capacity-aware context-depth tests. The per-budget target artist/album counts must
    /// scale deeper for frontier (large) token budgets while remaining unchanged for small
    /// budgets. Only the upper clamp rises (artist 90 -> 250, album 220 -> 600); the
    /// tokenBudget/260 and tokenBudget/120 scaling is preserved.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "PromptBuilder")]
    public class DefaultContextPolicyTests
    {
        private static readonly DefaultContextPolicy Policy = new();

        // Large library so the tokenBudget-scaled branch (totalArtists > 200 / totalAlbums > 400) is taken.
        private const int LargeArtists = 5000;
        private const int LargeAlbums = 20000;

        [Fact]
        public void LargeBudget_AllowsDeeperArtistContext()
        {
            // tokenBudget/260 == 461 at 120000. Old cap clamped to 90; new cap allows up to 250.
            var count = Policy.DetermineTargetArtistCount(LargeArtists, tokenBudget: 120000);

            Assert.True(count > 90, $"Expected large-budget artist count > old cap 90, got {count}");
            Assert.Equal(250, count);
        }

        [Fact]
        public void LargeBudget_AllowsDeeperAlbumContext()
        {
            // tokenBudget/120 == 1000 at 120000. Old cap clamped to 220; new cap allows up to 600.
            var count = Policy.DetermineTargetAlbumCount(LargeAlbums, tokenBudget: 120000);

            Assert.True(count > 220, $"Expected large-budget album count > old cap 220, got {count}");
            Assert.Equal(600, count);
        }

        [Fact]
        public void SmallBudget_ArtistCountUnchanged()
        {
            // tokenBudget/260 == 15 at 4000 -> Max(32, 15) == 32. Below both old and new caps,
            // so raising the ceiling must not change small-budget behavior.
            var count = Policy.DetermineTargetArtistCount(LargeArtists, tokenBudget: 4000);

            Assert.Equal(32, count);
        }

        [Fact]
        public void SmallBudget_AlbumCountUnchanged()
        {
            // tokenBudget/120 == 33 at 4000 -> Max(70, 33) == 70. Below both old and new caps.
            var count = Policy.DetermineTargetAlbumCount(LargeAlbums, tokenBudget: 4000);

            Assert.Equal(70, count);
        }
    }
}
