using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class BrainarrSettingsIterationProfileTests
    {
        [Fact]
        public void GetIterationProfile_Off_DisablesBackfill()
        {
            var s = new BrainarrSettings { BackfillStrategy = BackfillStrategy.Off };
            var p = s.GetIterationProfile();
            p.EnableRefinement.Should().BeFalse();
            p.MaxIterations.Should().Be(0);
            p.ZeroStop.Should().Be(0);
            p.LowStop.Should().Be(0);
            s.IsBackfillEnabled().Should().BeFalse();
        }

        [Fact]
        public void GetIterationProfile_Standard_MapsThresholds_WithSensitivity()
        {
            var s = new BrainarrSettings
            {
                BackfillStrategy = BackfillStrategy.Standard,
                IterativeMaxIterations = 4,
                IterativeZeroSuccessStopThreshold = 0, // below minimums
                IterativeLowSuccessStopThreshold = 1,
                TopUpStopSensitivity = StopSensitivity.Strict,
                IterativeCooldownMs = 750,
                GuaranteeExactTarget = true
            };
            var p = s.GetIterationProfile();
            p.EnableRefinement.Should().BeTrue();
            p.MaxIterations.Should().Be(4);
            // Strict maps ZeroStop>=1, LowStop>=2 and applies Math.Max with thresholds
            p.ZeroStop.Should().BeGreaterThanOrEqualTo(1);
            p.LowStop.Should().BeGreaterThanOrEqualTo(2);
            p.CooldownMs.Should().Be(750);
            p.GuaranteeExactTarget.Should().BeTrue();
            s.IsBackfillEnabled().Should().BeTrue();
        }

        [Fact]
        public void GetIterationProfile_Lenient_IncreasesMinimums()
        {
            var s = new BrainarrSettings
            {
                BackfillStrategy = BackfillStrategy.Standard,
                IterativeZeroSuccessStopThreshold = 5,
                IterativeLowSuccessStopThreshold = 3,
                TopUpStopSensitivity = StopSensitivity.Lenient
            };
            var p = s.GetIterationProfile();
            // Lenient maps ZeroStop>=2, LowStop>=4; with thresholds higher, Math.Max should pick thresholds
            p.ZeroStop.Should().BeGreaterThanOrEqualTo(5);
            p.LowStop.Should().BeGreaterThanOrEqualTo(4);
        }
    }
}
