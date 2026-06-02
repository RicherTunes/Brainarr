using System.Collections.Generic;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class RecommendationSanitizerTests
    {
        [Fact]
        public void SanitizeRecommendations_filters_malicious_and_clamps_confidence()
        {
            var logger = LogManager.GetLogger("test");
            var sanitizer = new RecommendationSanitizer(logger);

            var list = new List<Recommendation>
            {
                new Recommendation { Artist = "<script>alert(1)</script>", Album = "A" }, // malicious -> filtered
                new Recommendation { Artist = " Valid ", Album = "  ", Confidence = 2.0, Reason = "ok" }, // clamped + trims
            };

            var result = sanitizer.SanitizeRecommendations(list);
            result.Count.Should().Be(1);
            result[0].Artist.Should().Be("Valid");
            result[0].Confidence.Should().BeLessThanOrEqualTo(1.0);
        }

        [Fact]
        public void IsValidRecommendation_checks_required_and_lengths()
        {
            var logger = LogManager.GetLogger("test");
            var sanitizer = new RecommendationSanitizer(logger);

            sanitizer.IsValidRecommendation(null).Should().BeFalse();
            sanitizer.IsValidRecommendation(new Recommendation { Artist = "" }).Should().BeFalse();
            sanitizer.IsValidRecommendation(new Recommendation { Artist = "A", Confidence = 0.5 }).Should().BeTrue();

            var longArtist = new string('a', 501);
            sanitizer.IsValidRecommendation(new Recommendation { Artist = longArtist }).Should().BeFalse();
        }

        [Fact]
        public void SanitizeString_removes_xss_and_paths_and_normalizes_but_keeps_plain_words()
        {
            var logger = LogManager.GetLogger("test");
            var sanitizer = new RecommendationSanitizer(logger);

            var input = "  <script>alert(1)</script> AC/DC & Friends  ../etc/passwd  Selected Works  ";
            var outp = sanitizer.SanitizeString(input);

            outp.Should().NotContain("<script>");
            outp.Should().NotContain("../");
            outp.Should().NotContain("etc/passwd");        // path traversal stripped
            outp.Should().Contain("AC/DC & Friends");      // ampersand preserved RAW, not HTML-encoded (main's MusicBrainz fix: '&'→'&amp;' corrupted "&"-in-name lookups)
            outp.Should().NotContain("&amp;");             // must not HTML-encode
            outp.Should().Contain("Selected Works");       // plain words preserved (#589: no SQL-keyword false-positive stripping)
            outp.Should().Be(outp.Trim());
        }

        [Fact]
        public void SanitizeRecommendations_preserves_mbids_and_year()
        {
            var logger = LogManager.GetLogger("test");
            var sanitizer = new RecommendationSanitizer(logger);
            var rec = new Recommendation
            {
                Artist = "Artist",
                Album = "Album",
                ArtistMusicBrainzId = "abc-123",
                AlbumMusicBrainzId = "def-456",
                Year = 1999,
                Confidence = 0.9
            };
            var result = sanitizer.SanitizeRecommendations(new System.Collections.Generic.List<Recommendation> { rec });
            result.Should().HaveCount(1);
            result[0].ArtistMusicBrainzId.Should().Be("abc-123");
            result[0].AlbumMusicBrainzId.Should().Be("def-456");
            result[0].Year.Should().Be(1999);
        }

        // Free-text music names are never concatenated into raw SQL (MusicBrainz uses
        // parameterized HTTP, Lidarr's import uses a parameterized DB), so SQL-keyword
        // heuristics produce only false positives here — real artists/albums whose names
        // happen to contain SQL keywords must NOT be dropped or mangled.
        [Theory]
        [InlineData("Union", "The Union")]
        [InlineData("Drop Nineteens", "Delaware")]
        [InlineData("Insert Coin", "Insert Coin")]
        [InlineData("Update", "Update")]
        [InlineData("DELETE", "Delete Yourself!")]
        public void SanitizeRecommendations_keeps_real_names_containing_sql_keywords(string artist, string album)
        {
            var logger = LogManager.GetLogger("test");
            var sanitizer = new RecommendationSanitizer(logger);

            var result = sanitizer.SanitizeRecommendations(new List<Recommendation>
            {
                new Recommendation { Artist = artist, Album = album, Confidence = 0.8 }
            });

            result.Should().HaveCount(1, $"'{artist} - {album}' is a real name, not a SQL-injection attempt");
            result[0].Artist.Should().Be(artist);
            result[0].Album.Should().Be(album);
        }

        [Fact]
        public void SanitizeString_preserves_words_that_look_like_sql_keywords()
        {
            var logger = LogManager.GetLogger("test");
            var sanitizer = new RecommendationSanitizer(logger);

            sanitizer.SanitizeString("Drop Nineteens").Should().Be("Drop Nineteens");
            sanitizer.SanitizeString("Insert Coin").Should().Be("Insert Coin");
        }

        // Genuinely dangerous content that renders in Lidarr's web UI or touches the
        // filesystem must still be neutralized — the threat-model correction only drops
        // the SQL-keyword heuristic, not these.
        [Fact]
        public void SanitizeRecommendations_still_filters_xss_and_path_traversal()
        {
            var logger = LogManager.GetLogger("test");
            var sanitizer = new RecommendationSanitizer(logger);

            var result = sanitizer.SanitizeRecommendations(new List<Recommendation>
            {
                new Recommendation { Artist = "<script>alert(1)</script>", Album = "X", Confidence = 0.9 },
                new Recommendation { Artist = "../../etc/passwd", Album = "Y", Confidence = 0.9 },
                new Recommendation { Artist = "Real Artist", Album = "Real Album", Confidence = 0.9 },
            });

            result.Should().HaveCount(1);
            result[0].Artist.Should().Be("Real Artist");
        }
    }
}
