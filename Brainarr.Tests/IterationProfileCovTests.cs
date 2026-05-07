using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using Xunit;

namespace Brainarr.Tests
{
    public class IterationProfileCovTests
    {
        [Fact]
        public void IterationProfile_DefaultValues_AreZeroOrFalse()
        {
            // Arrange & Act
            var profile = new IterationProfile();

            // Assert - default values for bool is false, int is 0
            profile.EnableRefinement.Should().BeFalse("because default bool is false");
            profile.MaxIterations.Should().Be(0, "because default int is 0");
            profile.ZeroStop.Should().Be(0, "because default int is 0");
            profile.LowStop.Should().Be(0, "because default int is 0");
            profile.CooldownMs.Should().Be(0, "because default int is 0");
            profile.GuaranteeExactTarget.Should().BeFalse("because default bool is false");
        }

        [Fact]
        public void IterationProfile_EnableRefinement_CanBeSetAndRetrieved()
        {
            // Arrange
            var profile = new IterationProfile();

            // Act
            profile.EnableRefinement = true;

            // Assert
            profile.EnableRefinement.Should().BeTrue("because it was set to true");
        }

        [Fact]
        public void IterationProfile_MaxIterations_CanBeSetAndRetrieved()
        {
            // Arrange
            var profile = new IterationProfile();
            const int expectedValue = 5;

            // Act
            profile.MaxIterations = expectedValue;

            // Assert
            profile.MaxIterations.Should().Be(expectedValue, "because it was set to 5");
        }

        [Fact]
        public void IterationProfile_ZeroStop_CanBeSetAndRetrieved()
        {
            // Arrange
            var profile = new IterationProfile();
            const int expectedValue = 3;

            // Act
            profile.ZeroStop = expectedValue;

            // Assert
            profile.ZeroStop.Should().Be(expectedValue, "because it was set to 3");
        }

        [Fact]
        public void IterationProfile_LowStop_CanBeSetAndRetrieved()
        {
            // Arrange
            var profile = new IterationProfile();
            const int expectedValue = 7;

            // Act
            profile.LowStop = expectedValue;

            // Assert
            profile.LowStop.Should().Be(expectedValue, "because it was set to 7");
        }

        [Fact]
        public void IterationProfile_CooldownMs_CanBeSetAndRetrieved()
        {
            // Arrange
            var profile = new IterationProfile();
            const int expectedValue = 1000;

            // Act
            profile.CooldownMs = expectedValue;

            // Assert
            profile.CooldownMs.Should().Be(expectedValue, "because it was set to 1000");
        }

        [Fact]
        public void IterationProfile_GuaranteeExactTarget_CanBeSetAndRetrieved()
        {
            // Arrange
            var profile = new IterationProfile();

            // Act
            profile.GuaranteeExactTarget = true;

            // Assert
            profile.GuaranteeExactTarget.Should().BeTrue("because it was set to true");
        }

        [Fact]
        public void IterationProfile_AllProperties_CanBeSetTogether()
        {
            // Arrange
            var profile = new IterationProfile();

            // Act
            profile.EnableRefinement = true;
            profile.MaxIterations = 10;
            profile.ZeroStop = 2;
            profile.LowStop = 5;
            profile.CooldownMs = 500;
            profile.GuaranteeExactTarget = true;

            // Assert
            profile.EnableRefinement.Should().BeTrue("because it was set to true");
            profile.MaxIterations.Should().Be(10, "because it was set to 10");
            profile.ZeroStop.Should().Be(2, "because it was set to 2");
            profile.LowStop.Should().Be(5, "because it was set to 5");
            profile.CooldownMs.Should().Be(500, "because it was set to 500");
            profile.GuaranteeExactTarget.Should().BeTrue("because it was set to true");
        }
    }
}
