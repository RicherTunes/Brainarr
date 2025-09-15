using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Validation;
using Xunit;

namespace Brainarr.Tests.Services.Validation
{
    [Trait("Category", "Unit")]
    public class RecommendationValidatorTests
    {
        private static RecommendationValidator Create()
        {
            var logger = LogManager.GetCurrentClassLogger();
            // pass nulls for optional collaborators; code handles null checks
            return new RecommendationValidator(logger, null, null, null, null, null);
        }

        [Fact]
        public async Task ValidateRecommendationAsync_MissingFields_IsInvalid()
        {
            var v = Create();
            var res = await v.ValidateRecommendationAsync(new Recommendation { Artist = "", Album = "" });
            res.IsValid.Should().BeFalse();
            res.Score.Should().Be(0.0);
        }

        [Fact]
        public async Task ValidateRecommendationAsync_Valid_IsValid()
        {
            var v = Create();
            var res = await v.ValidateRecommendationAsync(new Recommendation { Artist = "Radiohead", Album = "OK Computer", Year = 1997 });
            res.IsValid.Should().BeTrue();
            res.Score.Should().BeGreaterThan(0.69);
        }

        [Fact]
        public async Task ValidateRecommendationAsync_ImpossibleYear_IsInvalid()
        {
            var v = Create();
            var res = await v.ValidateRecommendationAsync(new Recommendation { Artist = "The Beatles", Album = "Revolver", Year = 1800 });
            res.IsValid.Should().BeFalse();
        }

        [Fact]
        public async Task FilterValidRecommendationsAsync_Filters_By_Score()
        {
            var v = Create();
            var list = new List<Recommendation>
            {
                new Recommendation { Artist = "A", Album = "A", Year = 1990 },
                new Recommendation { Artist = "", Album = "B" },
                new Recommendation { Artist = "C", Album = "C", Year = 2050 },
            };
            var valid = await v.FilterValidRecommendationsAsync(list);
            valid.Should().ContainSingle().Which.Artist.Should().Be("A");
        }
    }
}
