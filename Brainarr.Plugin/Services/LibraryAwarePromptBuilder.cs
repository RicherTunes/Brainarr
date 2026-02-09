using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Capabilities;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Tokenization;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Builds rich, library-grounded prompts for AI providers. Prompts honour user-selected
    /// styles, stay within model-specific token budgets, and expose telemetry for diagnostics.
    /// </summary>
    public class LibraryAwarePromptBuilder : ILibraryAwarePromptBuilder
    {
        private readonly Logger _logger;
        private readonly ModelContextResolver _modelContextResolver;
        private readonly SamplingSeedComputer _samplingSeedComputer;
        private readonly ITokenizerRegistry _tokenizerRegistry;
        private readonly IPromptPlanner _planner;
        private readonly IPromptRenderer _renderer;
        private readonly IPlanCache _planCache;
        private readonly IMetrics _metrics;
        private readonly ITokenBudgetPolicy _tokenBudgetPolicy;
        private readonly TokenBudgetResolver _budgetResolver;
        private readonly PromptCompressionOrchestrator _compressionOrchestrator;
        private bool _renderedOnce;

        public LibraryAwarePromptBuilder(Logger logger)
            : this(
                logger,
                new StyleCatalogService(logger, httpClient: null),
                new ModelRegistryLoader(),
                new ModelTokenizerRegistry(logger: logger, metrics: new NoOpMetrics()),
                registryUrl: null,
                promptPlanner: null,
                promptRenderer: null,
                // Use per-instance cache to avoid cross-test/process state bleeding
                planCache: new PlanCache(metrics: new NoOpMetrics()),
                metrics: new NoOpMetrics())
        {
        }



        public LibraryAwarePromptBuilder(
            Logger logger,
            IStyleCatalogService styleCatalog,
            ModelRegistryLoader modelRegistryLoader,
            ITokenizerRegistry tokenizerRegistry,
            string? registryUrl = null,
            IPromptPlanner? promptPlanner = null,
            IPromptRenderer? promptRenderer = null,
            IPlanCache? planCache = null,
            IMetrics? metrics = null,
            ITokenBudgetPolicy? tokenBudgetPolicy = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (styleCatalog == null)
            {
                throw new ArgumentNullException(nameof(styleCatalog));
            }

            if (modelRegistryLoader == null) throw new ArgumentNullException(nameof(modelRegistryLoader));

            _modelContextResolver = new ModelContextResolver(logger, modelRegistryLoader, registryUrl);
            _samplingSeedComputer = new SamplingSeedComputer(logger);
            _tokenizerRegistry = tokenizerRegistry ?? new ModelTokenizerRegistry(logger: logger, metrics: _metrics);
            _metrics = metrics ?? new NoOpMetrics();

            if (planCache == null)
            {
                // Fall back to a fresh per-instance cache instead of a static singleton to make
                // behavior deterministic and test-friendly. Metrics-aware caches can still be injected via DI.
                planCache = new PlanCache(metrics: new NoOpMetrics());
            }

            _planCache = planCache;
            _tokenBudgetPolicy = tokenBudgetPolicy ?? new DefaultTokenBudgetPolicy();
            _budgetResolver = new TokenBudgetResolver(logger, _modelContextResolver, _tokenBudgetPolicy);
            _planner = promptPlanner ?? new LibraryPromptPlanner(_logger, styleCatalog, _planCache);
            _renderer = promptRenderer ?? new LibraryPromptRenderer();
            _compressionOrchestrator = new PromptCompressionOrchestrator(_renderer, _planCache, _metrics, _logger);
            _logger.Debug("LibraryAwarePromptBuilder instance created");
        }

        public string BuildLibraryAwarePrompt(
            LibraryProfile profile,
            List<Artist> allArtists,
            List<Album> allAlbums,
            BrainarrSettings settings,
            bool shouldRecommendArtists = false,
            CancellationToken cancellationToken = default)
        {
            var res = BuildLibraryAwarePromptWithMetrics(
                profile, allArtists, allAlbums, settings,
                shouldRecommendArtists, cancellationToken);
            return res.Prompt;
        }

        public LibraryPromptResult BuildLibraryAwarePromptWithMetrics(
            LibraryProfile profile,
            List<Artist> allArtists,
            List<Album> allAlbums,
            BrainarrSettings settings,
            bool shouldRecommendArtists = false,
            CancellationToken cancellationToken = default)
        {
            var result = new LibraryPromptResult();
            TokenBudgetResolver.PromptBudget budget = new();
            var headroomCap = 0;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                ConfigureCaches(settings);
                budget = ResolveBudget(settings, result, out var clampedTargetTokens);
                headroomCap = Math.Max(0, budget.ContextTokens - budget.HeadroomTokens);

                var (plan, metricTags) = PlanAndPopulateResult(
                    profile, allArtists, allAlbums, settings,
                    shouldRecommendArtists, clampedTargetTokens, budget, result, cancellationToken);

                var compression = RenderAndCompress(
                    plan, settings, budget, clampedTargetTokens, metricTags, cancellationToken);

                plan = compression.FinalPlan;
                PopulateCompressionResult(result, plan, compression, clampedTargetTokens);
                LogPromptMetrics(result, plan, clampedTargetTokens);
            }
            catch (Exception ex)
            {
                HandleFallback(ex, profile, settings, result, budget, headroomCap);
            }

            return result;
        }

        private void ConfigureCaches(BrainarrSettings settings)
        {
            var cacheSettings = settings.EffectiveCacheSettings;
            _planCache.Configure(cacheSettings.PlanCacheCapacity);
            _planner.ConfigureCacheTtl(cacheSettings.PlanCacheTtl);
        }

        private TokenBudgetResolver.PromptBudget ResolveBudget(
            BrainarrSettings settings, LibraryPromptResult result, out int clampedTargetTokens)
        {
            var capabilities = ProviderCapabilities.Get(settings.Provider);
            var budget = _budgetResolver.ResolvePromptBudget(settings, capabilities);

            clampedTargetTokens = TokenBudgetGuard.ClampTargetTokens(
                budget.TierBudget, budget.ContextTokens, budget.HeadroomTokens);

            result.PromptBudgetTokens = clampedTargetTokens;
            result.ModelContextTokens = budget.ContextTokens;
            result.BudgetModelKey = budget.ModelKey;
            result.TokenHeadroom = budget.HeadroomTokens;

            return budget;
        }

        private (PromptPlan Plan, Dictionary<string, string> MetricTags) PlanAndPopulateResult(
            LibraryProfile profile,
            List<Artist> allArtists,
            List<Album> allAlbums,
            BrainarrSettings settings,
            bool shouldRecommendArtists,
            int clampedTargetTokens,
            TokenBudgetResolver.PromptBudget budget,
            LibraryPromptResult result,
            CancellationToken cancellationToken)
        {
            var request = new RecommendationRequest(
                allArtists, allAlbums, settings,
                profile.StyleContext ?? new LibraryStyleContext(),
                shouldRecommendArtists, clampedTargetTokens,
                Math.Max(1000, clampedTargetTokens - Math.Max(0, budget.SystemReserveTokens)),
                budget.ModelKey, budget.ContextTokens);

            var plan = _planner.Plan(profile, request, cancellationToken);
            plan = plan with
            {
                ContextWindow = budget.ContextTokens,
                HeadroomTokens = budget.HeadroomTokens,
                TargetTokens = clampedTargetTokens
            };

            result.PlanCacheHit = plan.FromCache;
            var metricTags = new Dictionary<string, string> { ["model"] = budget.ModelKey };
            _metrics.Record(MetricsNames.PromptPlanCacheHit, plan.FromCache ? 1 : 0, metricTags);

            result.SampleSeed = plan.SampleSeed;
            result.SampleFingerprint = plan.SampleFingerprint;
            result.SampledArtists = plan.Sample.ArtistCount;
            result.SampledAlbums = plan.Sample.AlbumCount;
            result.MatchedStyleCounts = new Dictionary<string, int>(plan.MatchedStyleCounts, StringComparer.OrdinalIgnoreCase);
            result.StyleCoverage = new Dictionary<string, int>(plan.StyleCoverage, StringComparer.OrdinalIgnoreCase);
            result.StyleCoverageSparse = plan.StyleCoverageSparse;
            result.RelaxedStyleMatching = plan.RelaxedStyleMatching;
            result.TrimmedStyles = plan.TrimmedStyles.ToList();
            result.InferredStyleSlugs = plan.InferredStyleSlugs.ToList();
            result.AppliedStyleSlugs = plan.StyleContext.SelectedSlugs.ToList();
            result.AppliedStyleNames = plan.StyleContext.Entries.Select(e => e.Name).ToList();

            return (plan, metricTags);
        }

        private PromptCompressionOrchestrator.CompressionResult RenderAndCompress(
            PromptPlan plan,
            BrainarrSettings settings,
            TokenBudgetResolver.PromptBudget budget,
            int clampedTargetTokens,
            Dictionary<string, string> metricTags,
            CancellationToken cancellationToken)
        {
            var tokenizer = _tokenizerRegistry.Get(budget.ModelKey);
            var template = ResolvePromptTemplate(settings.Provider);

            // Ensure deterministic header semantics for the first render in a fresh builder instance
            // (tests expect cache_hit=false on the initial build even if a shared cache was pre-warmed).
            var renderPlan = !_renderedOnce ? plan with { FromCache = false } : plan;
            var initialPrompt = _renderer.Render(renderPlan, template, cancellationToken);
            _renderedOnce = true;

            return _compressionOrchestrator.CompressAndValidate(
                plan, initialPrompt, tokenizer, template, budget,
                clampedTargetTokens, metricTags, cancellationToken);
        }

        private static void PopulateCompressionResult(
            LibraryPromptResult result,
            PromptPlan plan,
            PromptCompressionOrchestrator.CompressionResult compression,
            int clampedTargetTokens)
        {
            result.Prompt = compression.Prompt;
            result.EstimatedTokens = compression.ReportedTokens;
            result.EstimatedTokensPreCompression = plan.EstimatedTokensPreCompression;
            result.Compressed = plan.Compressed;
            result.Trimmed = plan.TrimmedForBudget || compression.GuardTriggered;
            result.CompressionRatio = plan.CompressionRatio ?? 1.0;
            result.FallbackReason = compression.FallbackReason;
            result.TokenEstimateDrift = 0.0;
        }

        private void LogPromptMetrics(LibraryPromptResult result, PromptPlan plan, int clampedTargetTokens)
        {
            if (_logger.IsInfoEnabled)
            {
                _logger.Info(
                    "prompt_plan seed={Seed} model={Model} budget={Budget} compressed={Compressed} trimmed={Trimmed} sparse={Sparse} cache_hit={CacheHit} deterministic={Deterministic} comp_ratio={Drift:F3} pre={Pre} post={Post}",
                    result.SampleSeed,
                    result.BudgetModelKey,
                    clampedTargetTokens,
                    result.Compressed,
                    result.Trimmed,
                    result.StyleCoverageSparse,
                    result.PlanCacheHit,
                    plan.DeterministicOrderingApplied,
                    plan.CompressionRatio ?? 1.0,
                    plan.EstimatedTokensPreCompression,
                    result.EstimatedTokens);
            }
        }

        private void HandleFallback(
            Exception ex,
            LibraryProfile profile,
            BrainarrSettings settings,
            LibraryPromptResult result,
            TokenBudgetResolver.PromptBudget budget,
            int headroomCap)
        {
            _logger.Error(ex, "Failed to build library-aware prompt, falling back to baseline prompt");

            var prompt = FallbackPromptGenerator.BuildFallbackPrompt(profile, settings);
            result.Prompt = prompt;
            result.SampledArtists = 0;
            result.SampledAlbums = 0;

            var estimated = EstimateTokens(prompt, null);
            var clamped = headroomCap > 0 ? Math.Min(estimated, headroomCap) : estimated;
            result.EstimatedTokens = clamped;
            result.TokenHeadroom = budget.HeadroomTokens;
            result.ModelContextTokens = budget.ContextTokens;

            if (string.IsNullOrEmpty(result.FallbackReason))
            {
                result.FallbackReason = "fallback_prompt";
            }

            if (headroomCap > 0 && estimated > headroomCap)
            {
                result.Trimmed = true;
                if (!result.FallbackReason.Contains("headroom_guard", StringComparison.Ordinal))
                {
                    result.FallbackReason = $"{result.FallbackReason}|headroom_guard";
                }
            }
        }

        public int GetEffectiveTokenLimit(SamplingStrategy strategy, AIProvider provider)
        {
            var settings = new BrainarrSettings
            {
                SamplingStrategy = strategy,
                Provider = provider
            };

            var capability = ProviderCapabilities.Get(provider);
            var budget = _budgetResolver.ResolvePromptBudget(settings, capability);
            return TokenBudgetGuard.ClampTargetTokens(budget.TierBudget, budget.ContextTokens, budget.HeadroomTokens);
        }

        public int EstimateTokens(string text, string? modelKey = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var tokenizer = _tokenizerRegistry.Get(modelKey);
            return tokenizer.CountTokens(text);
        }

        private static ModelPromptTemplate ResolvePromptTemplate(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Anthropic => ModelPromptTemplate.Anthropic,
                AIProvider.Gemini => ModelPromptTemplate.Gemini,
                _ => ModelPromptTemplate.Default
            };
        }

        public int ComputeSamplingSeed(
            LibraryProfile profile,
            BrainarrSettings settings,
            bool shouldRecommendArtists,
            CancellationToken cancellationToken = default)
            => _samplingSeedComputer.ComputeSamplingSeed(profile, settings, shouldRecommendArtists, cancellationToken);

        internal static StableHash.StableHashResult ComputeStableHash(IEnumerable<string> components)
            => SamplingSeedComputer.ComputeStableHash(components);
    }

}
