using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class LibraryContextBuilderTests
    {
        [Fact]
        public void BuildProfile_returns_profile_with_top_artists_and_genres()
        {
            var logger = LogManager.GetLogger("test");
            var builder = new LibraryContextBuilder(logger);

            var artists = new List<Artist>
            {
                new Artist { Id = 1, Name = "A", Added = DateTime.UtcNow.AddDays(-1) },
                new Artist { Id = 2, Name = "B", Added = DateTime.UtcNow },
            };
            var albums = new List<Album>
            {
                new Album { Id = 10, ArtistId = 1, Title = "A1" },
                new Album { Id = 11, ArtistId = 1, Title = "A2" },
                new Album { Id = 12, ArtistId = 2, Title = "B1" },
            };

            var artistSvc = new Mock<IArtistService>();
            artistSvc.Setup(s => s.GetAllArtists()).Returns(artists);
            var albumSvc = new Mock<IAlbumService>();
            albumSvc.Setup(s => s.GetAllAlbums()).Returns(albums);

            var profile = builder.BuildProfile(artistSvc.Object, albumSvc.Object);

            profile.TotalArtists.Should().Be(2);
            profile.TotalAlbums.Should().Be(3);
            profile.TopArtists.Should().Contain(new[] { "A", "B" });
            profile.TopGenres.Count.Should().BeGreaterThan(0);
            profile.RecentlyAdded.Should().Contain("B");
        }

        [Fact]
        public void GenerateFingerprint_is_deterministic_for_same_profile()
        {
            var logger = LogManager.GetLogger("test");
            var builder = new LibraryContextBuilder(logger);
            var profile = new LibraryProfile
            {
                TotalArtists = 2,
                TotalAlbums = 3,
                TopArtists = new List<string> { "A", "B" },
                TopGenres = new Dictionary<string, int> { { "Rock", 2 }, { "Electronic", 1 } },
                RecentlyAdded = new List<string> { "B" }
            };

            var fp1 = builder.GenerateFingerprint(profile);
            var fp2 = builder.GenerateFingerprint(profile);
            fp1.Should().Be(fp2);
            fp1.Should().Contain("2_");
        }
    }
}

