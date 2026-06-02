using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// A3: target attainment is reported distinctly from provider-health success rate so the
    /// run summary can't read "100%" while delivering a fraction of the requested items.
    /// </summary>
    public class AttainmentPercentTests
    {
        [Theory]
        [InlineData(17, 50, 34)]   // the real "100% provider success but 17/50" case
        [InlineData(50, 50, 100)]
        [InlineData(25, 50, 50)]
        [InlineData(0, 50, 0)]
        public void AttainmentPercent_ComputesExpected(int items, int target, int expected)
        {
            BrainarrOrchestrator.AttainmentPercent(items, target).Should().Be(expected);
        }

        [Fact]
        public void AttainmentPercent_ClampsAboveTarget_To100()
        {
            // Top-up/over-delivery shouldn't report >100%.
            BrainarrOrchestrator.AttainmentPercent(60, 50).Should().Be(100);
        }

        [Theory]
        [InlineData(10, 0)]
        [InlineData(0, 0)]
        [InlineData(5, -1)]
        public void AttainmentPercent_NonPositiveTarget_IsZero(int items, int target)
        {
            BrainarrOrchestrator.AttainmentPercent(items, target).Should().Be(0);
        }
    }
}
