using System.Linq;
using Brainarr.Tests.Helpers;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Cost;
using Xunit;

namespace Brainarr.Tests.Services.Cost
{
    /// <summary>
    /// Live-audit follow-up (2026-07): ZaiCoding/ZaiGlm are deliberately absent from the
    /// pricing table (metered APIs with no confident public pricing — the honesty policy
    /// routes them through the unpriced path rather than mislabeling them free). But the
    /// unpriced branch warned on EVERY estimate call, producing ~29 identical
    /// "No pricing data available for provider ZaiCoding" warns per day on the live
    /// instance. The unpriced RESULT is by design; the per-call warn spam is not.
    /// These tests pin the corrected contract: the warn fires once per provider per
    /// process, and the honest-unpriced estimate itself is unchanged.
    /// </summary>
    [Collection("LoggingTests")] // TestLogger mutates global NLog config; serialize with other logging tests
    public class TokenCostEstimatorWarnOnceTests
    {
        private readonly Logger _logger;
        private readonly TokenCostEstimator _estimator;

        public TokenCostEstimatorWarnOnceTests()
        {
            _logger = TestLogger.Create("TokenCostEstimatorWarnOnceTests");
            TestLogger.ClearLoggedMessages();
            LoggerExtensions.ClearWarnOnceKeysForTests();
            _estimator = new TokenCostEstimator(_logger);
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Logging")]
        public void EstimateCost_UnpricedProvider_WarnsOnlyOncePerProcess()
        {
            _estimator.EstimateCost(AIProvider.ZaiCoding, "glm-4.5-air", "prompt one", 500);
            _estimator.EstimateCost(AIProvider.ZaiCoding, "glm-4.5-air", "prompt two", 500);
            _estimator.EstimateCost(AIProvider.ZaiCoding, "glm-4.5-air", "prompt three", 500);

            var warnCount = TestLogger.GetLoggedMessages()
                .Count(l => l.Contains("No pricing data available"));

            Assert.True(warnCount == 1, $"expected exactly one pricing warn, got {warnCount}");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Logging")]
        public void EstimateCost_UnpricedProviders_WarnOncePerProvider()
        {
            _estimator.EstimateCost(AIProvider.ZaiCoding, "glm-4.5-air", "prompt", 500);
            _estimator.EstimateCost(AIProvider.ZaiCoding, "glm-4.5-air", "prompt", 500);
            _estimator.EstimateCost(AIProvider.ZaiGlm, "glm-4.5", "prompt", 500);
            _estimator.EstimateCost(AIProvider.ZaiGlm, "glm-4.5", "prompt", 500);

            var logs = TestLogger.GetLoggedMessages();
            var zaiCodingWarns = logs.Count(l => l.Contains("No pricing data available") && l.Contains("ZaiCoding"));
            var zaiGlmWarns = logs.Count(l => l.Contains("No pricing data available") && l.Contains("ZaiGlm"));

            Assert.True(zaiCodingWarns == 1, $"expected one ZaiCoding warn, got {zaiCodingWarns}");
            Assert.True(zaiGlmWarns == 1, $"expected one ZaiGlm warn, got {zaiGlmWarns}");
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Logging")]
        public void EstimateCost_UnpricedProvider_StillReturnsHonestUnpricedEstimate()
        {
            // The warn-once change must not alter the honesty policy itself.
            var result = _estimator.EstimateCost(AIProvider.ZaiCoding, "glm-4.5-air", "prompt", 500);

            Assert.False(result.IsPriceKnown);
            Assert.Equal(0, result.EstimatedCost);
        }
    }
}
