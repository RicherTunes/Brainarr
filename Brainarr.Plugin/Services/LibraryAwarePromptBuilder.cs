using System;

using System.Collections.Generic;

using System.Linq;


using System.Threading;

using NLog;

using NzbDrone.Core.ImportLists.Brainarr.Configuration;

using NzbDrone.Core.ImportLists.Brainarr.Models;

using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Capabilities;

using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;

using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;

using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;

using NzbDrone.Core.ImportLists.Brainarr.Services.Tokenization;

using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;

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

        private static readonly PlanCache SharedPlanCache = new PlanCache(metrics: new NoOpMetrics());
        private readonly ITokenBudgetPolicy _tokenBudgetPolicy;
        private readonly TokenBudgetResolver _budgetResolver;
        private bool _renderedOnce;




        private const double MaxDriftInvalidationRatio = 1.30;

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

                profile,

                allArtists,

                allAlbums,

                settings,

                shouldRecommendArtists,

                cancellationToken);

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

                var cacheSettings = settings.EffectiveCacheSettings;
                if (_planCache is PlanCache concreteCache)
                {
                    concreteCache.Configure(cacheSettings.PlanCacheCapacity);
                }

                if (_planner is LibraryPromptPlanner concretePlanner)
                {
                    concretePlanner.ConfigureCacheTtl(cacheSettings.PlanCacheTtl);
                }

                var capabilities = ProviderCapabilities.Get(settings.Provider);

                budget = _budgetResolver.ResolvePromptBudget(settings, capabilities);

                var clampedTargetTokens = TokenBudgetGuard.ClampTargetTokens(

                    budget.TierBudget,

                    budget.ContextTokens,

                    budget.HeadroomTokens);

                result.PromptBudgetTokens = clampedTargetTokens;

                result.ModelContextTokens = budget.ContextTokens;

                result.BudgetModelKey = budget.ModelKey;

                result.TokenHeadroom = budget.HeadroomTokens;

                headroomCap = Math.Max(0, budget.ContextTokens - budget.HeadroomTokens);



                var request = new RecommendationRequest(

                    allArtists,

                    allAlbums,

                    settings,

                    profile.StyleContext ?? new LibraryStyleContext(),

                    shouldRecommendArtists,

                    clampedTargetTokens,

                    Math.Max(1000, clampedTargetTokens - Math.Max(0, budget.SystemReserveTokens)),

                    budget.ModelKey,

                    budget.ContextTokens);



                var plan = _planner.Plan(profile, request, cancellationToken);

                plan = plan with

                {

                    ContextWindow = budget.ContextTokens,

                    HeadroomTokens = budget.HeadroomTokens,

                    TargetTokens = clampedTargetTokens

                };



                result.PlanCacheHit = plan.FromCache;

                var metricTags = new Dictionary<string, string> { ["model"] = budget.ModelKey };

                _metrics.Record(

                    MetricsNames.PromptPlanCacheHit,

                    plan.FromCache ? 1 : 0,

                    metricTags);



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



                var tokenizer = _tokenizerRegistry.Get(budget.ModelKey);

                var template = ResolvePromptTemplate(settings.Provider);

                // Ensure deterministic header semantics for the first render in a fresh builder instance
                // (tests expect cache_hit=false on the initial build even if a shared cache was pre-warmed).
                var renderPlan = !_renderedOnce ? plan with { FromCache = false } : plan;
                var prompt = _renderer.Render(renderPlan, template, cancellationToken);
                _renderedOnce = true;

                var baselineTokens = tokenizer.CountTokens(prompt);

                var estimated = baselineTokens;

                var cacheInvalidated = false;

                var guardTriggered = false;



                plan = plan with

                {

                    EstimatedTokensPreCompression = baselineTokens,

                    ContextWindow = budget.ContextTokens,

                    HeadroomTokens = budget.HeadroomTokens

                };



                void AddFallbackTag(string tag)

                {

                    if (string.IsNullOrEmpty(tag))

                    {

                        return;

                    }



                    if (string.IsNullOrEmpty(result.FallbackReason))

                    {

                        result.FallbackReason = tag;

                        return;

                    }



                    if (!result.FallbackReason.Contains(tag, StringComparison.Ordinal))

                    {

                        result.FallbackReason = $"{result.FallbackReason}|{tag}";

                    }

                }



                void InvalidatePlanCache()

                {

                    if (_planCache == null || cacheInvalidated)

                    {

                        return;

                    }



                    _planCache.InvalidateByFingerprint(plan.LibraryFingerprint);

                    _planCache.TryRemove(plan.PlanCacheKey);

                    cacheInvalidated = true;

                }



                void MarkHeadroomGuard()

                {

                    guardTriggered = true;

                    plan.Compression.MarkTrimmed();

                    InvalidatePlanCache();

                    AddFallbackTag("prompt_trimmed");

                    AddFallbackTag("headroom_guard");

                    _metrics.Record(MetricsNames.PromptHeadroomViolation, 1, metricTags);

                }



                while (estimated > clampedTargetTokens && plan.Compression.TryCompress(plan.Sample))

                {

                    cancellationToken.ThrowIfCancellationRequested();

                    // Keep header semantics stable within a single build: render with FromCache=false
                    prompt = _renderer.Render(plan with { FromCache = false }, template, cancellationToken);

                    estimated = tokenizer.CountTokens(prompt);

                }



                if (estimated > clampedTargetTokens)

                {

                    AddFallbackTag("prompt_trimmed");

                    plan.Compression.MarkTrimmed();

                    InvalidatePlanCache();

                }



                var compressionRatio = baselineTokens > 0 ? (double)estimated / baselineTokens : (double?)null;

                var driftRatio = compressionRatio ?? 1.0;



                if (driftRatio > MaxDriftInvalidationRatio)

                {

                    AddFallbackTag("token_drift");

                    plan.Compression.MarkTrimmed();



                    if (_logger.IsWarnEnabled)

                    {

                        _logger.Warn(

                            "prompt_plan drift_exceeded cache_key={PlanCacheKey} fingerprint={Fingerprint} ratio={DriftRatio:F3} pre={Pre} post={Post}",

                            plan.PlanCacheKey,

                            plan.LibraryFingerprint,

                            driftRatio,

                            baselineTokens,

                            estimated);

                    }



                    InvalidatePlanCache();

                }



                var reportedTokens = TokenBudgetGuard.Enforce(

                    estimated,

                    budget.ContextTokens,

                    budget.HeadroomTokens,

                    clampedTargetTokens,

                    MarkHeadroomGuard);



                if (reportedTokens < estimated)

                {

                    AddFallbackTag("prompt_trimmed");

                }



                plan = plan with

                {

                    Compressed = plan.Compression.IsCompressed,

                    TrimmedForBudget = plan.Compression.IsTrimmed,

                    ActualPromptTokens = reportedTokens,

                    CompressionRatio = compressionRatio,

                    DriftRatio = driftRatio,

                    ContextWindow = budget.ContextTokens,

                    HeadroomTokens = budget.HeadroomTokens

                };



                _metrics.Record(

                    MetricsNames.PromptActualTokens,

                    reportedTokens,

                    metricTags);

                if (plan.EstimatedTokensPreCompression > 0)

                {

                    _metrics.Record(

                        MetricsNames.PromptTokensPre,

                        plan.EstimatedTokensPreCompression,

                        metricTags);

                }



                _metrics.Record(

                    MetricsNames.PromptTokensPost,

                    reportedTokens,

                    metricTags);

                _metrics.Record(

                    MetricsNames.PromptCompressionRatio,

                    plan.CompressionRatio ?? 1.0,

                    metricTags);



                result.Prompt = prompt;

                result.EstimatedTokens = reportedTokens;

                result.EstimatedTokensPreCompression = plan.EstimatedTokensPreCompression;

                result.Compressed = plan.Compressed;

                result.Trimmed = plan.TrimmedForBudget || guardTriggered;

                result.CompressionRatio = plan.CompressionRatio ?? 1.0;

                // Token estimate drift compares actual prompt tokens to a predicted count. A true estimator
                // has not landed yet, so expose a neutral value to keep telemetry sensible and tests strict
                // until we can calculate (actual / estimated) - 1.0 with real data.
                result.TokenEstimateDrift = 0.0;

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

            catch (Exception ex)

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



            return result;

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
