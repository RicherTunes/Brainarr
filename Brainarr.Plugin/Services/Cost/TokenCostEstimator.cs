using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Cost
{
    /// <summary>
    /// Provides token counting and cost estimation for AI provider API calls.
    /// Helps users understand and control their AI provider expenses.
    /// </summary>
    public class TokenCostEstimator : ITokenCostEstimator
    {
        private readonly Logger _logger;
        private readonly Dictionary<AIProvider, ProviderPricing> _pricingData;

        public TokenCostEstimator(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _pricingData = InitializePricingData();
        }

        /// <summary>
        /// Estimates the number of tokens in a text string.
        /// Uses GPT-3/4 tokenization approximation (1 token ≈ 4 characters or 0.75 words).
        /// </summary>
        public int EstimateTokenCount(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            // More accurate tokenization based on OpenAI's tiktoken rules
            // This is an approximation - actual tokenization varies by model

            // Count words and characters
            var words = Regex.Matches(text, @"\b\w+\b").Count;
            var characters = text.Length;

            // Use hybrid approach: average of word-based and character-based estimates
            var wordBasedEstimate = (int)(words / 0.75);
            var charBasedEstimate = (int)(characters / 4.0);

            // Weight character-based slightly higher as it's more consistent
            var estimate = (int)((wordBasedEstimate * 0.4) + (charBasedEstimate * 0.6));

            // Add overhead for special tokens (start, end, etc.)
            estimate += 3;

            return estimate;
        }

        /// <summary>
        /// Calculates the estimated cost for a prompt and expected response.
        /// </summary>
        public CostEstimate EstimateCost(
            AIProvider provider,
            string model,
            string prompt,
            int expectedResponseTokens = 500)
        {
            var promptTokens = EstimateTokenCount(prompt);
            return EstimateCostFromTokenCounts(provider, model, promptTokens, expectedResponseTokens);
        }

        /// <summary>
        /// Calculates the estimated cost from already-known token counts, without
        /// re-tokenizing prompt/response text. Used by callers on the real request path
        /// (e.g. <see cref="TrackUsage(AIProvider, string, int, int, TimeSpan)"/>) that
        /// already have precise token estimates from upstream (prompt-builder budgeting,
        /// completion-size heuristics) and shouldn't fabricate text just to re-derive them.
        /// </summary>
        private CostEstimate EstimateCostFromTokenCounts(
            AIProvider provider,
            string model,
            int promptTokens,
            int responseTokens)
        {
            if (!_pricingData.TryGetValue(provider, out var pricing))
            {
                _logger.Warn($"No pricing data available for provider {provider}");
                return new CostEstimate
                {
                    Provider = provider,
                    Model = model,
                    PromptTokens = promptTokens,
                    ResponseTokens = responseTokens,
                    EstimatedCost = 0,
                    IsPriceKnown = false,
                    CostBreakdown = $"Pricing data not available for provider {provider} — cost not estimated to avoid an inaccurate number."
                };
            }

            var modelPricing = GetModelPricing(pricing, model);

            if (modelPricing == null)
            {
                // Unknown/unrecognized model: surface honestly rather than guessing a
                // number. A stale or overly-generic fallback here previously reported a
                // confidently-wrong dollar figure for any model the pricing table hadn't
                // been updated for yet (see CLAUDE.md "Cost visibility action" section).
                _logger.Debug($"No pricing entry matched model '{model}' for provider {provider}; reporting as unpriced.");
                return new CostEstimate
                {
                    Provider = provider,
                    Model = model,
                    PromptTokens = promptTokens,
                    ResponseTokens = responseTokens,
                    EstimatedCost = 0,
                    IsPriceKnown = false,
                    CostBreakdown = $"Pricing unknown for model '{model}' ({provider}) — cost not estimated to avoid a fabricated number."
                };
            }

            var promptCost = (promptTokens / 1000.0m) * modelPricing.InputPricePer1K;
            var responseCost = (responseTokens / 1000.0m) * modelPricing.OutputPricePer1K;
            var totalCost = promptCost + responseCost;

            return new CostEstimate
            {
                Provider = provider,
                Model = model,
                PromptTokens = promptTokens,
                ResponseTokens = responseTokens,
                EstimatedCost = totalCost,
                IsPriceKnown = true,
                CostBreakdown = $"Input: ${promptCost:F6} ({promptTokens} tokens) + " +
                               $"Output: ${responseCost:F6} ({responseTokens} tokens)",
                PricePerMillionTokens = modelPricing.InputPricePer1K * 1000,
                Currency = "USD"
            };
        }

        /// <summary>
        /// Tracks actual usage and updates cost estimates based on real response sizes.
        /// </summary>
        public UsageReport TrackUsage(
            AIProvider provider,
            string model,
            string prompt,
            string response,
            TimeSpan duration)
        {
            var promptTokens = EstimateTokenCount(prompt);
            var responseTokens = EstimateTokenCount(response);

            return TrackUsageFromTokenCounts(provider, model, promptTokens, responseTokens, duration);
        }

        /// <summary>
        /// Tracks actual usage from already-known prompt/completion token counts. This is
        /// the integration point for callers on the real recommendation path (e.g.
        /// <c>RecommendationGenerator</c>) that call a parsed-recommendations provider API
        /// (no raw response text is ever available) but already have accurate token
        /// estimates: prompt tokens from the prompt builder's own budgeting, completion
        /// tokens from the existing per-item completion-size heuristic. Avoids fabricating
        /// a synthetic "response" string just to re-tokenize it.
        /// </summary>
        public UsageReport TrackUsage(
            AIProvider provider,
            string model,
            int promptTokens,
            int completionTokens,
            TimeSpan duration)
        {
            return TrackUsageFromTokenCounts(provider, model, promptTokens, completionTokens, duration);
        }

        private UsageReport TrackUsageFromTokenCounts(
            AIProvider provider,
            string model,
            int promptTokens,
            int responseTokens,
            TimeSpan duration)
        {
            var estimate = EstimateCostFromTokenCounts(provider, model, promptTokens, responseTokens);

            var report = new UsageReport
            {
                Provider = provider,
                Model = model,
                Timestamp = DateTime.UtcNow,
                PromptTokens = promptTokens,
                ResponseTokens = responseTokens,
                TotalTokens = promptTokens + responseTokens,
                EstimatedCost = estimate.EstimatedCost,
                IsPriceKnown = estimate.IsPriceKnown,
                Duration = duration,
                TokensPerSecond = responseTokens / Math.Max(1, duration.TotalSeconds)
            };

            // Store in usage history for reporting
            StoreUsageReport(report);

            return report;
        }

        /// <summary>
        /// Gets usage statistics for a time period.
        /// </summary>
        public UsageStatistics GetUsageStatistics(DateTime startDate, DateTime endDate)
        {
            var reports = GetStoredReports(startDate, endDate);

            if (!reports.Any())
            {
                return new UsageStatistics
                {
                    StartDate = startDate,
                    EndDate = endDate,
                    TotalCost = 0,
                    TotalTokens = 0
                };
            }

            return new UsageStatistics
            {
                StartDate = startDate,
                EndDate = endDate,
                // Unpriced reports always carry EstimatedCost == 0, so this sum reflects
                // only known-priced usage by construction — it is never inflated by a
                // fabricated number for models the pricing table doesn't recognize.
                TotalCost = reports.Sum(r => r.EstimatedCost),
                TotalTokens = reports.Sum(r => r.TotalTokens),
                TotalRequests = reports.Count,
                UnpricedRequestCount = reports.Count(r => !r.IsPriceKnown),
                AverageTokensPerRequest = reports.Average(r => r.TotalTokens),
                AverageCostPerRequest = reports.Average(r => r.EstimatedCost),
                ProviderBreakdown = reports
                    .GroupBy(r => r.Provider)
                    .Select(g => new ProviderUsage
                    {
                        Provider = g.Key,
                        TotalCost = g.Sum(r => r.EstimatedCost),
                        TotalTokens = g.Sum(r => r.TotalTokens),
                        RequestCount = g.Count(),
                        UnpricedRequestCount = g.Count(r => !r.IsPriceKnown)
                    })
                    .ToList(),
                PeakUsageHour = reports
                    .GroupBy(r => r.Timestamp.Hour)
                    .OrderByDescending(g => g.Count())
                    .First().Key
            };
        }

        /// <summary>
        /// Checks if usage is approaching budget limits.
        /// </summary>
        public BudgetAlert CheckBudget(decimal monthlyBudget)
        {
            var currentMonth = DateTime.UtcNow;
            var startOfMonth = new DateTime(currentMonth.Year, currentMonth.Month, 1);
            var stats = GetUsageStatistics(startOfMonth, DateTime.UtcNow);

            var percentUsed = (stats.TotalCost / monthlyBudget) * 100;
            var daysInMonth = DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month);
            var daysElapsed = (DateTime.UtcNow - startOfMonth).Days + 1;
            var expectedUsage = (daysElapsed / (decimal)daysInMonth) * monthlyBudget;
            var projectedMonthlyTotal = (stats.TotalCost / daysElapsed) * daysInMonth;

            var alertLevel = percentUsed switch
            {
                >= 90 => AlertLevel.Critical,
                >= 75 => AlertLevel.Warning,
                >= 50 => AlertLevel.Info,
                _ => AlertLevel.None
            };

            return new BudgetAlert
            {
                MonthlyBudget = monthlyBudget,
                CurrentSpend = stats.TotalCost,
                PercentUsed = percentUsed,
                ProjectedMonthlyTotal = projectedMonthlyTotal,
                DaysRemaining = daysInMonth - daysElapsed,
                AlertLevel = alertLevel,
                Message = GenerateBudgetMessage(percentUsed, projectedMonthlyTotal, monthlyBudget),
                RecommendedDailyLimit = (monthlyBudget - stats.TotalCost) / Math.Max(1, daysInMonth - daysElapsed)
            };
        }

        // Pricing snapshot refreshed 2026-07 (publicly listed per-provider API pricing at
        // time of writing). Pricing tables go stale by nature — new models ship faster
        // than this table gets updated — so the honesty net in GetModelPricing/EstimateCost
        // is the durable fix: an unrecognized model surfaces as unpriced (IsPriceKnown =
        // false, cost = 0) rather than silently reusing a guessed number. Treat entries
        // below as "best known at last refresh," not a guarantee.
        //
        // IMPORTANT: pricing lookup is exact-only (plus an explicit provider "default"
        // sentinel for known-free local/subscription providers). Do not add prefix/fuzzy
        // matching here. Same-prefix families can carry different prices or non-token fees
        // (for example "gpt-5" vs later "gpt-5.x" ids, or Perplexity "sonar" vs
        // reasoning/deep-research SKUs), so unknown siblings must fall through to unpriced
        // until a maintainer adds an explicit table entry.
        private Dictionary<AIProvider, ProviderPricing> InitializePricingData()
        {
            return new Dictionary<AIProvider, ProviderPricing>
            {
                [AIProvider.OpenAI] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        // Current-generation
                        ["gpt-5-nano"] = new ModelPricing { InputPricePer1K = 0.00005m, OutputPricePer1K = 0.0004m },
                        ["gpt-5-mini"] = new ModelPricing { InputPricePer1K = 0.00025m, OutputPricePer1K = 0.002m },
                        ["gpt-5"] = new ModelPricing { InputPricePer1K = 0.00125m, OutputPricePer1K = 0.01m },
                        ["gpt-4o"] = new ModelPricing { InputPricePer1K = 0.0025m, OutputPricePer1K = 0.01m },
                        ["gpt-4o-mini"] = new ModelPricing { InputPricePer1K = 0.00015m, OutputPricePer1K = 0.0006m },
                        ["gpt-4.1-nano"] = new ModelPricing { InputPricePer1K = 0.0001m, OutputPricePer1K = 0.0004m },
                        ["gpt-4.1-mini"] = new ModelPricing { InputPricePer1K = 0.0004m, OutputPricePer1K = 0.0016m },
                        ["gpt-4.1"] = new ModelPricing { InputPricePer1K = 0.002m, OutputPricePer1K = 0.008m },
                        ["o1-mini"] = new ModelPricing { InputPricePer1K = 0.003m, OutputPricePer1K = 0.012m },
                        ["o1"] = new ModelPricing { InputPricePer1K = 0.015m, OutputPricePer1K = 0.06m },
                        ["o3-mini"] = new ModelPricing { InputPricePer1K = 0.0011m, OutputPricePer1K = 0.0044m },
                        ["o4-mini"] = new ModelPricing { InputPricePer1K = 0.0011m, OutputPricePer1K = 0.0044m },
                        // Legacy (still selectable/billable)
                        ["gpt-4-turbo"] = new ModelPricing { InputPricePer1K = 0.01m, OutputPricePer1K = 0.03m },
                        ["gpt-4"] = new ModelPricing { InputPricePer1K = 0.03m, OutputPricePer1K = 0.06m },
                        ["gpt-3.5-turbo"] = new ModelPricing { InputPricePer1K = 0.0005m, OutputPricePer1K = 0.0015m }
                        // o1-preview, o3-pro deliberately omitted: pricing not confidently
                        // known at refresh time — falls through to unpriced rather than a guess.
                    }
                },
                [AIProvider.Anthropic] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        // Real, dated ids as emitted by ModelIdMapper for the legacy/back-compat
                        // selections. Deliberately NOT keyed as bare "claude-sonnet-4" /
                        // "claude-opus-4" — see the class-level note above on prefix collisions
                        // with newer same-family raw ids this codebase's model picker can emit
                        // (e.g. "claude-sonnet-4-6", "claude-opus-4-7") whose real pricing isn't
                        // confidently known yet; those correctly fall through to unpriced.
                        ["claude-sonnet-4-20250514"] = new ModelPricing { InputPricePer1K = 0.003m, OutputPricePer1K = 0.015m },
                        ["claude-3-7-sonnet"] = new ModelPricing { InputPricePer1K = 0.003m, OutputPricePer1K = 0.015m },
                        ["claude-3-5-sonnet-20241022"] = new ModelPricing { InputPricePer1K = 0.003m, OutputPricePer1K = 0.015m },
                        ["claude-3-5-sonnet"] = new ModelPricing { InputPricePer1K = 0.003m, OutputPricePer1K = 0.015m },
                        ["claude-3-5-haiku-20241022"] = new ModelPricing { InputPricePer1K = 0.0008m, OutputPricePer1K = 0.004m },
                        ["claude-3-5-haiku"] = new ModelPricing { InputPricePer1K = 0.0008m, OutputPricePer1K = 0.004m },
                        // Legacy (still selectable/billable)
                        ["claude-3-opus"] = new ModelPricing { InputPricePer1K = 0.015m, OutputPricePer1K = 0.075m },
                        ["claude-3-sonnet"] = new ModelPricing { InputPricePer1K = 0.003m, OutputPricePer1K = 0.015m },
                        ["claude-3-haiku"] = new ModelPricing { InputPricePer1K = 0.00025m, OutputPricePer1K = 0.00125m }
                    }
                },
                [AIProvider.Gemini] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        // 2.5 pricing approximated at the <=200k-token / non-thinking tier;
                        // Gemini's actual tiering is more granular than this table models.
                        ["gemini-2.5-flash-lite"] = new ModelPricing { InputPricePer1K = 0.0001m, OutputPricePer1K = 0.0004m },
                        ["gemini-2.5-flash"] = new ModelPricing { InputPricePer1K = 0.0003m, OutputPricePer1K = 0.0025m },
                        ["gemini-2.5-pro"] = new ModelPricing { InputPricePer1K = 0.00125m, OutputPricePer1K = 0.01m },
                        ["gemini-2.0-flash"] = new ModelPricing { InputPricePer1K = 0.0001m, OutputPricePer1K = 0.0004m },
                        ["gemini-1.5-pro"] = new ModelPricing { InputPricePer1K = 0.00125m, OutputPricePer1K = 0.005m },
                        ["gemini-1.5-flash"] = new ModelPricing { InputPricePer1K = 0.000075m, OutputPricePer1K = 0.0003m }
                        // gemini-3.x deliberately omitted: not confidently known at refresh time.
                    }
                },
                [AIProvider.Perplexity] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["sonar-pro"] = new ModelPricing { InputPricePer1K = 0.003m, OutputPricePer1K = 0.015m },
                        ["sonar"] = new ModelPricing { InputPricePer1K = 0.001m, OutputPricePer1K = 0.001m },
                        // Legacy naming
                        ["llama-3-sonar-large"] = new ModelPricing { InputPricePer1K = 0.001m, OutputPricePer1K = 0.001m },
                        ["llama-3-sonar-small"] = new ModelPricing { InputPricePer1K = 0.0002m, OutputPricePer1K = 0.0002m }
                    }
                },
                [AIProvider.Groq] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["llama-3.3-70b"] = new ModelPricing { InputPricePer1K = 0.00059m, OutputPricePer1K = 0.00079m },
                        ["llama-3.1-8b"] = new ModelPricing { InputPricePer1K = 0.00005m, OutputPricePer1K = 0.00008m },
                        // Legacy
                        ["mixtral-8x7b"] = new ModelPricing { InputPricePer1K = 0.00027m, OutputPricePer1K = 0.00027m },
                        ["llama2-70b"] = new ModelPricing { InputPricePer1K = 0.00064m, OutputPricePer1K = 0.00064m }
                    }
                },
                [AIProvider.DeepSeek] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["deepseek-chat"] = new ModelPricing { InputPricePer1K = 0.00027m, OutputPricePer1K = 0.0011m },
                        ["deepseek-reasoner"] = new ModelPricing { InputPricePer1K = 0.00055m, OutputPricePer1K = 0.00219m }
                    }
                },
                // OpenRouter deliberately has NO blanket "default" entry: it proxies a
                // marketplace of independently-priced third-party models with no sane
                // single price. A generic default here is exactly the fabricated-number
                // failure mode this table refresh fixes — unmapped OpenRouter models
                // correctly fall through to the unpriced path instead.
                [AIProvider.OpenRouter] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>()
                },
                // Local providers have no API costs — this is a REAL, known $0 (IsPriceKnown
                // stays true), not "unknown pricing". Any model name routes to it because a
                // local backend genuinely costs nothing to call regardless of which model
                // is loaded.
                [AIProvider.Ollama] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["default"] = new ModelPricing { InputPricePer1K = 0m, OutputPricePer1K = 0m }
                    }
                },
                [AIProvider.LMStudio] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["default"] = new ModelPricing { InputPricePer1K = 0m, OutputPricePer1K = 0m }
                    }
                },
                // Subscription/CLI providers bill a flat plan fee (Claude Pro/Max, ChatGPT
                // subscription), not metered per-token API usage — like local providers, this
                // is a REAL known $0 marginal cost, not "we don't know the price." ZaiGlm and
                // ZaiCoding are intentionally excluded here: those ARE metered per-token APIs
                // (Z.AI PaaS / Coding Plan credits), just not yet in this table (no confident
                // pricing at refresh time) — they correctly fall through to the
                // provider-not-found unpriced path rather than being mislabeled as free.
                [AIProvider.ClaudeCodeSubscription] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["default"] = new ModelPricing { InputPricePer1K = 0m, OutputPricePer1K = 0m }
                    }
                },
                [AIProvider.OpenAICodexSubscription] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["default"] = new ModelPricing { InputPricePer1K = 0m, OutputPricePer1K = 0m }
                    }
                },
                [AIProvider.ClaudeCodeCli] = new ProviderPricing
                {
                    Models = new Dictionary<string, ModelPricing>
                    {
                        ["default"] = new ModelPricing { InputPricePer1K = 0m, OutputPricePer1K = 0m }
                    }
                }
            };
        }

        /// <summary>
        /// Resolves pricing for a model, or null when the model/provider pairing isn't
        /// recognized. Returning null (rather than a generic guessed price) is load-bearing
        /// for the cost-panel honesty guarantee — callers must treat null as "unknown," not
        /// silently substitute a number.
        /// </summary>
        private ModelPricing GetModelPricing(ProviderPricing provider, string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return provider.Models.GetValueOrDefault("default");
            }

            var normalizedModel = model.Trim();

            // Try exact match first.
            if (provider.Models.TryGetValue(normalizedModel, out var pricing))
                return pricing;

            // Keep lookup case-tolerant, but still exact. Prefix/fuzzy matching creates
            // confidently-wrong estimates for newer same-family models.
            var caseInsensitiveMatch = provider.Models
                .FirstOrDefault(kvp => string.Equals(kvp.Key, normalizedModel, StringComparison.OrdinalIgnoreCase));
            if (caseInsensitiveMatch.Value != null)
                return caseInsensitiveMatch.Value;

            // Only a provider that explicitly declares a "default" (i.e. every model under
            // it is priced identically — local backends) falls back to it. Everything else
            // (including OpenRouter's marketplace of independently-priced models) surfaces
            // as unpriced via the null return.
            return provider.Models.GetValueOrDefault("default");
        }

        // Wave 54 thread-safety fix: UsageHistory is a STATIC List<T> mutated from
        // request paths (StoreUsageReport) AND read concurrently (GetStoredReports).
        // List<T> is not thread-safe; concurrent Add+RemoveAll+Where can corrupt the
        // list (resize race) or throw InvalidOperationException ("collection modified
        // during enumeration"). All access now goes through this static lock.
        private static readonly object _usageHistoryLock = new object();

        private void StoreUsageReport(UsageReport report)
        {
            var cutoff = DateTime.UtcNow.AddDays(-30);
            lock (_usageHistoryLock)
            {
                UsageHistory.Add(report);
                UsageHistory.RemoveAll(r => r.Timestamp < cutoff);

                if (UsageHistory.Count > MaxUsageHistoryEntries)
                {
                    UsageHistory.RemoveRange(0, UsageHistory.Count - MaxUsageHistoryEntries);
                }
            }
        }

        private List<UsageReport> GetStoredReports(DateTime startDate, DateTime endDate)
        {
            lock (_usageHistoryLock)
            {
                return UsageHistory
                    .Where(r => r.Timestamp >= startDate && r.Timestamp <= endDate)
                    .ToList();
            }
        }

        private string GenerateBudgetMessage(decimal percentUsed, decimal projected, decimal budget)
        {
            if (percentUsed >= 90)
                return $"CRITICAL: {percentUsed:F1}% of monthly budget used! Projected to exceed by ${projected - budget:F2}";
            if (percentUsed >= 75)
                return $"Warning: {percentUsed:F1}% of monthly budget used. Monitor closely.";
            if (percentUsed >= 50)
                return $"Info: {percentUsed:F1}% of monthly budget used. On track.";

            return $"Budget healthy: {percentUsed:F1}% used.";
        }

        internal const int MaxUsageHistoryEntries = 10_000;
        private static readonly List<UsageReport> UsageHistory = new List<UsageReport>();

        // Test hooks (InternalsVisibleTo "Brainarr.Tests"): UsageHistory is process-wide static state,
        // so a bounded-growth test must be able to reset it (before AND after, so it neither inherits
        // nor leaks pollution) and read the count. Both go through the same lock as the real paths.
        internal static void ResetUsageHistoryForTesting()
        {
            lock (_usageHistoryLock) { UsageHistory.Clear(); }
        }

        internal static int UsageHistoryCountForTesting
        {
            get { lock (_usageHistoryLock) { return UsageHistory.Count; } }
        }

        private class ProviderPricing
        {
            public Dictionary<string, ModelPricing> Models { get; set; }
        }

        private class ModelPricing
        {
            public decimal InputPricePer1K { get; set; }
            public decimal OutputPricePer1K { get; set; }
        }
    }

    public interface ITokenCostEstimator
    {
        int EstimateTokenCount(string text);
        CostEstimate EstimateCost(AIProvider provider, string model, string prompt, int expectedResponseTokens = 500);
        UsageReport TrackUsage(AIProvider provider, string model, string prompt, string response, TimeSpan duration);

        /// <summary>
        /// Tracks usage from already-known token counts (no raw response text available —
        /// e.g. providers whose API returns parsed results, not raw completion text).
        /// </summary>
        UsageReport TrackUsage(AIProvider provider, string model, int promptTokens, int completionTokens, TimeSpan duration);
        UsageStatistics GetUsageStatistics(DateTime startDate, DateTime endDate);
        BudgetAlert CheckBudget(decimal monthlyBudget);
    }

    public class CostEstimate
    {
        public AIProvider Provider { get; set; }
        public string Model { get; set; } = string.Empty;
        public int PromptTokens { get; set; }
        public int ResponseTokens { get; set; }
        public decimal EstimatedCost { get; set; }

        /// <summary>
        /// False when the model/provider pairing isn't in the pricing table — EstimatedCost
        /// is a real zero in that case, not "we know it costs nothing." Callers must not
        /// present EstimatedCost as reliable when this is false.
        /// </summary>
        public bool IsPriceKnown { get; set; } = true;
        public string CostBreakdown { get; set; } = string.Empty;
        public decimal PricePerMillionTokens { get; set; }
        public string Currency { get; set; } = string.Empty;
    }

    public class UsageReport
    {
        public AIProvider Provider { get; set; }
        public string Model { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public int PromptTokens { get; set; }
        public int ResponseTokens { get; set; }
        public int TotalTokens { get; set; }
        public decimal EstimatedCost { get; set; }

        /// <summary>See <see cref="CostEstimate.IsPriceKnown"/>.</summary>
        public bool IsPriceKnown { get; set; } = true;
        public TimeSpan Duration { get; set; }
        public double TokensPerSecond { get; set; }
    }

    public class UsageStatistics
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Sum of EstimatedCost across all reports in range. Unpriced reports contribute
        /// exactly 0, so this total is never inflated by a guessed number — see
        /// <see cref="UnpricedRequestCount"/> for how many requests it excludes.
        /// </summary>
        public decimal TotalCost { get; set; }
        public int TotalTokens { get; set; }
        public int TotalRequests { get; set; }

        /// <summary>
        /// Count of requests whose model/provider pairing had no known pricing (excluded
        /// from TotalCost, not zero-cost). A UI should surface this alongside TotalCost so
        /// "$0.00 spent" is never confused with "N requests we couldn't price."
        /// </summary>
        public int UnpricedRequestCount { get; set; }
        public double AverageTokensPerRequest { get; set; }
        public decimal AverageCostPerRequest { get; set; }
        public List<ProviderUsage> ProviderBreakdown { get; set; } = new List<ProviderUsage>();
        public int PeakUsageHour { get; set; }
    }

    public class ProviderUsage
    {
        public AIProvider Provider { get; set; }
        public decimal TotalCost { get; set; }
        public int TotalTokens { get; set; }
        public int RequestCount { get; set; }

        /// <summary>See <see cref="UsageStatistics.UnpricedRequestCount"/>, scoped to this provider.</summary>
        public int UnpricedRequestCount { get; set; }
    }

    public class BudgetAlert
    {
        public decimal MonthlyBudget { get; set; }
        public decimal CurrentSpend { get; set; }
        public decimal PercentUsed { get; set; }
        public decimal ProjectedMonthlyTotal { get; set; }
        public int DaysRemaining { get; set; }
        public AlertLevel AlertLevel { get; set; }
        public string Message { get; set; } = string.Empty;
        public decimal RecommendedDailyLimit { get; set; }
    }

    public enum AlertLevel
    {
        None,
        Info,
        Warning,
        Critical
    }
}
