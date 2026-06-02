using FluentAssertions;
using NLog;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;
using Xunit;

namespace Brainarr.Tests.Services.Providers.Parsing
{
    /// <summary>
    /// F2 (live-usage): GLM responses routinely truncate at max_tokens, so the primary JSON parse
    /// throws and "Failed to parse recommendations JSON" was logged at WARN on every iteration — even
    /// though SalvageObjectsFromText recovers the complete objects right after and the run succeeds.
    /// The WARN was premature/misleading. Correct behavior: WARN only on TOTAL loss (0 recovered);
    /// when fallback/salvage recovers >=1, log at Debug. These tests pin the log LEVEL, isolated to a
    /// local LogFactory so they don't depend on global NLog config.
    /// </summary>
    public class RecommendationJsonParserSalvageLogLevelTests
    {
        private static (Logger logger, MemoryTarget warnTarget, LogFactory factory) WarnCapture(string name)
        {
            var warnTarget = new MemoryTarget { Layout = "${message}" };
            var config = new LoggingConfiguration();
            // Capture WARN-and-above ONLY, so an empty target == "no WARN emitted".
            config.AddRule(LogLevel.Warn, LogLevel.Fatal, warnTarget, name);
            var factory = new LogFactory(config);
            return (factory.GetLogger(name), warnTarget, factory);
        }

        [Fact]
        public void TruncatedPayload_SalvageRecovers_DoesNotWarn()
        {
            // A real GLM shape: ```json fence, array opened, complete items, cut off mid-object at the
            // token cap. Primary parse throws; salvage recovers the complete objects.
            var content = "```json\n[\n" +
                          "  { \"artist\": \"Nujabes\", \"album\": \"Modal Soul\", \"confidence\": 0.95 },\n" +
                          "  { \"artist\": \"J Dilla\", \"album\": \"Donuts\", \"confidence\": 0.9 },\n" +
                          "  { \"artist\": \"MF DO";

            var (logger, warnTarget, factory) = WarnCapture("salvage-recovers");
            using (factory)
            {
                var list = RecommendationJsonParser.Parse(content, logger);

                list.Should().HaveCount(2, "salvage must recover the complete objects before the cut");
                warnTarget.Logs.Should().BeEmpty(
                    "when salvage recovers recommendations the primary-parse failure is not a WARN-worthy event");
            }
        }

        [Fact]
        public void TotalLoss_NothingRecovered_StillWarns()
        {
            // No salvageable objects at all → genuine total parse failure → WARN is still appropriate
            // (guards against over-correcting the fix into silently swallowing real failures).
            var content = "this is not json at all, just prose with no objects";

            var (logger, warnTarget, factory) = WarnCapture("total-loss");
            using (factory)
            {
                var list = RecommendationJsonParser.Parse(content, logger);

                list.Should().BeEmpty();
                warnTarget.Logs.Should().NotBeEmpty("a total parse failure with zero recovery must still WARN");
            }
        }
    }
}
