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

        // Capture DEBUG-and-above, rendering any attached exception. The fence-noise we are killing is
        // a Debug line ("Primary recommendations-JSON parse failed but recovered N item(s)...") that
        // carries the full JsonReaderException render — invisible to WarnCapture but spammed on every
        // GLM run because the live host logs at Debug. An empty target == "primary parse did not fail".
        private static (Logger logger, MemoryTarget debugTarget, LogFactory factory) DebugCapture(string name)
        {
            var debugTarget = new MemoryTarget { Layout = "${message}${onexception:|${exception:format=Type,Message}}" };
            var config = new LoggingConfiguration();
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, debugTarget, name);
            var factory = new LogFactory(config);
            return (factory.GetLogger(name), debugTarget, factory);
        }

        [Fact]
        public void CompleteFencedPayload_ParsesCleanly_NoPrimaryParseFailureNoise()
        {
            // The dominant live GLM shape: a COMPLETE, well-formed JSON array wrapped in a ```json fence
            // (response did NOT truncate). Before the fix the strict parse threw JsonReaderException on
            // the leading backtick and the catch logged a Debug line carrying the full exception render
            // on every successful run (~5x/session). After the fix the fence is stripped BEFORE the
            // strict parse, so the strict parse succeeds: items are extracted AND no parse-failure /
            // exception line is logged at all.
            var content = "```json\n[\n" +
                          "  { \"artist\": \"Nujabes\", \"album\": \"Modal Soul\", \"confidence\": 0.95 },\n" +
                          "  { \"artist\": \"J Dilla\", \"album\": \"Donuts\", \"confidence\": 0.9 }\n" +
                          "]\n```";

            var (logger, debugTarget, factory) = DebugCapture("complete-fence");
            using (factory)
            {
                var list = RecommendationJsonParser.Parse(content, logger);

                list.Should().HaveCount(2, "the fenced array is complete and must parse fully");
                debugTarget.Logs.Should().NotContain(
                    l => l.Contains("Primary recommendations-JSON parse failed"),
                    "stripping the fence before the strict parse must make the primary parse succeed");
                debugTarget.Logs.Should().NotContain(
                    l => l.Contains("JsonReaderException"),
                    "no JsonReaderException should be captured/rendered when the fence is stripped up front");
            }
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
