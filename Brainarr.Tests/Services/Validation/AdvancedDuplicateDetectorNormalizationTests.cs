using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation;
using NzbDrone.Core.Music;
using Xunit;
using Brainarr.Tests.Helpers;

namespace Brainarr.Tests.Services.Validation
{
    /// <summary>
    /// #68: the artist-name normalizer's "ft"/"feat" -> "featuring" step used a plain substring
    /// Replace, which corrupted any name containing those letters ("Daft Punk" -> "dafeaturing punk",
    /// "Lifton" -> "lifeaturingon"), distorting dedup similarity. Now word-bounded.
    /// </summary>
    public class AdvancedDuplicateDetectorNormalizationTests
    {
        private static AdvancedDuplicateDetector Create() =>
            new AdvancedDuplicateDetector(TestLogger.CreateNullLogger(), Mock.Of<IArtistService>(), Mock.Of<IAlbumService>());

        [Theory]
        [InlineData("Daft Punk", "daft punk")]            // "ft" inside a word must NOT become "featuring"
        [InlineData("Lifton", "lifton")]
        [InlineData("Soft Cell", "soft cell")]
        [InlineData("Shaft", "shaft")]
        [InlineData("Defeat", "defeat")]                  // "feat" inside a word must NOT become "featuring"
        [InlineData("Foo Fighters", "foo fighters")]
        public void NormalizeArtistName_DoesNotCorrupt_SubstringMatches(string input, string expected)
        {
            Create().NormalizeArtistName(input).Should().Be(expected);
        }

        [Theory]
        [InlineData("Jay-Z ft Alicia Keys", "jay z featuring alicia keys")] // standalone "ft" still normalizes
        [InlineData("Artist feat Other", "artist featuring other")]          // standalone "feat" still normalizes
        public void NormalizeArtistName_StillNormalizes_StandaloneFeaturingAbbreviations(string input, string expected)
        {
            Create().NormalizeArtistName(input).Should().Be(expected);
        }

        [Fact]
        public void NormalizeArtistName_UnifiesFeaturingVariants_ForDedup()
        {
            var d = Create();
            var ft = d.NormalizeArtistName("Jay-Z ft Alicia Keys");
            var feat = d.NormalizeArtistName("Jay-Z feat Alicia Keys");
            var full = d.NormalizeArtistName("Jay-Z featuring Alicia Keys");

            ft.Should().Be(feat);
            feat.Should().Be(full, "all three featuring spellings must normalize identically so they dedup");
        }
    }
}
