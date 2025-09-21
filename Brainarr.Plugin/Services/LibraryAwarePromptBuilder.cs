using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Tokenization;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using RegistryModelRegistryLoader = NzbDrone.Core.ImportLists.Brainarr.Services.Registry.ModelRegistryLoader;
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
        private readonly IStyleCatalogService _styleCatalog;
        private readonly RegistryModelRegistryLoader _modelRegistryLoader;
        private readonly string? _registryUrl;
        private readonly Lazy<Dictionary<string, ModelContextInfo>> _modelContextCache;
        private readonly ITokenizerRegistry _tokenizerRegistry;

        private const int SystemPromptReserve = 1200;
        private const double CompletionReserveRatio = 0.20;
        private const double SafetyMarginRatio = 0.10;
        private const double MinimalRatio = 0.35;
        private const double BalancedRatio = 0.60;
        private const double ComprehensiveRatio = 1.00;
        private const int MinimalPromptFloor = 1500;
        private const int SparseStyleArtistThreshold = 5;
        private const double RelaxedMatchThreshold = 0.70;
        private const double MaxRelaxedInflation = 3.0;

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

        private static void ShuffleInPlace<T>(IList<T> list, Random rng)
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }


        public LibraryAwarePromptBuilder(Logger logger)
            : this(
                logger,
                new StyleCatalogService(logger, httpClient: null),
                new RegistryModelRegistryLoader(),
                new ModelTokenizerRegistry(),
                registryUrl: null)
        {
        }

        public LibraryAwarePromptBuilder(
            Logger logger,
            IStyleCatalogService styleCatalog,
            RegistryModelRegistryLoader modelRegistryLoader,
            ITokenizerRegistry tokenizerRegistry,
            string? registryUrl = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _styleCatalog = styleCatalog ?? throw new ArgumentNullException(nameof(styleCatalog));
            _modelRegistryLoader = modelRegistryLoader ?? throw new ArgumentNullException(nameof(modelRegistryLoader));
            _registryUrl = string.IsNullOrWhiteSpace(registryUrl) ? null : registryUrl.Trim();
            _modelContextCache = new Lazy<Dictionary<string, ModelContextInfo>>(() => LoadModelContextCache(_registryUrl), isThreadSafe: true);
            _tokenizerRegistry = tokenizerRegistry ?? new ModelTokenizerRegistry();
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

                var styleSelection = BuildStyleSelection(profile, settings, result, cancellationToken);
                var seed = ComputeSamplingSeed(profile, allArtists, allAlbums, styleSelection, settings);
                result.SampleSeed = seed.ToString(CultureInfo.InvariantCulture);

                var sample = BuildLibrarySample(
                    allArtists,
                    allAlbums,
                    profile.StyleContext ?? new LibraryStyleContext(),
                    styleSelection,
                    settings,
                    Math.Max(1000, budget.TierBudget - SystemPromptReserve),
                    seed,
                    result,
                    cancellationToken);

                result.MatchedStyleCounts = new Dictionary<string, int>(styleSelection.MatchedCounts, StringComparer.OrdinalIgnoreCase);
                result.StyleCoverageSparse = styleSelection.Sparse;

                var fingerprint = ComputeSampleFingerprint(sample);
                result.SampleFingerprint = fingerprint;

                var plan = new PromptPlan(profile, sample, settings, styleSelection, shouldRecommendArtists, result);
                var renderer = new PromptRenderer(this);
                var tokenizer = _tokenizerRegistry.Get(budget.ModelKey);

                var prompt = renderer.Render(plan);
                var estimated = tokenizer.CountTokens(prompt);
                result.EstimatedTokensPreCompression = estimated;

                while (estimated > budget.TierBudget && plan.TryCompress())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    prompt = renderer.Render(plan);
                    estimated = tokenizer.CountTokens(prompt);
                }

                if (estimated > budget.TierBudget)
                {
                    result.FallbackReason = "prompt_trimmed";
                    plan.MarkTrimmed();
                }

                result.Prompt = prompt;
                result.EstimatedTokens = estimated;
                result.Compressed = plan.Compressed;
                result.Trimmed = plan.Trimmed;
                result.CompressionRatio = result.EstimatedTokensPreCompression > 0
                    ? (double)result.EstimatedTokens / result.EstimatedTokensPreCompression
                    : 1.0;
                result.TokenEstimateDrift = result.EstimatedTokensPreCompression > 0
                    ? (double)(result.EstimatedTokens - result.EstimatedTokensPreCompression) / result.EstimatedTokensPreCompression
                    : 0.0;

                if (_logger.IsInfoEnabled)
                {
                    _logger.Info(
                        "prompt_plan seed={Seed} model={Model} budget={Budget} compressed={Compressed} trimmed={Trimmed} sparse={Sparse}",
                        result.SampleSeed,
                        result.BudgetModelKey,
                        budget.TierBudget,
                        result.Compressed,
                        result.Trimmed,
                        result.StyleCoverageSparse);
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
        private int ComputeSamplingSeed(
            LibraryProfile profile,
            List<Artist> artists,
            List<Album> albums,
            StyleSelectionContext selection,
            BrainarrSettings settings)
        {
            var hasher = new HashCode();
            hasher.Add(profile?.TotalArtists ?? 0);
            hasher.Add(profile?.TotalAlbums ?? 0);
            hasher.Add((int)settings.DiscoveryMode);
            hasher.Add((int)settings.SamplingStrategy);

            if (selection.SelectedSlugs != null)
            {
                foreach (var slug in selection.SelectedSlugs.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                {
                    hasher.Add(slug);
                }
            }

            foreach (var id in artists.Select(a => a.Id).OrderBy(id => id).Take(24))
            {
                hasher.Add(id);
            }

            foreach (var id in albums.Select(a => a.Id).OrderBy(id => id).Take(24))
            {
                hasher.Add(id);
            }

            return hasher.ToHashCode();
        }
        private StyleSelectionContext BuildStyleSelection(
            LibraryProfile profile,
            BrainarrSettings settings,
            LibraryPromptResult metrics,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var coverage = profile?.StyleContext?.StyleCoverage ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var normalized = _styleCatalog.Normalize(settings.StyleFilters ?? Array.Empty<string>());
            var trimmed = new List<string>();

            if (normalized.Count > settings.MaxSelectedStyles)
            {
                var ordered = normalized
                    .OrderByDescending(slug => coverage.TryGetValue(slug, out var count) ? count : 0)
                    .ThenBy(slug => slug, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var keep = ordered.Take(settings.MaxSelectedStyles).ToHashSet(StringComparer.OrdinalIgnoreCase);
                trimmed = ordered.Skip(settings.MaxSelectedStyles).ToList();
                normalized = keep;
            }

            var inferred = new List<string>();
            if (normalized.Count == 0 && settings.DiscoveryMode == DiscoveryMode.Similar)
            {
                var dominant = profile?.StyleContext?.DominantStyles ?? new List<string>();
                foreach (var slug in dominant)
                {
                    if (inferred.Count >= settings.MaxSelectedStyles) break;
                    if (string.IsNullOrWhiteSpace(slug)) continue;
                    inferred.Add(slug);
                }

                if (inferred.Count > 0)
                {
                    normalized = inferred.ToHashSet(StringComparer.OrdinalIgnoreCase);
                }
            }

            var entries = normalized
                .Select(slug => _styleCatalog.GetBySlug(slug) ?? new StyleEntry { Name = slug, Slug = slug })
                .ToList();

            var relaxed = settings.RelaxStyleMatching;
            var threshold = relaxed ? RelaxedMatchThreshold : 1.0;

            var expanded = new HashSet<string>();
            var adjacent = new List<StyleEntry>();

            if (relaxed)
            {
                foreach (var slug in normalized)
                {
                    foreach (var similar in _styleCatalog.GetSimilarSlugs(slug))
                    {
                        if (similar.Score < threshold) continue;
                        if (normalized.Contains(similar.Slug)) continue;

                        if (expanded.Add(similar.Slug))
                        {
                            var entry = _styleCatalog.GetBySlug(similar.Slug);
                            if (entry != null)
                            {
                                adjacent.Add(entry);
                            }
                        }
                    }
                }
            }

            var selection = new StyleSelectionContext(
                normalized,
                expanded,
                entries,
                adjacent,
                coverage,
                relaxed,
                threshold,
                trimmed,
                inferred);

            metrics.AppliedStyleSlugs = selection.SelectedSlugs.ToList();
            metrics.AppliedStyleNames = selection.Entries.Select(e => e.Name).ToList();
            metrics.TrimmedStyles = trimmed;
            metrics.InferredStyleSlugs = inferred;
            metrics.RelaxedStyleMatching = relaxed;
            metrics.StyleCoverage = new Dictionary<string, int>(coverage, StringComparer.OrdinalIgnoreCase);

            if (selection.HasStyles)
            {
                var totalCoverage = selection.SelectedSlugs.Sum(slug => coverage.TryGetValue(slug, out var count) ? count : 0);
                if (totalCoverage < SparseStyleArtistThreshold)
                {
                    selection.Sparse = true;
                }
            }

            return selection;
        }
        private LibrarySample BuildLibrarySample(
            List<Artist> allArtists,
            List<Album> allAlbums,
            LibraryStyleContext styleContext,
            StyleSelectionContext selection,
            BrainarrSettings settings,
            int tokenBudget,
            int seed,
            LibraryPromptResult metrics,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rng = new Random(seed);
            var sample = new LibrarySample();

            var artistMatches = BuildArtistMatchList(allArtists, styleContext, selection);
            if (selection.HasStyles && artistMatches.Count < SparseStyleArtistThreshold)
            {
                selection.Sparse = true;
                var extras = allArtists
                    .Where(a => artistMatches.All(m => m.Artist.Id != a.Id))
                    .Select(a => new ArtistMatch(a, new HashSet<string>(), 0.0));
                artistMatches.AddRange(extras);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var targetArtistCount = DetermineTargetArtistCount(allArtists.Count, tokenBudget);
            var artistSamples = SampleArtists(
                artistMatches,
                allAlbums,
                selection,
                settings.DiscoveryMode,
                targetArtistCount,
                rng);
            sample.Artists.AddRange(artistSamples);
            metrics.SampledArtists = sample.ArtistCount;

            var albumMatches = BuildAlbumMatchList(allAlbums, styleContext, selection);
            if (selection.HasStyles && albumMatches.Count < SparseStyleArtistThreshold)
            {
                selection.Sparse = true;
                var extras = allAlbums
                    .Where(a => albumMatches.All(m => m.Album.Id != a.Id))
                    .Select(a => new AlbumMatch(a, new HashSet<string>(), 0.0));
                albumMatches.AddRange(extras);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var targetAlbumCount = DetermineTargetAlbumCount(allAlbums.Count, tokenBudget);
            var albumSamples = SampleAlbums(
                albumMatches,
                selection,
                settings.DiscoveryMode,
                targetAlbumCount,
                rng);
            sample.Albums.AddRange(albumSamples);
            metrics.SampledAlbums = sample.AlbumCount;

            var artistIndex = sample.Artists.ToDictionary(a => a.ArtistId);
            foreach (var album in albumSamples)
            {
                if (artistIndex.TryGetValue(album.ArtistId, out var artist))
                {
                    artist.Albums.Add(album);
                }
                else
                {
                    var synthetic = new LibrarySampleArtist
                    {
                        ArtistId = album.ArtistId,
                        Name = album.ArtistName,
                        MatchedStyles = Array.Empty<string>(),
                        MatchScore = album.MatchScore,
                        Added = album.Added,
                        Weight = 0.25
                    };
                    synthetic.Albums.Add(album);
                    sample.Artists.Add(synthetic);
                    artistIndex[synthetic.ArtistId] = synthetic;
                }
            }

            return sample;
        }
        private List<ArtistMatch> BuildArtistMatchList(List<Artist> artists, LibraryStyleContext context, StyleSelectionContext selection)
        {
            var matches = new List<ArtistMatch>(artists.Count);
            IEnumerable<Artist> candidateArtists = artists;
            IReadOnlyList<int> strictIds = Array.Empty<int>();
            IReadOnlyList<int> expandedIds = Array.Empty<int>();
            var relaxedApplied = false;
            var relaxedTrimmed = false;

            if (selection.HasStyles && context.StyleIndex != null)
            {
                strictIds = context.StyleIndex.GetArtistsForStyles(selection.SelectedSlugs);
                var candidateIds = strictIds;

                if (selection.ShouldUseRelaxedMatches)
                {
                    expandedIds = context.StyleIndex.GetArtistsForStyles(selection.ExpandedSlugs);
                    if (strictIds.Count > 0 && expandedIds.Count > strictIds.Count * MaxRelaxedInflation)
                    {
                        relaxedTrimmed = true;
                    }
                    else if (expandedIds.Count > strictIds.Count)
                    {
                        candidateIds = expandedIds;
                        relaxedApplied = expandedIds.Count > strictIds.Count;
                    }
                }

                if (candidateIds.Count == 0 && strictIds.Count == 0 && expandedIds.Count > 0)
                {
                    candidateIds = expandedIds;
                }

                if (candidateIds.Count > 0)
                {
                    var lookup = artists.ToDictionary(a => a.Id);
                    var filtered = new List<Artist>(candidateIds.Count);
                    foreach (var id in candidateIds)
                    {
                        if (lookup.TryGetValue(id, out var artist))
                        {
                            filtered.Add(artist);
                        }
                    }

                    if (filtered.Count > 0)
                    {
                        candidateArtists = filtered;
                    }
                }

                if (relaxedApplied)
                {
                    _logger.Debug(
                        "Relaxed artist style matches applied: strict={StrictCount}, relaxed={RelaxedCount}, selected=[{Selected}], expanded=[{Expanded}]",
                        strictIds.Count,
                        expandedIds.Count,
                        string.Join(", ", selection.SelectedSlugs),
                        string.Join(", ", selection.ExpandedSlugs));
                }
                else if (selection.ShouldUseRelaxedMatches && relaxedTrimmed)
                {
                    _logger.Debug(
                        "Relaxed artist style matches trimmed to maintain sparsity: strict={StrictCount}, candidate={CandidateCount}, limitFactor={Limit}, slugs=[{Expanded}]",
                        strictIds.Count,
                        expandedIds.Count,
                        MaxRelaxedInflation,
                        string.Join(", ", selection.ExpandedSlugs));
                }
                else if (!selection.ShouldUseRelaxedMatches)
                {
                    _logger.Debug(
                        "Artist style matches remain strict-only: count={StrictCount}, selected=[{Selected}]",
                        strictIds.Count,
                        string.Join(", ", selection.SelectedSlugs));
                }
            }

            foreach (var artist in candidateArtists)
            {
                var slugs = context.ArtistStyles.TryGetValue(artist.Id, out var set)
                    ? new HashSet<string>(set, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!selection.HasStyles)
                {
                    matches.Add(new ArtistMatch(artist, slugs, slugs.Count > 0 ? 1.0 : 0.0));
                    continue;
                }

                if (TryMatchStyles(slugs, selection, out var matched, out var score))
                {
                    selection.RegisterMatch(matched);
                    matches.Add(new ArtistMatch(artist, matched, score));
                }
            }

            return matches;
        }

        private List<AlbumMatch> BuildAlbumMatchList(List<Album> albums, LibraryStyleContext context, StyleSelectionContext selection)
        {
            var matches = new List<AlbumMatch>(albums.Count);
            IEnumerable<Album> candidateAlbums = albums;
            IReadOnlyList<int> strictIds = Array.Empty<int>();
            IReadOnlyList<int> expandedIds = Array.Empty<int>();
            var relaxedApplied = false;
            var relaxedTrimmed = false;

            if (selection.HasStyles && context.StyleIndex != null)
            {
                strictIds = context.StyleIndex.GetAlbumsForStyles(selection.SelectedSlugs);
                var candidateIds = strictIds;

                if (selection.ShouldUseRelaxedMatches)
                {
                    expandedIds = context.StyleIndex.GetAlbumsForStyles(selection.ExpandedSlugs);
                    if (strictIds.Count > 0 && expandedIds.Count > strictIds.Count * MaxRelaxedInflation)
                    {
                        relaxedTrimmed = true;
                    }
                    else if (expandedIds.Count > strictIds.Count)
                    {
                        candidateIds = expandedIds;
                        relaxedApplied = expandedIds.Count > strictIds.Count;
                    }
                }

                if (candidateIds.Count == 0 && strictIds.Count == 0 && expandedIds.Count > 0)
                {
                    candidateIds = expandedIds;
                }

                if (candidateIds.Count > 0)
                {
                    var lookup = albums.ToDictionary(a => a.Id);
                    var filtered = new List<Album>(candidateIds.Count);
                    foreach (var id in candidateIds)
                    {
                        if (lookup.TryGetValue(id, out var album))
                        {
                            filtered.Add(album);
                        }
                    }

                    if (filtered.Count > 0)
                    {
                        candidateAlbums = filtered;
                    }
                }

                if (relaxedApplied)
                {
                    _logger.Debug(
                        "Relaxed album style matches applied: strict={StrictCount}, relaxed={RelaxedCount}, selected=[{Selected}], expanded=[{Expanded}]",
                        strictIds.Count,
                        expandedIds.Count,
                        string.Join(", ", selection.SelectedSlugs),
                        string.Join(", ", selection.ExpandedSlugs));
                }
                else if (selection.ShouldUseRelaxedMatches && relaxedTrimmed)
                {
                    _logger.Debug(
                        "Relaxed album style matches trimmed to maintain sparsity: strict={StrictCount}, candidate={CandidateCount}, limitFactor={Limit}, slugs=[{Expanded}]",
                        strictIds.Count,
                        expandedIds.Count,
                        MaxRelaxedInflation,
                        string.Join(", ", selection.ExpandedSlugs));
                }
                else if (!selection.ShouldUseRelaxedMatches)
                {
                    _logger.Debug(
                        "Album style matches remain strict-only: count={StrictCount}, selected=[{Selected}]",
                        strictIds.Count,
                        string.Join(", ", selection.SelectedSlugs));
                }
            }

            foreach (var album in candidateAlbums)
            {
                var slugs = context.AlbumStyles.TryGetValue(album.Id, out var set)
                    ? new HashSet<string>(set, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (!selection.HasStyles)
                {
                    matches.Add(new AlbumMatch(album, slugs, slugs.Count > 0 ? 1.0 : 0.0));
                    continue;
                }

                if (TryMatchStyles(slugs, selection, out var matched, out var score))
                {
                    selection.RegisterMatch(matched);
                    matches.Add(new AlbumMatch(album, matched, score));
                }
            }

            return matches;
        }
        private bool TryMatchStyles(HashSet<string> itemSlugs, StyleSelectionContext selection, out HashSet<string> matched, out double score)
        {
            matched = new HashSet<string>();
            score = 0.0;

            if (!selection.HasStyles)
            {
                return true;
            }

            if (itemSlugs == null || itemSlugs.Count == 0)
            {
                return false;
            }

            foreach (var slug in itemSlugs)
            {
                if (selection.SelectedSlugs.Contains(slug))
                {
                    matched.Add(slug);
                    score = Math.Max(score, 1.0);
                    continue;
                }

                if (selection.Relaxed)
                {
                    foreach (var similar in _styleCatalog.GetSimilarSlugs(slug))
                    {
                        if (selection.SelectedSlugs.Contains(similar.Slug) && similar.Score >= selection.Threshold)
                        {
                            matched.Add(similar.Slug);
                            score = Math.Max(score, similar.Score);
                            break;
                        }
                    }
                }
            }

            if (matched.Count == 0)
            {
                return false;
            }

            if (!selection.Relaxed)
            {
                return true;
            }

            return score >= selection.Threshold;
        }
        private int DetermineTargetArtistCount(int totalArtists, int tokenBudget)
        {
            if (totalArtists <= 50)
            {
                return Math.Min(40, totalArtists);
            }

            if (totalArtists <= 200)
            {
                return Math.Min(60, Math.Max(30, totalArtists / 2));
            }

            return Math.Min(90, Math.Max(32, tokenBudget / 260));
        }

        private int DetermineTargetAlbumCount(int totalAlbums, int tokenBudget)
        {
            if (totalAlbums <= 120)
            {
                return Math.Min(100, totalAlbums);
            }

            if (totalAlbums <= 400)
            {
                return Math.Min(160, Math.Max(60, totalAlbums / 2));
            }

            return Math.Min(220, Math.Max(70, tokenBudget / 120));
        }
        private List<LibrarySampleArtist> SampleArtists(
            List<ArtistMatch> matches,
            List<Album> allAlbums,
            StyleSelectionContext selection,
            DiscoveryMode mode,
            int targetCount,
            Random rng)
        {
            var result = new List<LibrarySampleArtist>();
            if (matches.Count == 0 || targetCount <= 0)
            {
                return result;
            }

            var albumCounts = allAlbums
                .GroupBy(a => a.ArtistId)
                .ToDictionary(g => g.Key, g => g.Count());

            var used = new HashSet<int>();

            void AddRange(IEnumerable<ArtistMatch> source)
            {
                foreach (var match in source)
                {
                    if (used.Contains(match.Artist.Id)) continue;
                    var sampleArtist = CreateSampleArtist(match, albumCounts);
                    result.Add(sampleArtist);
                    used.Add(match.Artist.Id);
                    if (result.Count >= targetCount) break;
                }
            }

            var topPct = mode == DiscoveryMode.Similar ? 60 : mode == DiscoveryMode.Adjacent ? 45 : 35;
            var recentPct = mode == DiscoveryMode.Similar ? 30 : mode == DiscoveryMode.Adjacent ? 35 : 35;
            var randomPct = Math.Max(0, 100 - topPct - recentPct);

            var topCount = Math.Max(1, targetCount * topPct / 100);
            AddRange(matches
                .OrderByDescending(m => m.Score)
                .ThenByDescending(m => albumCounts.TryGetValue(m.Artist.Id, out var count) ? count : 0)
                .ThenBy(m => m.Artist.Name, StringComparer.OrdinalIgnoreCase)
                .Take(topCount));

            if (result.Count < targetCount)
            {
                var recentCount = Math.Max(1, targetCount * recentPct / 100);
                AddRange(matches
                    .Where(m => !used.Contains(m.Artist.Id))
                    .OrderByDescending(m => NormalizeAddedDate(m.Artist.Added))
                    .Take(recentCount));
            }

            if (result.Count < targetCount && randomPct > 0)
            {
                var remaining = matches
                    .Where(m => !used.Contains(m.Artist.Id))
                    .ToList();
                ShuffleInPlace(remaining, rng);
                AddRange(remaining.Take(Math.Max(0, targetCount - result.Count)));
            }

            return result;
        }

        private LibrarySampleArtist CreateSampleArtist(ArtistMatch match, Dictionary<int, int> albumCounts)
        {
            var count = albumCounts.TryGetValue(match.Artist.Id, out var albums) ? albums : 0;
            var weight = ComputeArtistWeight(match.Artist, count, match.Score);

            return new LibrarySampleArtist
            {
                ArtistId = match.Artist.Id,
                Name = string.IsNullOrWhiteSpace(match.Artist.Name) ? $"Artist {match.Artist.Id}" : match.Artist.Name,
                MatchedStyles = match.MatchedStyles.ToArray(),
                MatchScore = match.Score,
                Added = match.Artist.Added,
                Weight = weight
            };
        }

        private double ComputeArtistWeight(Artist artist, int albumCount, double matchScore)
        {
            var added = artist.Added;
            var recency = added == default
                ? 0.5
                : Math.Max(0.2, 12.0 / Math.Max(1.0, (DateTime.UtcNow - added).TotalDays / 30.0));
            var depth = Math.Log(Math.Max(1, albumCount) + 1);
            return (matchScore * 1.5) + recency + depth;
        }

        private List<LibrarySampleAlbum> SampleAlbums(
            List<AlbumMatch> matches,
            StyleSelectionContext selection,
            DiscoveryMode mode,
            int targetCount,
            Random rng)
        {
            var result = new List<LibrarySampleAlbum>();
            if (matches.Count == 0 || targetCount <= 0)
            {
                return result;
            }

            var used = new HashSet<int>();

            void AddRange(IEnumerable<AlbumMatch> source)
            {
                foreach (var match in source)
                {
                    if (used.Contains(match.Album.Id)) continue;
                    var sample = new LibrarySampleAlbum
                    {
                        AlbumId = match.Album.Id,
                        ArtistId = match.Album.ArtistId,
                        ArtistName = match.Album.ArtistMetadata?.Value?.Name ?? match.Album.Title ?? $"Artist {match.Album.ArtistId}",
                        Title = string.IsNullOrWhiteSpace(match.Album.Title) ? $"Album {match.Album.Id}" : match.Album.Title,
                        MatchedStyles = match.MatchedStyles.ToArray(),
                        MatchScore = match.Score,
                        Added = match.Album.Added,
                        Year = match.Album.ReleaseDate?.Year
                    };
                    result.Add(sample);
                    used.Add(match.Album.Id);
                    if (result.Count >= targetCount) break;
                }
            }

            var topPct = mode == DiscoveryMode.Similar ? 55 : mode == DiscoveryMode.Adjacent ? 45 : 35;
            var recentPct = mode == DiscoveryMode.Similar ? 30 : mode == DiscoveryMode.Adjacent ? 35 : 40;
            var randomPct = Math.Max(0, 100 - topPct - recentPct);

            var topCount = Math.Max(1, targetCount * topPct / 100);
            AddRange(matches
                .OrderByDescending(m => m.Score)
                .ThenByDescending(m => m.Album.Ratings?.Value ?? 0)
                .ThenByDescending(m => m.Album.Ratings?.Votes ?? 0)
                .ThenBy(m => m.Album.Title, StringComparer.OrdinalIgnoreCase)
                .Take(topCount));

            if (result.Count < targetCount)
            {
                var recentCount = Math.Max(1, targetCount * recentPct / 100);
                AddRange(matches
                    .Where(m => !used.Contains(m.Album.Id))
                    .OrderByDescending(m => NormalizeAddedDate(m.Album.Added))
                    .Take(recentCount));
            }

            if (result.Count < targetCount && randomPct > 0)
            {
                var remaining = matches
                    .Where(m => !used.Contains(m.Album.Id))
                    .ToList();
                ShuffleInPlace(remaining, rng);
                AddRange(remaining.Take(Math.Max(0, targetCount - result.Count)));
            }

            return result;
        }
        private string ComputeSampleFingerprint(LibrarySample sample)
        {
            var sb = new StringBuilder();
            foreach (var artist in sample.Artists.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(artist.Name).Append('|');
                foreach (var album in artist.Albums.OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase))
                {
                    sb.Append(album.Title).Append(';');
                }
                sb.Append('#');
            }

            foreach (var album in sample.Albums
                .OrderBy(a => a.ArtistName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(album.ArtistName).Append('-').Append(album.Title).Append('|');
            }

            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var hash = sha.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", string.Empty, StringComparison.Ordinal);
        }
        private sealed record ArtistMatch(Artist Artist, HashSet<string> MatchedStyles, double Score);
        private sealed record AlbumMatch(Album Album, HashSet<string> MatchedStyles, double Score);

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

            var hashResult = ComputeStableHash(components);
            _logger.Trace(
                "Computed sampling seed from {ComponentCount} components (hash prefix {HashPrefix}) => {Seed}",
                hashResult.ComponentCount,
                hashResult.HashPrefix,
                hashResult.Seed);

            return hashResult.Seed;
        }

        internal static StableHashResult ComputeStableHash(IEnumerable<string> components)
        {
            var normalized = components
                .Select(component => component ?? string.Empty)
                .ToArray();

            var joined = string.Join('\u001F', normalized);
            var bytes = Encoding.UTF8.GetBytes(joined);
            var hash = SHA256.HashData(bytes);
            var seed32 = BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(0, sizeof(uint)));
            var seed = (int)(seed32 & 0x7FFF_FFFF);
            var hashPrefix = Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();

            return new StableHashResult(seed, hashPrefix, normalized.Length);
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

        internal readonly struct StableHashResult
        {
            public StableHashResult(int seed, string hashPrefix, int componentCount)
            {
                Seed = seed;
                HashPrefix = hashPrefix;
                ComponentCount = componentCount;
            }

            public int Seed { get; }
            public string HashPrefix { get; }
            public int ComponentCount { get; }
        }
        private sealed class StyleSelectionContext
        {
            public StyleSelectionContext(
                ISet<string> selected,
                ISet<string> expanded,
                List<StyleEntry> entries,
                List<StyleEntry> adjacent,
                Dictionary<string, int> coverage,
                bool relaxed,
                double threshold,
                List<string> trimmed,
                List<string> inferred)
            {
                if (selected is null) throw new ArgumentNullException(nameof(selected));
                if (expanded is null) throw new ArgumentNullException(nameof(expanded));

                SelectedSlugs = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);

                ExpandedSlugs = new HashSet<string>(expanded, StringComparer.OrdinalIgnoreCase);
                foreach (var slug in SelectedSlugs)
                {
                    ExpandedSlugs.Add(slug);
                }

                Entries = entries ?? new List<StyleEntry>();
                AdjacentEntries = adjacent ?? new List<StyleEntry>();
                Coverage = coverage ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                Relaxed = relaxed;
                Threshold = threshold;
                TrimmedSlugs = trimmed ?? new List<string>();
                InferredSlugs = inferred ?? new List<string>();
            }

            public ISet<string> SelectedSlugs { get; }
            public ISet<string> ExpandedSlugs { get; }
            public List<StyleEntry> Entries { get; }
            public List<StyleEntry> AdjacentEntries { get; }
            public Dictionary<string, int> Coverage { get; }
            public bool Relaxed { get; }
            public double Threshold { get; }
            public List<string> TrimmedSlugs { get; }
            public List<string> InferredSlugs { get; }
            public Dictionary<string, int> MatchedCounts { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public bool Sparse { get; set; }
            public bool HasStyles => SelectedSlugs.Count > 0;
            public bool ShouldUseRelaxedMatches => Relaxed && ExpandedSlugs.Any(slug => !SelectedSlugs.Contains(slug));

            public void RegisterMatch(IEnumerable<string> slugs)
            {
                if (slugs == null)
                {
                    return;
                }

                foreach (var slug in slugs)
                {
                    if (string.IsNullOrWhiteSpace(slug))
                    {
                        continue;
                    }

                    MatchedCounts.TryGetValue(slug, out var count);
                    MatchedCounts[slug] = count + 1;
                }
            }
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
        private sealed class PromptPlan
        {
            private int _maxArtists;
            private int _maxAlbumGroups;
            private int _maxAlbumsPerGroup = 5;
            private bool _compressed;
            private bool _trimmed;

            public PromptPlan(
                LibraryProfile profile,
                LibrarySample sample,
                BrainarrSettings settings,
                StyleSelectionContext styles,
                bool shouldRecommendArtists,
                LibraryPromptResult metrics)
            {
                Profile = profile;
                Sample = sample;
                Settings = settings;
                Styles = styles;
                ShouldRecommendArtists = shouldRecommendArtists;
                Metrics = metrics;

                _maxArtists = Math.Max(1, sample.ArtistCount);
                _maxAlbumGroups = Math.Max(1, sample.ArtistCount);
            }

            public LibraryProfile Profile { get; }
            public LibrarySample Sample { get; }
            public BrainarrSettings Settings { get; }
            public StyleSelectionContext Styles { get; }
            public bool ShouldRecommendArtists { get; }
            public LibraryPromptResult Metrics { get; }

            public int MaxArtists => _maxArtists;
            public int MaxAlbumGroups => _maxAlbumGroups;
            public int MaxAlbumsPerGroup => _maxAlbumsPerGroup;
            public bool Compressed => _compressed;
            public bool Trimmed => _trimmed;

            public bool TryCompress()
            {
                if (_maxAlbumsPerGroup > 3)
                {
                    _maxAlbumsPerGroup--;
                    _compressed = true;
                    return true;
                }

                if (_maxAlbumGroups > Math.Min(12, Sample.Artists.Count))
                {
                    _maxAlbumGroups = Math.Max(Math.Min(12, Sample.Artists.Count), _maxAlbumGroups - 3);
                    _compressed = true;
                    _trimmed = true;
                    return true;
                }

                if (_maxArtists > Math.Min(15, Sample.Artists.Count))
                {
                    _maxArtists = Math.Max(Math.Min(15, Sample.Artists.Count), _maxArtists - 3);
                    _compressed = true;
                    _trimmed = true;
                    return true;
                }

                return false;
            }

            public void MarkTrimmed()
            {
                _trimmed = true;
                _compressed = true;
            }
        }

        private sealed class PromptRenderer
        {
            private readonly LibraryAwarePromptBuilder _parent;

            public PromptRenderer(LibraryAwarePromptBuilder parent)
            {
                _parent = parent;
            }

            public string Render(PromptPlan plan)
            {
                var builder = new StringBuilder();
                var settings = plan.Settings;
                var profile = plan.Profile;
                var styles = plan.Styles;
                var sample = plan.Sample;

                var strategyPreamble = _parent.GetSamplingStrategyPreamble(settings.SamplingStrategy);
                if (!string.IsNullOrEmpty(strategyPreamble))
                {
                    builder.AppendLine(strategyPreamble);
                    builder.AppendLine();
                    builder.AppendLine("Note: Items below are representative samples of a much larger library; avoid recommending duplicates even if not explicitly listed.");
                    builder.AppendLine();
                }

                builder.AppendLine(_parent.GetDiscoveryModeTemplate(settings.DiscoveryMode, settings.MaxRecommendations, plan.ShouldRecommendArtists, styles.HasStyles));
                builder.AppendLine();

                if (styles.HasStyles)
                {
                    builder.AppendLine("🎨 STYLE FILTERS (library-aligned):");
                    foreach (var entry in styles.Entries)
                    {
                        var aliasText = entry.Aliases != null && entry.Aliases.Any()
                            ? $" (aliases: {string.Join(", ", entry.Aliases.Take(5))})"
                            : string.Empty;
                        var coverage = styles.Coverage.TryGetValue(entry.Slug, out var count) ? $" • coverage: {count}" : string.Empty;
                        builder.AppendLine($"• {entry.Name}{aliasText}{coverage}");
                    }
                    if (styles.AdjacentEntries.Any())
                    {
                        builder.AppendLine($"Adjacent context (only if needed): {string.Join(", ", styles.AdjacentEntries.Select(a => a.Name))}");
                    }
                    if (styles.Sparse)
                    {
                        builder.AppendLine("Sparse library coverage detected for these styles. Stay inside the cluster and prefer concrete connections (collaborators, side projects, shared labels).");
                    }
                    builder.AppendLine("Rule: Recommendations must live inside these styles and be grounded in the user's existing collection footprint.");
                    builder.AppendLine();
                }

                builder.AppendLine("📊 COLLECTION OVERVIEW:");
                builder.AppendLine(_parent.BuildEnhancedCollectionContext(profile));
                builder.AppendLine();

                builder.AppendLine("🎵 MUSICAL DNA:");
                builder.AppendLine(_parent.BuildMusicalDnaContext(profile));
                builder.AppendLine();

                var patterns = _parent.BuildCollectionPatterns(profile);
                if (!string.IsNullOrEmpty(patterns))
                {
                    builder.AppendLine("📈 COLLECTION PATTERNS:");
                    builder.AppendLine(patterns);
                    builder.AppendLine();
                }

                var artistLines = BuildArtistGroups(plan);
                builder.AppendLine($"🎶 LIBRARY ARTISTS & KEY ALBUMS ({artistLines.Count} groups shown):");
                foreach (var line in artistLines)
                {
                    builder.AppendLine(line);
                }
                builder.AppendLine();

                builder.AppendLine("🎯 RECOMMENDATION REQUIREMENTS:");
                if (plan.ShouldRecommendArtists)
                {
                    builder.AppendLine("1. DO NOT recommend any artists already listed above (they represent a much larger library).");
                    builder.AppendLine($"2. Return EXACTLY {settings.MaxRecommendations} NEW ARTIST recommendations as JSON.");
                    builder.AppendLine("3. Each entry must include: artist, genre, confidence (0.0-1.0), adjacency_source, reason.");
                    builder.AppendLine("4. Focus on artists – Lidarr will import their releases.");
                    builder.AppendLine("5. Highlight the concrete connection to the user's library (collaborations, side projects, shared producers, labelmates).");
                }
                else
                {
                    builder.AppendLine("1. DO NOT recommend any albums already listed above (treat the list as representative).");
                    builder.AppendLine($"2. Return EXACTLY {settings.MaxRecommendations} NEW ALBUM recommendations as JSON.");
                    builder.AppendLine("3. Each entry must include: artist, album, genre, year, confidence (0.0-1.0), adjacency_source, reason.");
                    builder.AppendLine("4. Prefer studio albums over live or compilation releases.");
                }
                builder.AppendLine("6. Keep every recommendation inside the style cluster defined above.");
                builder.AppendLine($"7. Match the collection's {_parent.GetCollectionCharacter(profile)} character.");
                builder.AppendLine($"8. Align with {_parent.GetTemporalPreference(profile)} temporal preferences.");
                builder.AppendLine($"9. Consider {_parent.GetDiscoveryTrend(profile)} discovery pattern.");
                builder.AppendLine();

                builder.AppendLine("JSON Response Format:");
                builder.AppendLine("[");
                builder.AppendLine("  {");
                builder.AppendLine("    \"artist\": \"Artist Name\",");
                if (!plan.ShouldRecommendArtists)
                {
                    builder.AppendLine("    \"album\": \"Album Title\",");
                    builder.AppendLine("    \"year\": 2024,");
                }
                builder.AppendLine("    \"genre\": \"Primary Genre\",");
                builder.AppendLine("    \"confidence\": 0.85,");
                builder.AppendLine("    \"adjacency_source\": \"Shared producer with <existing artist>\",");
                builder.AppendLine("    \"reason\": \"Explain the concrete connection to the user's library\"");
                builder.AppendLine("  }");
                builder.AppendLine("]");

                return builder.ToString();
            }

            private List<string> BuildArtistGroups(PromptPlan plan)
            {
                var lines = new List<string>();
                var ordered = plan.Sample.Artists
                    .OrderByDescending(a => a.Weight)
                    .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(plan.MaxArtists)
                    .ToList();

                foreach (var artist in ordered)
                {
                    var albums = artist.Albums
                        .OrderByDescending(a => a.Added ?? DateTime.MinValue)
                        .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var line = new StringBuilder();
                    var styleText = artist.MatchedStyles.Length > 0 ? $" [{string.Join("/", artist.MatchedStyles)}]" : string.Empty;
                    line.Append("• ").Append(artist.Name).Append(styleText);
                    line.Append(BuildAlbumText(albums, plan));
                    lines.Add(line.ToString());
                }

                if (lines.Count > plan.MaxAlbumGroups)
                {
                    lines = lines.Take(plan.MaxAlbumGroups).ToList();
                }

                return lines;
            }

            private string BuildAlbumText(List<LibrarySampleAlbum> albums, PromptPlan plan)
            {
                if (albums.Count == 0)
                {
                    return string.Empty;
                }

                var slice = albums
                    .Take(plan.MaxAlbumsPerGroup)
                    .Select(a => a.Year.HasValue ? $"{a.Title} ({a.Year.Value})" : a.Title);
                var more = albums.Count - plan.MaxAlbumsPerGroup;
                var suffix = more > 0 ? $"; +{more} more" : string.Empty;
                return $" — [{string.Join("; ", slice)}{suffix}]";
            }
        }
    }
}
