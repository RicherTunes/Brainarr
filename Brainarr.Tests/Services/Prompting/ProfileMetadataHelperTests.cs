using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using Xunit;

namespace Brainarr.Tests.Services.Prompting
{
    public class ProfileMetadataHelperTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void GetString_ReturnsValue_WhenKeyExists()
        {
            var profile = new LibraryProfile();
            profile.Metadata["CollectionSize"] = "massive";

            var result = ProfileMetadataHelper.GetString(profile, "CollectionSize", "default");

            Assert.Equal("massive", result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetString_ReturnsDefault_WhenKeyMissing()
        {
            var profile = new LibraryProfile();

            var result = ProfileMetadataHelper.GetString(profile, "Missing", "fallback");

            Assert.Equal("fallback", result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetTyped_ReturnsCastValue_WhenTypeMatches()
        {
            var profile = new LibraryProfile();
            var eras = new List<string> { "Modern", "Contemporary" };
            profile.Metadata["PreferredEras"] = eras;

            var result = ProfileMetadataHelper.GetTyped<List<string>>(profile, "PreferredEras");

            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
            Assert.Equal("Modern", result[0]);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetTyped_ReturnsNull_WhenTypeMismatch()
        {
            var profile = new LibraryProfile();
            profile.Metadata["PreferredEras"] = "not a list";

            var result = ProfileMetadataHelper.GetTyped<List<string>>(profile, "PreferredEras");

            Assert.Null(result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetTyped_ReturnsNull_WhenKeyMissing()
        {
            var profile = new LibraryProfile();

            var result = ProfileMetadataHelper.GetTyped<List<string>>(profile, "Missing");

            Assert.Null(result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetDouble_ReturnsValue_WhenPresent()
        {
            var profile = new LibraryProfile();
            profile.Metadata["MonitoredRatio"] = 0.85;

            var result = ProfileMetadataHelper.GetDouble(profile, "MonitoredRatio");

            Assert.NotNull(result);
            Assert.Equal(0.85, result.Value, 3);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetDouble_ReturnsNull_WhenKeyMissing()
        {
            var profile = new LibraryProfile();

            var result = ProfileMetadataHelper.GetDouble(profile, "Missing");

            Assert.Null(result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetTopN_ReturnsFormattedString_FromDictionary()
        {
            var profile = new LibraryProfile();
            profile.Metadata["AlbumTypes"] = new Dictionary<string, int>
            {
                { "Studio", 100 },
                { "EP", 20 },
                { "Single", 10 },
                { "Compilation", 5 }
            };

            var result = ProfileMetadataHelper.GetTopN<int>(
                profile, "AlbumTypes", 3,
                kv => $"{kv.Key} ({kv.Value})");

            Assert.Equal("Studio (100), EP (20), Single (10)", result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetTopN_ReturnsEmpty_WhenKeyMissing()
        {
            var profile = new LibraryProfile();

            var result = ProfileMetadataHelper.GetTopN<int>(
                profile, "Missing", 3,
                kv => kv.Key);

            Assert.Equal(string.Empty, result);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void GetTopN_AppliesFilter_WhenProvided()
        {
            var profile = new LibraryProfile();
            profile.Metadata["GenreDistribution"] = new Dictionary<string, double>
            {
                { "Rock", 40.0 },
                { "Rock_significance", 3.0 },
                { "Jazz", 20.0 },
                { "Jazz_significance", 2.0 }
            };

            var result = ProfileMetadataHelper.GetTopN<double>(
                profile, "GenreDistribution", 5,
                kv => $"{kv.Key} ({kv.Value:F1}%)",
                kv => !kv.Key.EndsWith("_significance", System.StringComparison.OrdinalIgnoreCase));

            Assert.Contains("Rock (40.0%)", result);
            Assert.Contains("Jazz (20.0%)", result);
            Assert.DoesNotContain("significance", result);
        }
    }
}
