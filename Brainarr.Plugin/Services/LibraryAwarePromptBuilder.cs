using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Tokenization;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using RegistryModelRegistryLoader = NzbDrone.Core.ImportLists.Brainarr.Services.Registry.ModelRegistryLoader;
using NzbDrone.Core.Music;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using StableHashResult = NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.LibraryPromptPlanner.StableHashResult;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Builds rich, library-grounded prompts for AI providers. Prompts honour user-selected
    /// styles, stay within model-specific token budgets, and expose telemetry for diagnostics.
    /// </summary>
    public class LibraryAwarePromptBuilder : ILibraryAwarePromptBuilder
    {
        private readonly Logger _logger;
        private readonly RegistryModelRegistryLoader _modelRegistryLoader;
        private readonly string? _registryUrl;
        private readonly Lazy<Dictionary<string, ModelContextInfo>> _modelContextCache;
        private readonly ITokenizerRegistry _tokenizerRegistry;
        private readonly IPromptPlanner _planner;
        private readonly IPromptRenderer _renderer;
        private readonly IPlanCache _planCache;

        private const int SystemPromptReserve = 1200;
        private const double CompletionReserveRatio = 0.20;
        private const double SafetyMarginRatio = 0.10;
        private const double MinimalRatio = 0.35;
        private const double BalancedRatio = 0.60;
        private const double ComprehensiveRatio = 1.00;
        private const int MinimalPromptFloor = 1500;

        private static readonly Dictionary<AIProvider, int> DefaultContextTokens = new()
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
        private static DateTime NormalizeAddedDate(DateTime value)
        {
            return value == default ? DateTime.MinValue : value;
        }

        public LibraryAwarePromptBuilder(Logger logger)
            : this(
                logger,
                new StyleCatalogService(logger, httpClient: null),
                new RegistryModelRegistryLoader(),
                new ModelTokenizerRegistry(),
                registryUrl: null,
                promptPlanner: null,
                promptRenderer: null,
                planCache: null)
        {
        }

        public LibraryAwarePromptBuilder(
            Logger logger,
            IStyleCatalogService styleCatalog,
            RegistryModelRegistryLoader modelRegistryLoader,
            ITokenizerRegistry tokenizerRegistry,
            string? registryUrl = null,
            IPromptPlanner? promptPlanner = null,
            IPromptRenderer? promptRenderer = null,
            IPlanCache? planCache = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            if (styleCatalog == null)
            {
                throw new ArgumentNullException(nameof(styleCatalog));
            }
            _modelRegistryLoader = modelRegistryLoader ?? throw new ArgumentNullException(nameof(modelRegistryLoader));
            _registryUrl = string.IsNullOrWhiteSpace(registryUrl) ? null : registryUrl.Trim();
            _modelContextCache = new Lazy<Dictionary<string, ModelContextInfo>>(() => LoadModelContextCache(_registryUrl), isThreadSafe: true);
            _tokenizerRegistry = tokenizerRegistry ?? new ModelTokenizerRegistry();
            _planCache = planCache ?? new PlanCache();
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

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var budget = ResolvePromptBudget(settings);
                result.PromptBudgetTokens = budget.TierBudget;
                result.ModelContextTokens = budget.ContextTokens;
                result.BudgetModelKey = budget.ModelKey;
                result.TokenHeadroom = budget.HeadroomTokens;

                var request = new RecommendationRequest(
                    allArtists,
                    allAlbums,
                    settings,
                    profile.StyleContext ?? new LibraryStyleContext(),
                    shouldRecommendArtists,
                    budget.TierBudget,
                    Math.Max(1000, budget.TierBudget - SystemPromptReserve),
                    budget.ModelKey,
                    budget.ContextTokens);

                var plan = _planner.Plan(profile, request, cancellationToken);
                plan = plan with
                {
                    ContextWindow = budget.ContextTokens,
                    HeadroomTokens = budget.HeadroomTokens,
                    TargetTokens = budget.TierBudget
                };

                result.PlanCacheHit = plan.FromCache;
                MetricsCollector.RecordMetric(
                    "prompt.plan_cache_hit",
                    plan.FromCache ? 1 : 0,
                    new Dictionary<string, string> { ["model"] = budget.ModelKey });

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
                var prompt = _renderer.Render(plan, ModelPromptTemplate.Default, cancellationToken);
                var baselineTokens = tokenizer.CountTokens(prompt);
                var estimated = baselineTokens;

                plan = plan with
                {
                    EstimatedTokensPreCompression = baselineTokens,
                    ContextWindow = budget.ContextTokens,
                    HeadroomTokens = budget.HeadroomTokens
                };

                while (estimated > budget.TierBudget && plan.Compression.TryCompress(plan.Sample))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    prompt = _renderer.Render(plan, ModelPromptTemplate.Default, cancellationToken);
                    estimated = tokenizer.CountTokens(prompt);
                }

                if (estimated > budget.TierBudget)
                {
                    result.FallbackReason = "prompt_trimmed";
                    plan.Compression.MarkTrimmed();
                    _planCache.InvalidateByFingerprint(plan.LibraryFingerprint);
                }

                var compressionRatio = baselineTokens > 0 ? (double)estimated / baselineTokens : (double?)null;
                var driftRatio = compressionRatio ?? 1.0;

                plan = plan with
                {
                    Compressed = plan.Compression.IsCompressed,
                    TrimmedForBudget = plan.Compression.IsTrimmed,
                    ActualPromptTokens = estimated,
                    CompressionRatio = compressionRatio,
                    DriftRatio = driftRatio,
                    ContextWindow = budget.ContextTokens,
                    HeadroomTokens = budget.HeadroomTokens
                };

                MetricsCollector.RecordMetric(
                    "prompt.actual_tokens",
                    estimated,
                    new Dictionary<string, string> { ["model"] = budget.ModelKey });
                MetricsCollector.RecordMetric(
                    "prompt.compression_ratio",
                    plan.DriftRatio,
                    new Dictionary<string, string> { ["model"] = budget.ModelKey });

                result.Prompt = prompt;
                result.EstimatedTokens = estimated;
                result.EstimatedTokensPreCompression = plan.EstimatedTokensPreCompression;
                result.Compressed = plan.Compressed;
                result.Trimmed = plan.TrimmedForBudget;
                result.CompressionRatio = plan.CompressionRatio ?? 1.0;
                result.TokenEstimateDrift = plan.EstimatedTokensPreCompression > 0
                    ? plan.DriftRatio - 1.0
                    : 0.0;

                if (_logger.IsInfoEnabled)
                {
                    _logger.Info(
                        "prompt_plan seed={Seed} model={Model} budget={Budget} compressed={Compressed} trimmed={Trimmed} sparse={Sparse} cache_hit={CacheHit} comp_ratio={Drift:F3} pre={Pre} post={Post}",
                        result.SampleSeed,
                        result.BudgetModelKey,
                        budget.TierBudget,
                        result.Compressed,
                        result.Trimmed,
                        result.StyleCoverageSparse,
                        result.PlanCacheHit,
                        plan.DriftRatio,
                        plan.EstimatedTokensPreCompression,
                        result.EstimatedTokens);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to build library-aware prompt, falling back to baseline prompt");
                var prompt = BuildFallbackPrompt(profile, settings);
                result.Prompt = prompt;
                result.SampledArtists = 0;
                result.SampledAlbums = 0;
                result.EstimatedTokens = EstimateTokens(prompt, null);
                result.FallbackReason = "fallback_prompt";
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

            var budget = ResolvePromptBudget(settings);
            return budget.TierBudget;
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
        private PromptBudget ResolvePromptBudget(BrainarrSettings settings)
        {
            var modelInfo = ResolveModelContext(settings);
            var contextTokens = modelInfo.ContextTokens > 0
                ? modelInfo.ContextTokens
                : DefaultContextTokens.TryGetValue(settings.Provider, out var fallback)
                    ? fallback
                    : 24000;

            var completionReserve = Math.Max(512, (int)Math.Floor(contextTokens * CompletionReserveRatio));
            var headroomReserve = Math.Max(256, (int)Math.Floor(contextTokens * SafetyMarginRatio));
            var promptBudget = Math.Max(MinimalPromptFloor, contextTokens - SystemPromptReserve - completionReserve - headroomReserve);
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

            return new PromptBudget
            {
                ContextTokens = contextTokens,
                PromptTokens = promptBudget,
                TierBudget = tierBudget,
                ModelKey = modelInfo.ModelKey,
                RawModelId = modelInfo.RawModelId,
                HeadroomTokens = headroomReserve
            };
        }

        private static int GetProviderPromptCeiling(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.Ollama or AIProvider.LMStudio => int.MaxValue,
                _ => 20000
            };
        }

        private ModelContextInfo ResolveModelContext(BrainarrSettings settings)
        {
            var providerSlug = MapProviderToRegistrySlug(settings.Provider);
            if (providerSlug == null)
            {
                return new ModelContextInfo();
            }

            var rawModelId = ResolveRawModelId(settings.Provider, settings);
            if (string.IsNullOrWhiteSpace(rawModelId))
            {
                return new ModelContextInfo { ModelKey = providerSlug + ":default", RawModelId = rawModelId ?? string.Empty };
            }

            var key = BuildModelCacheKey(providerSlug, rawModelId);
            if (_modelContextCache.Value.TryGetValue(key, out var info))
            {
                return info with { RawModelId = rawModelId };
            }

            return new ModelContextInfo
            {
                ContextTokens = 0,
                ModelKey = key,
                RawModelId = rawModelId
            };
        }

        private string ResolveRawModelId(AIProvider provider, BrainarrSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.ManualModelId))
            {
                return settings.ManualModelId.Trim();
            }

            var friendly = settings.ModelSelection;
            var normalized = ProviderModelNormalizer.Normalize(provider, friendly);
            var providerSlug = MapProviderToRegistrySlug(provider);
            if (string.IsNullOrEmpty(providerSlug))
            {
                return normalized;
            }

            try
            {
                return ModelIdMapper.ToRawId(providerSlug, normalized);
            }
            catch
            {
                return normalized;
            }
        }

        private static string BuildModelCacheKey(string providerSlug, string rawModelId)
        {
            return ($"{providerSlug}:{rawModelId}").ToLowerInvariant();
        }

        private static string? MapProviderToRegistrySlug(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.OpenAI => "openai",
                AIProvider.Perplexity => "perplexity",
                AIProvider.Anthropic => "anthropic",
                AIProvider.OpenRouter => "openrouter",
                AIProvider.DeepSeek => "deepseek",
                AIProvider.Gemini => "gemini",
                AIProvider.Groq => "groq",
                AIProvider.Ollama => "ollama",
                AIProvider.LMStudio => "lmstudio",
                _ => null
            };
        }
        public int ComputeSamplingSeed(
        LibraryProfile profile,
        BrainarrSettings settings,
        bool shouldRecommendArtists,
        CancellationToken cancellationToken = default)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var components = new List<string>
            {
                settings.Provider.ToString(),
                settings.SamplingStrategy.ToString(),
                settings.DiscoveryMode.ToString(),
                settings.MaxRecommendations.ToString(CultureInfo.InvariantCulture),
                shouldRecommendArtists ? "artist-mode" : "album-mode",
                profile.TotalArtists.ToString(CultureInfo.InvariantCulture),
                profile.TotalAlbums.ToString(CultureInfo.InvariantCulture)
            };

            if (profile.StyleContext?.DominantStyles?.Count > 0)
            {
                components.AddRange(profile.StyleContext.DominantStyles.OrderBy(s => s, StringComparer.Ordinal));
            }

            var styleFilters = settings.StyleFilters?.Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            if (styleFilters?.Length > 0)
            {
                components.AddRange(styleFilters.OrderBy(s => s, StringComparer.Ordinal));
            }

            if (profile.TopArtists?.Any() == true)
            {
                components.AddRange(profile.TopArtists.OrderBy(a => a, StringComparer.Ordinal));
            }

            if (profile.TopGenres?.Any() == true)
            {
                foreach (var kvp in profile.TopGenres.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    components.Add($"{kvp.Key}:{kvp.Value.ToString(CultureInfo.InvariantCulture)}");
                }
            }

            if (profile.RecentlyAdded?.Any() == true)
            {
                components.AddRange(profile.RecentlyAdded.OrderBy(item => item, StringComparer.Ordinal));
            }

            if (profile.Metadata?.Any() == true)
            {
                foreach (var kvp in profile.Metadata.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    components.Add($"{kvp.Key}:{ConvertMetadataValue(kvp.Value)}");
                }
            }

            var hashResult = LibraryPromptPlanner.ComputeStableHash(components);
            _logger.Trace(
                "Computed sampling seed from {ComponentCount} components (hash prefix {HashPrefix}) => {Seed}",
                hashResult.ComponentCount,
                hashResult.HashPrefix,
                hashResult.Seed);

            return hashResult.Seed;
        }

        internal static StableHashResult ComputeStableHash(IEnumerable<string> components)
        {
            return LibraryPromptPlanner.ComputeStableHash(components);
        }

        private static string ConvertMetadataValue(object? value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is string str)
            {
                return str;
            }

            if (value is IDictionary dictionary)
            {
                var entries = new List<string>();
                foreach (DictionaryEntry entry in dictionary)
                {
                    var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
                    entries.Add($"{key}:{ConvertMetadataValue(entry.Value)}");
                }

                entries.Sort(StringComparer.Ordinal);
                return string.Join("|", entries);
            }

            if (value is IEnumerable enumerable)
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                {
                    items.Add(ConvertMetadataValue(item));
                }

                items.Sort(StringComparer.Ordinal);
                return string.Join("|", items);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private sealed record ModelContextInfo
        {
            public int ContextTokens { get; init; }
            public string ModelKey { get; init; } = string.Empty;
            public string RawModelId { get; init; } = string.Empty;
        }

        private sealed record PromptBudget
        {
            public int ContextTokens { get; init; }
            public int PromptTokens { get; init; }
            public int TierBudget { get; init; }
            public string ModelKey { get; init; } = string.Empty;
            public string RawModelId { get; init; } = string.Empty;
            public int HeadroomTokens { get; init; }
        }
        private Dictionary<string, ModelContextInfo> LoadModelContextCache(string? registryUrl)
        {
            var map = new Dictionary<string, ModelContextInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var result = _modelRegistryLoader.LoadAsync(registryUrl, default).GetAwaiter().GetResult();
                var registry = result.Registry;
                if (registry == null)
                {
                    return map;
                }

                foreach (var provider in registry.Providers)
                {
                    var slug = ExtractProviderSlug(provider);
                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        continue;
                    }

                    foreach (var model in provider.Models)
                    {
                        if (string.IsNullOrWhiteSpace(model.Id) || model.ContextTokens <= 0)
                        {
                            continue;
                        }

                        var key = BuildModelCacheKey(slug, model.Id);
                        map[key] = new ModelContextInfo
                        {
                            ContextTokens = model.ContextTokens,
                            ModelKey = key,
                            RawModelId = model.Id
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to load model registry context tokens; using defaults.");
            }

            return map;
        }


        private static string? ExtractProviderSlug(ModelRegistry.ProviderDescriptor provider)
        {
            if (provider == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(provider.Slug))
            {
                return provider.Slug.Trim();
            }

            if (!string.IsNullOrWhiteSpace(provider.Name))
            {
                return provider.Name.Trim();
            }

            return null;
        }








        private string BuildFallbackPrompt(LibraryProfile profile, BrainarrSettings settings)
        {
            return $@"Based on this music library, recommend {settings.MaxRecommendations} new albums to discover:

Library: {profile.TotalArtists} artists, {profile.TotalAlbums} albums
Top genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => $"{g.Key} ({g.Value})"))}
Sample artists: {string.Join(", ", profile.TopArtists.Take(10))}

