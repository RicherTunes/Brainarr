using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    /// <summary>
    /// Regression guard for the adversarial-review finding: the sanitizer rebuilds every
    /// Recommendation field-by-field and runs BEFORE the safety gate, so it must preserve
    /// <see cref="Recommendation.ConfidenceProvided"/>. Dropping it reset the flag to its default
    /// (true) and silently re-introduced the fabricated-confidence floor cliff in the real pipeline,
    /// even though the gate/parser unit tests passed. This test exercises the integration seam.
    /// </summary>
    public class RecommendationSanitizerProvenanceTests
    {
        [Fact]
        public void Sanitize_PreservesConfidenceProvided_False()
        {
            var sanitizer = new RecommendationSanitizer(LogManager.GetCurrentClassLogger());
            var input = new List<Recommendation>
            {
                new Recommendation { Artist = "A", Album = "B", Confidence = 0.85, ConfidenceProvided = false }
            };

            var result = sanitizer.SanitizeRecommendations(input);

            result.Should().ContainSingle();
            result[0].ConfidenceProvided.Should().BeFalse(
                "the sanitizer must not reset confidence provenance before the safety gate");
        }

        [Fact]
        public void Sanitize_PreservesConfidenceProvided_True()
        {
            var sanitizer = new RecommendationSanitizer(LogManager.GetCurrentClassLogger());
            var input = new List<Recommendation>
            {
                new Recommendation { Artist = "C", Album = "D", Confidence = 0.9, ConfidenceProvided = true }
            };

            var result = sanitizer.SanitizeRecommendations(input);

            result.Should().ContainSingle();
            result[0].ConfidenceProvided.Should().BeTrue();
        }
    }
}
