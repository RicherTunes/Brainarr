using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class ProviderCalibrationTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void AllProviders_HaveCalibrationProfiles()
        {
            foreach (AIProvider provider in Enum.GetValues<AIProvider>())
            {
                var profile = ProviderCalibrationRegistry.GetProfile(provider);
                profile.Should().NotBeNull($"provider {provider} should have a calibration profile");
                profile.ProviderName.Should().NotBeNullOrWhiteSpace();
                profile.Scale.Should().BeInRange(0.5, 1.5, $"{provider} scale out of range");
                profile.Bias.Should().BeInRange(-0.5, 0.5, $"{provider} bias out of range");
                profile.QualityTier.Should().BeInRange(0.0, 1.0, $"{provider} quality tier out of range");
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetAllProfiles_ReturnsAllProviders()
        {
            var profiles = ProviderCalibrationRegistry.GetAllProfiles();
            var providerValues = Enum.GetValues<AIProvider>();

            profiles.Count.Should().Be(providerValues.Length);
            foreach (var provider in providerValues)
            {
                profiles.Should().ContainKey(provider);
            }
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void DefaultProfile_IsIdentity()
        {
            var profile = ProviderCalibrationProfile.Default;
            profile.IsIdentity.Should().BeTrue();
            profile.Calibrate(0.75).Should().Be(0.75);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetProfileOrNull_WithNull_ReturnsNull()
        {
            var profile = ProviderCalibrationRegistry.GetProfileOrNull(null);
            profile.Should().BeNull();
        }

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData(AIProvider.OpenAI, true)]
        [InlineData(AIProvider.Anthropic, true)]
        [InlineData(AIProvider.ClaudeCodeSubscription, true)]
        [InlineData(AIProvider.OpenAICodexSubscription, true)]
        [InlineData(AIProvider.Ollama, false)]
        [InlineData(AIProvider.LMStudio, false)]
        [InlineData(AIProvider.Groq, false)]
        public void CloudProviders_AreIdentity_LocalProviders_AreNot(AIProvider provider, bool expectedIdentity)
        {
            var profile = ProviderCalibrationRegistry.GetProfile(provider);
            profile.IsIdentity.Should().Be(expectedIdentity);
        }

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData(0.0)]
        [InlineData(0.5)]
        [InlineData(1.0)]
        [InlineData(-0.1)]
        [InlineData(1.5)]
        public void Calibrate_ClampsToValidRange(double raw)
        {
            var profile = new ProviderCalibrationProfile("Test", Scale: 0.8, Bias: 0.1, QualityTier: 0.5);
            var result = profile.Calibrate(raw);
            result.Should().BeInRange(0.0, 1.0);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Ollama_Calibration_AdjustsConfidence()
        {
            var profile = ProviderCalibrationRegistry.GetProfile(AIProvider.Ollama);

            // Raw 0.75 from Ollama → calibrated = 0.75 * 0.80 + 0.05 = 0.65
            var calibrated = profile.Calibrate(0.75);
            calibrated.Should().BeApproximately(0.65, 0.01);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Groq_Calibration_AdjustsConfidence()
        {
            var profile = ProviderCalibrationRegistry.GetProfile(AIProvider.Groq);

            // Raw 0.80 from Groq → calibrated = 0.80 * 0.88 + 0.02 = 0.724
            var calibrated = profile.Calibrate(0.80);
            calibrated.Should().BeApproximately(0.724, 0.01);
        }
    }
}
