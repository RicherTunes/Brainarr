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

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void Enforce_ClampsToTargetAndInvokesCallback()
        {
            var invoked = false;
            var result = TokenBudgetGuard.Enforce(5200, 64000, 2000, 4800, () => invoked = true);
            Assert.True(invoked);
            Assert.Equal(4800, result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void Enforce_DoesNothing_WhenWithinBudget()
        {
            var invoked = false;
            var result = TokenBudgetGuard.Enforce(3500, 64000, 2000, 4800, () => invoked = true);
            Assert.False(invoked);
            Assert.Equal(3500, result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "PromptBuilder")]
        public void Enforce_ZeroCap_ReturnsZero()
        {
            var invoked = false;
            var result = TokenBudgetGuard.Enforce(1500, 2000, 2000, 0, () => invoked = true);
            Assert.True(invoked);
            Assert.Equal(0, result);
        }
    }
}
