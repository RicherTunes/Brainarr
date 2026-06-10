using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;
using Xunit;

namespace Brainarr.Tests.Logging
{
    /// <summary>
    /// Pins the exact text of log emissions that were observed misleading in live runs:
    /// 1. DuplicateFilterService claimed library-duplicate filtering ran "prior to validation",
    ///    but RecommendationPipeline invokes it AFTER ValidateAsync completes (live log order:
    ///    "Validation complete" 20:46:53 then this line 20:46:58), and before enrichment.
    /// 2. The strict-only style-selection lines rendered all slugs inside ONE quote pair
    ///    (selected=["lofi-hip-hop, alternative-rock"]) because the joined slug string was
    ///    passed as a single structured-template capture, which the host's NLog 5.x quotes
    ///    as one token — misreading as a single slug.
    /// </summary>
    public class LogMessageAccuracyTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void LibraryDuplicatesMessage_states_post_validation_pipeline_position()
        {
            var message = DuplicateFilterService.FormatLibraryDuplicatesMessage(3);

            message.Should().Be(
                "Filtered out 3 recommendation(s) already present in the library after validation (before enrichment)");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void ArtistStrictOnlyMessage_renders_each_slug_distinctly()
        {
            var message = DefaultSamplingService.FormatStrictOnlyLogMessage(
                "Artist", 2, new[] { "lofi-hip-hop", "alternative-rock" });

            message.Should().Be(
                "Artist style matches remain strict-only: count=2, selected=[lofi-hip-hop, alternative-rock]");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void AlbumStrictOnlyMessage_renders_each_slug_distinctly()
        {
            var message = DefaultSamplingService.FormatStrictOnlyLogMessage(
                "Album", 2, new[] { "lofi-hip-hop", "alternative-rock" });

            message.Should().Be(
                "Album style matches remain strict-only: count=2, selected=[lofi-hip-hop, alternative-rock]");
        }
    }
}