Return a JSON array with exactly {settings.MaxRecommendations} recommendations.
Each item must have: artist, album, genre, confidence (0.0-1.0), reason (brief).

Focus on: {GetDiscoveryFocus(settings.DiscoveryMode)}

Example format:
[
  {{""artist"": ""Artist Name"", ""album"": ""Album Title"", ""genre"": ""Genre"", ""confidence"": 0.8, ""reason"": ""Similar to your jazz collection""}}
]";
        }

        private string GetDiscoveryFocus(DiscoveryMode mode)
        {
            return mode switch
            {
                DiscoveryMode.Similar => "artists very similar to the library",
                DiscoveryMode.Adjacent => "artists in related genres",
                DiscoveryMode.Exploratory => "new genres and styles to explore",
                _ => "balanced recommendations"
            };
        }

        private string GetDiscoveryModeTemplate(DiscoveryMode mode, int maxRecommendations, bool artists, bool styleLocked)
        {
            var target = artists ? "artists" : "albums";
            return mode switch
            {
                DiscoveryMode.Similar when styleLocked =>
                    $@"You are a music connoisseur tasked with finding {maxRecommendations} {target} that sit inside the listener's existing style cluster.
OBJECTIVE: Recommend {target} that have tangible ties to the user's current collection (collaborators, side projects, labelmates, shared producers, or historically linked acts).
- Stay inside the listed styles unless explicitly told otherwise.
- Highlight the concrete connection for every recommendation.
- Avoid generic genre matches without specific adjacency.",

                DiscoveryMode.Similar =>
                    $@"You are a music connoisseur tasked with finding {maxRecommendations} {target} that perfectly match this user's established taste.
OBJECTIVE: Recommend {target} from the exact same subgenres and scenes already in the collection.
- Look for artists frequently mentioned alongside their favourites.
- Match production styles, era, and sonic characteristics precisely.
- Prioritise artists who have collaborated with or influenced their collection.",

                DiscoveryMode.Adjacent =>
                    $@"You are a music discovery expert helping expand this user's horizons into ADJACENT musical territories.
OBJECTIVE: Recommend {target} that bridge from their core collection into closely related scenes.
- Use gateway releases that share personnel, labels, or stylistic DNA.
- Explain why each recommendation is a comfortable stretch rather than a leap.",

                DiscoveryMode.Exploratory =>
                    $@"You are a bold music curator introducing this user to completely NEW musical experiences.
OBJECTIVE: Recommend {target} from genres outside their current collection but with a compelling reason to explore.
- Provide accessible entry points into new styles.
- Highlight cultural or historical relevance that might resonate with the listener.",

                _ => $"Analyze this music library and recommend {maxRecommendations} NEW {target} that would enhance the collection."
            };
        }

        private string GetSamplingStrategyPreamble(SamplingStrategy strategy)
        {
            return strategy switch
            {
                SamplingStrategy.Minimal =>
                    @"CONTEXT SCOPE: You have been provided with a brief summary of the user's top artists and genres.
Based on this limited information, provide broad recommendations that align with their core tastes.",

                SamplingStrategy.Comprehensive =>
                    @"CONTEXT SCOPE: You have been provided with a highly detailed and comprehensive analysis of the user's music library.
Use ALL available details (genre distributions, collection patterns, temporal preferences, completionist behaviour) to generate deeply personalised recommendations.",

                SamplingStrategy.Balanced =>
                    @"CONTEXT SCOPE: You have been provided with a balanced overview of the user's library including key artists, genre preferences, and collection patterns.
Use this information to provide well-informed recommendations that respect their established taste while offering meaningful discovery.",

                _ => string.Empty
            };
        }
        private string BuildEnhancedCollectionContext(LibraryProfile profile)
        {
            var context = new StringBuilder();

            var collectionSize = profile.Metadata?.ContainsKey("CollectionSize") == true
                ? profile.Metadata["CollectionSize"].ToString()
                : "established";

            var collectionFocus = profile.Metadata?.ContainsKey("CollectionFocus") == true
                ? profile.Metadata["CollectionFocus"].ToString()
                : "general";

            context.AppendLine($"• Size: {collectionSize} ({profile.TotalArtists} artists, {profile.TotalAlbums} albums)");

            if (profile.Metadata?.ContainsKey("GenreDistribution") == true &&
                profile.Metadata["GenreDistribution"] is Dictionary<string, double> genreDistribution &&
                genreDistribution.Any())
            {
                var topGenres = string.Join(", ", genreDistribution
                    .Where(kv => !kv.Key.EndsWith("_significance", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(kv => kv.Value)
                    .Take(5)
                    .Select(kv => $"{kv.Key} ({kv.Value:F1}%)"));
                context.AppendLine($"• Genres: {topGenres}");
            }
            else
            {
                context.AppendLine($"• Genres: {string.Join(", ", profile.TopGenres.Take(5).Select(g => g.Key))}");
            }

            if (profile.Metadata?.ContainsKey("CollectionStyle") == true)
            {
                var style = profile.Metadata["CollectionStyle"].ToString();
                var completion = profile.Metadata?.ContainsKey("CompletionistScore") == true
                    ? Convert.ToDouble(profile.Metadata["CompletionistScore"])
                    : (double?)null;
                if (completion.HasValue)
                {
                    context.AppendLine($"• Collection style: {style} (completionist score: {completion.Value:F1}%)");
                    context.AppendLine($"• Completionist score: {completion.Value:F1}%");
                }
                else
                {
                    context.AppendLine($"• Collection style: {style}");
                }
            }
            else
            {
                context.AppendLine($"• Collection style: {collectionFocus}");
            }

            if (profile.Metadata?.ContainsKey("AverageAlbumsPerArtist") == true)
            {
                var avg = Convert.ToDouble(profile.Metadata["AverageAlbumsPerArtist"]);
                context.AppendLine($"• Collection depth: avg {avg:F1} albums per artist");
            }

            return context.ToString().TrimEnd();
        }

        private string BuildMusicalDnaContext(LibraryProfile profile)
        {
            var context = new StringBuilder();

            if (profile.Metadata?.ContainsKey("PreferredEras") == true &&
                profile.Metadata["PreferredEras"] is List<string> eras && eras.Any())
            {
                context.AppendLine($"• Era preference: {string.Join(", ", eras)}");
            }

            if (profile.Metadata?.ContainsKey("AlbumTypes") == true &&
                profile.Metadata["AlbumTypes"] is Dictionary<string, int> albumTypes && albumTypes.Any())
            {
                var topTypes = string.Join(", ", albumTypes
                    .OrderByDescending(kv => kv.Value)
                    .Take(3)
                    .Select(kv => $"{kv.Key} ({kv.Value})"));
                context.AppendLine($"• Album types: {topTypes}");
            }

            if (profile.Metadata?.ContainsKey("NewReleaseRatio") == true)
            {
                var ratio = Convert.ToDouble(profile.Metadata["NewReleaseRatio"]);
                var interest = ratio > 0.3 ? "High" : ratio > 0.15 ? "Moderate" : "Low";
                context.AppendLine($"• New release interest: {interest} ({ratio:P0} recent)");
            }

            if (profile.RecentlyAdded != null && profile.RecentlyAdded.Any())
            {
                var recent = string.Join(", ", profile.RecentlyAdded.Take(10));
                context.AppendLine($"• Recently added artists: {recent}");
            }

            return context.ToString().TrimEnd();
        }

        private string BuildCollectionPatterns(LibraryProfile profile)
        {
            var context = new StringBuilder();

            if (profile.Metadata?.ContainsKey("DiscoveryTrend") == true)
            {
                context.AppendLine($"• Discovery trend: {profile.Metadata["DiscoveryTrend"]}");
            }

            if (profile.Metadata?.ContainsKey("CollectionCompleteness") == true)
            {
                var completeness = Convert.ToDouble(profile.Metadata["CollectionCompleteness"]);
                var quality = completeness > 0.8 ? "Very High" : completeness > 0.6 ? "High" : completeness > 0.4 ? "Moderate" : "Building";
                context.AppendLine($"• Collection quality: {quality} ({completeness:P0} complete)");
            }

            if (profile.Metadata?.ContainsKey("MonitoredRatio") == true)
            {
                var ratio = Convert.ToDouble(profile.Metadata["MonitoredRatio"]);
                context.AppendLine($"• Active tracking: {ratio:P0} of collection");
            }

            if (profile.Metadata?.ContainsKey("TopCollectedArtistNames") == true &&
                profile.Metadata["TopCollectedArtistNames"] is Dictionary<string, int> nameCounts && nameCounts.Any())
            {
                var line = string.Join(", ", nameCounts
                    .OrderByDescending(kv => kv.Value)
                    .Take(5)
                    .Select(kv => $"{kv.Key} ({kv.Value})"));
                context.AppendLine($"• Top collected artists: {line}");
            }

            return context.ToString().TrimEnd();
        }

        private string GetCollectionCharacter(LibraryProfile profile)
        {
            if (profile.Metadata?.ContainsKey("CollectionFocus") == true)
            {
                return profile.Metadata["CollectionFocus"].ToString();
            }
            return "balanced";
        }

        private string GetTemporalPreference(LibraryProfile profile)
        {
            if (profile.Metadata?.ContainsKey("PreferredEras") == true &&
                profile.Metadata["PreferredEras"] is List<string> eras && eras.Any())
            {
                return string.Join("/", eras).ToLowerInvariant();
            }
            return "mixed era";
        }

        private string GetDiscoveryTrend(LibraryProfile profile)
        {
            if (profile.Metadata?.ContainsKey("DiscoveryTrend") == true)
            {
                return profile.Metadata["DiscoveryTrend"].ToString();
            }
            return "steady";
        }
        

    }
}
