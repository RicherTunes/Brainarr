using System;
using System.Collections.Generic;
using System.Reflection;
using Brainarr.Tests.Services.Core;
using Moq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// Regression coverage for <c>IterativeRecommendationStrategy.BuildExistingAlbumsSet</c>, which
    /// built the top-up dedup baseline by reading <c>album.ArtistMetadata.Value.Name</c> for every
    /// album. <see cref="Album.ArtistMetadata"/> is LazyLoaded on the albums <c>GetAllAlbums()</c>
    /// returns, so that per-album <c>.Value</c> access is the same per-row DB round trip / OOM hazard
    /// as <see cref="Album.ArtistId"/>. It now resolves the artist name from the already-materialized
    /// artists list via the plain <c>ArtistMetadataId</c> column.
    /// </summary>
    [Trait("Category", "Performance")]
    public class IterativeRecommendationStrategyNPlusOneTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [Fact]
        public void BuildExistingAlbumsSet_ResolvesArtistNames_WithoutPerAlbumMetadataLazyLoad()
        {
            var counter = new LazyLoadCounter();
            var artists = new List<Artist>();
            var albums = new List<Album>();

            for (var i = 1; i <= 50; i++)
            {
                var artist = new Artist
                {
                    Id = i,
                    ArtistMetadataId = i,
                    Name = $"Artist {i}"
                };
                artists.Add(artist);

                albums.Add(new Album
                {
                    Id = i,
                    Title = $"Album {i}",
                    ArtistMetadataId = i,
                    ArtistMetadata = new RecordingArtistMetadataLazyLoaded(
                        new ArtistMetadata { Name = $"Artist {i}" }, counter)
                });
            }

            var promptBuilder = new Mock<ILibraryAwarePromptBuilder>();
            var strategy = new IterativeRecommendationStrategy(Logger, promptBuilder.Object);

            var method = typeof(IterativeRecommendationStrategy).GetMethod(
                "BuildExistingAlbumsSet", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);

            var result = (HashSet<string>)method.Invoke(strategy, new object[] { albums, artists });

            // 0 => no album.ArtistMetadata.Value deref; the dedup baseline was built purely from the
            // eager artists list keyed on ArtistMetadataId.
            Assert.Equal(0, counter.Count);
            // Behavior preserved: one dedup key per album whose artist resolves.
            Assert.Equal(50, result.Count);
        }
    }
}
