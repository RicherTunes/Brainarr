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

        [Fact]
        public void Sanitize_PreservesAllNonTransformedFields_ViaWithCopy()
        {
            // #64: the field-by-field rebuild silently DROPPED ReleaseYear/Source/Provider/
            // MusicBrainzId/SpotifyId (reset to default). The `with`-copy must carry them through.
            var sanitizer = new RecommendationSanitizer(LogManager.GetCurrentClassLogger());
            var input = new List<Recommendation>
            {
                new Recommendation
                {
                    Artist = "Artist", Album = "Album", Confidence = 0.8,
                    Year = 1999, ReleaseYear = 2001, Source = "src", Provider = "openai",
                    MusicBrainzId = "mbid-rg", ArtistMusicBrainzId = "mbid-artist",
                    AlbumMusicBrainzId = "mbid-album", SpotifyId = "spotify-1"
                }
            };

            var r = sanitizer.SanitizeRecommendations(input).Single();

            r.Year.Should().Be(1999);
            r.ReleaseYear.Should().Be(2001);
            r.Source.Should().Be("src");
            r.Provider.Should().Be("openai");
            r.MusicBrainzId.Should().Be("mbid-rg");
            r.ArtistMusicBrainzId.Should().Be("mbid-artist");
            r.AlbumMusicBrainzId.Should().Be("mbid-album");
            r.SpotifyId.Should().Be("spotify-1");
        }
    }
}
