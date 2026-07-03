using System;
using FluentAssertions;
using Brainarr.Tests.Helpers;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Cost;
using Xunit;

namespace Brainarr.Tests.Services.Cost
{
    /// <summary>
    /// Feature A2 (cost-visibility action): the pricing table silently defaulted any
    /// unrecognized model to $0.001/1K tokens — a fabricated, confidently-wrong dollar
    /// figure. These tests pin the honesty fix: unknown/unpriced models surface as such
    /// (IsPriceKnown = false, EstimatedCost = 0, no guessed number) instead of a made-up
    /// price, while known current models still price correctly and local/free providers
    /// still report a real (not "unknown") $0.
    /// </summary>
    [Trait("Category", "Unit")]
    public class TokenCostEstimatorPricingHonestyTests
    {
        private readonly Logger _logger;
        private readonly TokenCostEstimator _estimator;

        public TokenCostEstimatorPricingHonestyTests()
        {
            _logger = TestLogger.CreateNullLogger();
            _estimator = new TokenCostEstimator(_logger);
        }

        [Theory]
        [InlineData(AIProvider.OpenAI, "gpt-99-does-not-exist-2099")]
        [InlineData(AIProvider.Anthropic, "claude-99-future-model")]
        [InlineData(AIProvider.OpenRouter, "some-vendor/some-brand-new-model")]
        public void EstimateCost_WithUnknownModel_SurfacesUnpriced_NotFabricatedNumber(AIProvider provider, string model)
        {
            // Act
            var result = _estimator.EstimateCost(provider, model, "test prompt", 500);

            // Assert: no confidently-wrong dollar figure. Cost is exactly zero and the
            // caller can tell (IsPriceKnown) that zero means "unknown", not "free".
            result.IsPriceKnown.Should().BeFalse();
            result.EstimatedCost.Should().Be(0m);
            result.CostBreakdown.Should().ContainAny("unknown", "not available", "not estimated");
        }

        [Theory]
        [InlineData(AIProvider.OpenAI, "gpt-4o")]
        [InlineData(AIProvider.OpenAI, "gpt-4o-mini")]
        [InlineData(AIProvider.Anthropic, "claude-3-5-sonnet-20241022")]
        [InlineData(AIProvider.Anthropic, "claude-3-5-haiku-20241022")]
        public void EstimateCost_WithKnownCurrentModel_ReturnsPricedEstimate(AIProvider provider, string model)
        {
            // Act
            var result = _estimator.EstimateCost(provider, model, "test prompt with some content", 500);

            // Assert: the refreshed pricing table recognizes current-generation models
            // and returns a real, non-zero, confidently-priced estimate.
            result.IsPriceKnown.Should().BeTrue();
            result.EstimatedCost.Should().BeGreaterThan(0m);
        }

        [Theory]
        [InlineData(AIProvider.Ollama)]
        [InlineData(AIProvider.LMStudio)]
        public void EstimateCost_WithLocalProvider_IsPriceKnownTrue_AndZeroCost(AIProvider localProvider)
        {
            // Act
            var result = _estimator.EstimateCost(localProvider, "whatever-local-model", "test prompt", 500);

            // Assert: local providers are a KNOWN $0 (not an unknown/unpriced model) —
            // the panel must be able to distinguish "genuinely free" from "we don't know".
            result.IsPriceKnown.Should().BeTrue("local providers have known, real $0 pricing");
            result.EstimatedCost.Should().Be(0m);
        }

        [Fact]
        public void EstimateCost_OpenRouterUnknownModel_DoesNotFallBackToGenericPrice()
        {
            // OpenRouter proxies wildly different per-model prices; a single blanket
            // "default" price is exactly the fabricated-number failure mode this fix
            // targets. Assert there's no silent catch-all left for it.
            var result = _estimator.EstimateCost(AIProvider.OpenRouter, "totally-unmapped-model", "prompt", 500);

            result.IsPriceKnown.Should().BeFalse();
            result.EstimatedCost.Should().Be(0m);
        }

        [Fact]
        public void TrackUsage_WithExplicitTokenCounts_ComputesCostWithoutRetokenizingText()
        {
            // RecommendationGenerator has precise prompt/completion token estimates already
            // (from the prompt builder and EstimateCompletionTokens respectively) — it should
            // not need to fabricate a "response" string just to feed the char/word heuristic.
            var report = _estimator.TrackUsage(
                AIProvider.OpenAI,
                "gpt-4o-mini",
                promptTokens: 500,
                completionTokens: 200,
                duration: TimeSpan.FromMilliseconds(750));

            report.Should().NotBeNull();
            report.Provider.Should().Be(AIProvider.OpenAI);
            report.Model.Should().Be("gpt-4o-mini");
            report.PromptTokens.Should().Be(500);
            report.ResponseTokens.Should().Be(200);
            report.TotalTokens.Should().Be(700);
            report.IsPriceKnown.Should().BeTrue();
            report.EstimatedCost.Should().BeGreaterThan(0m);
        }

        [Fact]
        public void TrackUsage_WithExplicitTokenCounts_UnknownModel_ReportsUnpriced()
        {
            var report = _estimator.TrackUsage(
                AIProvider.OpenAI,
                "gpt-99-does-not-exist-2099",
                promptTokens: 500,
                completionTokens: 200,
                duration: TimeSpan.FromMilliseconds(750));

            report.IsPriceKnown.Should().BeFalse();
            report.EstimatedCost.Should().Be(0m);
        }

        [Fact]
        public void GetUsageStatistics_TracksUnpricedRequestCount_SeparatelyFromTotalCost()
        {
            TokenCostEstimator.ResetUsageHistoryForTesting();
            try
            {
                _estimator.TrackUsage(AIProvider.OpenAI, "gpt-4o-mini", promptTokens: 500, completionTokens: 200, duration: TimeSpan.FromMilliseconds(500));
                _estimator.TrackUsage(AIProvider.OpenAI, "gpt-99-does-not-exist-2099", promptTokens: 500, completionTokens: 200, duration: TimeSpan.FromMilliseconds(500));

                var stats = _estimator.GetUsageStatistics(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(5));

                stats.TotalRequests.Should().Be(2);
                stats.UnpricedRequestCount.Should().Be(1);
                // TotalCost must reflect ONLY the priced request — the unpriced one
                // contributes zero, not a guessed amount, so the total is never inflated
                // by a fabricated number.
                stats.TotalCost.Should().BeGreaterThan(0m);
            }
            finally
            {
                TokenCostEstimator.ResetUsageHistoryForTesting();
            }
        }
    }
}
