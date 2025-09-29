using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using Xunit;

namespace Brainarr.Tests.Services.Prompting
{
    public class TokenBudgetGuardTests
    {
        [Theory]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        [InlineData(5000, 4000, 500, 3500)]
        [InlineData(3000, 4096, 512, 3000)]
        [InlineData(99999, 32000, 2000, 30000)]
        public void ClampTargetTokens_RespectsContextMinusHeadroom(int target, int context, int headroom, int expected)
        {
            var clamped = TokenBudgetGuard.ClampTargetTokens(target, context, headroom);
            Assert.Equal(expected, clamped);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void ClampTargetTokens_FloorsNegativeTargetsToZero()
        {
            var clamped = TokenBudgetGuard.ClampTargetTokens(-100, 4000, 500);
            Assert.Equal(0, clamped);
        }
    }
}
