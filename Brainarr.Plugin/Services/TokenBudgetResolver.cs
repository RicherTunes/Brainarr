using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Capabilities;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using ProviderCapabilityCaps = NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Capabilities.ProviderCapability;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Resolves token budgets for prompt building based on provider capabilities,
    /// model context windows, and sampling strategy tiers.
    /// Extracted from LibraryAwarePromptBuilder (M6-2).
    /// </summary>
    internal class TokenBudgetResolver
    {
        private readonly Logger _logger;
        private readonly ModelContextResolver _modelContextResolver;
        private readonly ITokenBudgetPolicy _tokenBudgetPolicy;

        internal const double MinimalRatio = 0.35;
        internal const double BalancedRatio = 0.60;
        internal const double ComprehensiveRatio = 1.00;
        internal const int MinimalPromptFloor = 1500;

        internal static readonly Dictionary<AIProvider, int> DefaultContextTokens = new()
        {
            [AIProvider.Ollama] = 32768,
            [AIProvider.LMStudio] = 32768,
            [AIProvider.OpenAI] = 64000,
            [AIProvider.Anthropic] = 120000,
            [AIProvider.Perplexity] = 32000,
            [AIProvider.OpenRouter] = 64000,
            [AIProvider.DeepSeek] = 48000,
            [AIProvider.Gemini] = 32000,
            [AIProvider.Groq] = 32000,
        };

        public TokenBudgetResolver(Logger logger, ModelContextResolver modelContextResolver, ITokenBudgetPolicy tokenBudgetPolicy)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _modelContextResolver = modelContextResolver ?? throw new ArgumentNullException(nameof(modelContextResolver));
            _tokenBudgetPolicy = tokenBudgetPolicy ?? throw new ArgumentNullException(nameof(tokenBudgetPolicy));
        }

        public PromptBudget ResolvePromptBudget(BrainarrSettings settings, ProviderCapabilityCaps capabilities)
        {
            var modelInfo = _modelContextResolver.Resolve(settings);
            var modelKey = !string.IsNullOrWhiteSpace(modelInfo.ModelKey)
                ? modelInfo.ModelKey
                : ProviderSlugs.ToRegistrySlug(settings.Provider) ?? settings.Provider.ToString();

            var contextTokens = modelInfo.ContextTokens > 0
                ? modelInfo.ContextTokens
                : capabilities.MaxContextTokensOverride
                    ?? (DefaultContextTokens.TryGetValue(settings.Provider, out var fallback)
                        ? fallback
                        : 24000);

            var systemReserve = Math.Max(0, _tokenBudgetPolicy.SystemReserveTokens(modelKey));
            var completionRatio = Math.Clamp(_tokenBudgetPolicy.CompletionReserveRatio(modelKey), 0.0, 0.90);
            var completionReserve = Math.Max(512, (int)Math.Floor(contextTokens * completionRatio));

            var safetyRatio = Math.Clamp(_tokenBudgetPolicy.SafetyMarginRatio(modelKey), 0.0, 0.90);
            var headroomTokens = Math.Max(_tokenBudgetPolicy.HeadroomTokens(modelKey), (int)Math.Floor(contextTokens * safetyRatio));

            var promptBudget = Math.Max(MinimalPromptFloor, contextTokens - systemReserve - completionReserve - headroomTokens);

            var providerCeiling = GetProviderPromptCeiling(settings.Provider);
            if (providerCeiling > 0 && providerCeiling < int.MaxValue)
            {
                promptBudget = Math.Min(promptBudget, providerCeiling);
            }

            if (settings.SamplingStrategy == SamplingStrategy.Comprehensive && settings.ComprehensiveTokenBudgetOverride.HasValue)
            {
                promptBudget = Math.Min(promptBudget, settings.ComprehensiveTokenBudgetOverride.Value);
            }

            var tierRatio = settings.SamplingStrategy switch
            {
                SamplingStrategy.Minimal => MinimalRatio,
                SamplingStrategy.Balanced => BalancedRatio,
                _ => ComprehensiveRatio
            };

            var tierBudget = (int)Math.Max(MinimalPromptFloor * tierRatio, Math.Floor(promptBudget * tierRatio));
            tierBudget = Math.Min(promptBudget, Math.Max(MinimalPromptFloor, tierBudget));

            _logger.Debug("Resolved prompt budget (provider={0}, model={1}, context={2}, system={3}, completion={4}, headroom={5}, prompt={6}, tier={7})",
                settings.Provider,
                modelInfo.ModelKey ?? modelInfo.RawModelId ?? "<unknown>",
                contextTokens,
                systemReserve,
                completionReserve,
                headroomTokens,
                promptBudget,
                tierBudget);
            return new PromptBudget
            {
                ContextTokens = contextTokens,
                PromptTokens = promptBudget,
                CompletionReserveTokens = completionReserve,
                SystemReserveTokens = systemReserve,
                TierBudget = tierBudget,
                ModelKey = modelInfo.ModelKey,
                RawModelId = modelInfo.RawModelId,
                HeadroomTokens = headroomTokens
            };
        }

        internal static int GetProviderPromptCeiling(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama or AIProvider.LMStudio => int.MaxValue,
                _ => 20000
            };
        }

        /// <summary>
        /// Token budget resolved for a specific provider/model/strategy combination.
        /// </summary>
        public sealed record PromptBudget
        {
            public int ContextTokens { get; init; }
            public int PromptTokens { get; init; }
            public int CompletionReserveTokens { get; init; }
            public int SystemReserveTokens { get; init; }
            public int TierBudget { get; init; }
            public string ModelKey { get; init; } = string.Empty;
            public string RawModelId { get; init; } = string.Empty;
            public int HeadroomTokens { get; init; }
        }
    }
}
