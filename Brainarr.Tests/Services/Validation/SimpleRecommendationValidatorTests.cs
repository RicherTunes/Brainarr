using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Validation
{
    [Trait("Category", "Unit")]
    public class SimpleRecommendationValidatorTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();
        private readonly Mock<IArtistService> _artistService = new();
        private readonly Mock<IAlbumService> _albumService = new();

        private SimpleRecommendationValidator CreateValidator(
            IList<Artist> artists = null,
            IList<Album> albums = null)
        {
            _artistService.Setup(x => x.GetAllArtists()).Returns((List<Artist>)(artists ?? new List<Artist>()));
            _albumService.Setup(x => x.GetAllAlbums()).Returns((List<Album>)(albums ?? new List<Album>()));
            return new SimpleRecommendationValidator(_logger, _artistService.Object, _albumService.Object);
        }

        // ---- ctor ----

        [Fact]
        public void Ctor_NullLogger_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new SimpleRecommendationValidator(null!, _artistService.Object, _albumService.Object));
        }

        [Fact]
        public void Ctor_NullArtistService_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new SimpleRecommendationValidator(_logger, null!, _albumService.Object));
        }

        [Fact]
        public void Ctor_NullAlbumService_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new SimpleRecommendationValidator(_logger, _artistService.Object, null!));
        }

        // ---- ValidateRecommendationAsync: format ----

        [Fact]
        public async Task Validate_EmptyArtist_IsCriticalAndInvalid()
        {
            var v = CreateValidator();
            var rec = new Recommendation { Artist = "", Album = "Some Album" };
            var result = await v.ValidateRecommendationAsync(rec);

            result.IsValid.Should().BeFalse();
            result.Score.Should().Be(0.0);
            result.Findings.Should().Contain(f =>
                f.CheckType == ValidationCheckType.FormatValidation &&
                f.Severity == ValidationSeverity.Critical);
        }

        [Fact]
        public async Task Validate_EmptyAlbum_IsCriticalAndInvalid()
        {
            var v = CreateValidator();
            var rec = new Recommendation { Artist = "Some Artist", Album = "" };
            var result = await v.ValidateRecommendationAsync(rec);

            result.IsValid.Should().BeFalse();
            result.Findings.Should().Contain(f =>
                f.CheckType == ValidationCheckType.FormatValidation);
        }

        [Fact]
        public async Task Validate_GoodRecommendation_IsValidWithFullScore()
        {
            var v = CreateValidator();
            var rec = new Recommendation { Artist = "Pink Floyd", Album = "The Wall", Year = 1979 };
            var result = await v.ValidateRecommendationAsync(rec);

            result.IsValid.Should().BeTrue();
            result.Score.Should().Be(1.0);
            result.Findings.Should().BeEmpty();
        }

        // ---- ValidateRecommendationAsync: dates ----

        [Fact]
        public async Task Validate_AncientYear_FailsReleaseDateCheck()
        {
            var v = CreateValidator();
            var rec = new Recommendation { Artist = "X", Album = "Y", Year = 1800 };
            var result = await v.ValidateRecommendationAsync(rec);

            result.Findings.Should().Contain(f =>
                f.CheckType == ValidationCheckType.ReleaseDateValidation);
            result.IsValid.Should().BeFalse(); // critical severity
        }

        [Fact]
        public async Task Validate_FarFutureYear_FailsReleaseDateCheck()
        {
            var v = CreateValidator();
            var rec = new Recommendation { Artist = "X", Album = "Y", Year = 9999 };
            var result = await v.ValidateRecommendationAsync(rec);

            result.Findings.Should().Contain(f => f.CheckType == ValidationCheckType.ReleaseDateValidation);
        }

        // ---- DetectHallucinationAsync ----

        [Fact]
        public async Task DetectHallucination_AIPattern_RaisesConfidence()
        {
            var v = CreateValidator();
            var rec = new Recommendation { Artist = "X", Album = "The Multiverse Sessions" };
            var h = await v.DetectHallucinationAsync(rec);

            h.HallucinationConfidence.Should().BeGreaterThan(0.7);
            h.IsLikelyHallucination.Should().BeTrue();
            h.DetectedPatterns.Should().Contain(p =>
                p.PatternType == HallucinationPatternType.NonExistentAlbum);
        }

        [Fact]
        public async Task DetectHallucination_ImpossibleDate_RaisesConfidence()
        {
            var v = CreateValidator();
            var rec = new Recommendation { Artist = "X", Album = "Real Album", Year = 1500 };
            var h = await v.DetectHallucinationAsync(rec);

            h.DetectedPatterns.Should().Contain(p =>
                p.PatternType == HallucinationPatternType.ImpossibleReleaseDate);
        }

        [Fact]
        public async Task DetectHallucination_CleanAlbum_NoPatterns()
        {
            var v = CreateValidator();
            var rec = new Recommendation { Artist = "X", Album = "Clean Album", Year = 2010 };
            var h = await v.DetectHallucinationAsync(rec);

            h.HallucinationConfidence.Should().Be(0.0);
            h.IsLikelyHallucination.Should().BeFalse();
            h.DetectedPatterns.Should().BeEmpty();
        }

        [Fact]
        public async Task DetectHallucination_ConfidenceClampedAtOne()
        {
            var v = CreateValidator();
            // Both AI pattern (0.8) and impossible date (0.9) → clamp to 1.0
            var rec = new Recommendation { Artist = "X", Album = "alternate timeline", Year = 1500 };
            var h = await v.DetectHallucinationAsync(rec);

            h.HallucinationConfidence.Should().BeLessOrEqualTo(1.0);
            h.HallucinationConfidence.Should().BeGreaterOrEqualTo(0.7);
        }

        // ---- IsAlreadyInLibraryAsync / duplicate detection ----

        [Fact]
        public async Task IsAlreadyInLibrary_ExactMatch_ReturnsTrue()
        {
            var artists = new List<Artist> { new Artist { Id = 1, Name = "Pink Floyd" } };
            var albums = new List<Album> { new Album { Id = 10, ArtistId = 1, Title = "The Wall" } };
            var v = CreateValidator(artists, albums);

            var rec = new Recommendation { Artist = "Pink Floyd", Album = "The Wall" };
            (await v.IsAlreadyInLibraryAsync(rec)).Should().BeTrue();
        }

        [Fact]
        public async Task IsAlreadyInLibrary_NormalizesThePrefix()
        {
            var artists = new List<Artist> { new Artist { Id = 2, Name = "Beatles" } };
            var albums = new List<Album> { new Album { Id = 20, ArtistId = 2, Title = "Abbey Road" } };
            var v = CreateValidator(artists, albums);

            var rec = new Recommendation { Artist = "The Beatles", Album = "Abbey Road" };
            (await v.IsAlreadyInLibraryAsync(rec)).Should().BeTrue();
        }

        [Fact]
        public async Task IsAlreadyInLibrary_StripsRemasterEdition()
        {
            var artists = new List<Artist> { new Artist { Id = 3, Name = "Led Zeppelin" } };
            var albums = new List<Album> { new Album { Id = 30, ArtistId = 3, Title = "IV" } };
            var v = CreateValidator(artists, albums);

            var rec = new Recommendation { Artist = "Led Zeppelin", Album = "IV (Deluxe Remaster)" };
            (await v.IsAlreadyInLibraryAsync(rec)).Should().BeTrue();
        }

        [Fact]
        public async Task IsAlreadyInLibrary_DifferentArtist_ReturnsFalse()
        {
            var artists = new List<Artist> { new Artist { Id = 4, Name = "Miles Davis" } };
            var albums = new List<Album> { new Album { Id = 40, ArtistId = 4, Title = "Kind of Blue" } };
            var v = CreateValidator(artists, albums);

            var rec = new Recommendation { Artist = "John Coltrane", Album = "Giant Steps" };
            (await v.IsAlreadyInLibraryAsync(rec)).Should().BeFalse();
        }

        [Fact]
        public async Task IsAlreadyInLibrary_ServiceThrows_ReturnsFalseAndDoesNotPropagate()
        {
            _artistService.Setup(x => x.GetAllArtists()).Throws(new InvalidOperationException("boom"));
            _albumService.Setup(x => x.GetAllAlbums()).Returns(new List<Album>());
            var v = new SimpleRecommendationValidator(_logger, _artistService.Object, _albumService.Object);

            var rec = new Recommendation { Artist = "X", Album = "Y" };
            (await v.IsAlreadyInLibraryAsync(rec)).Should().BeFalse();
        }

        // ---- ValidateRecommendationsAsync / FilterValidRecommendationsAsync ----

        [Fact]
        public async Task ValidateRecommendations_BatchProducesOneResultPerInput()
        {
            var v = CreateValidator();
            var recs = new List<Recommendation>
            {
                new Recommendation { Artist = "A", Album = "1" },
                new Recommendation { Artist = "", Album = "2" },
                new Recommendation { Artist = "C", Album = "3", Year = 2020 },
            };

            var results = await v.ValidateRecommendationsAsync(recs);
            results.Should().HaveCount(3);
            results[1].IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task FilterValidRecommendations_KeepsOnlyValidAboveThreshold()
        {
            var v = CreateValidator();
            var recs = new List<Recommendation>
            {
                new Recommendation { Artist = "Good", Album = "Real" },
                new Recommendation { Artist = "", Album = "Empty" },
                new Recommendation { Artist = "Bad", Album = "alternate timeline", Year = 1500 },
            };

            var kept = await v.FilterValidRecommendationsAsync(recs);
            kept.Select(r => r.Artist).Should().Contain("Good");
            kept.Should().NotContain(r => r.Artist == "");
            kept.Should().NotContain(r => r.Artist == "Bad");
        }
    }
}
