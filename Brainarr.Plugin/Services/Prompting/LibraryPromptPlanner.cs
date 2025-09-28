using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Utils;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public class LibraryPromptPlanner : IPromptPlanner
{
    private readonly Logger _logger;
    private readonly IStyleCatalogService _styleCatalog;
    private readonly IPlanCache? _planCache;
    private readonly TimeSpan _planCacheTtl;

    private const int SparseStyleArtistThreshold = 5;
    private const double RelaxedMatchThreshold = 0.70;
    private const double MaxRelaxedInflation = 3.0;
    public LibraryPromptPlanner(
        Logger logger,
        IStyleCatalogService styleCatalog,
        IPlanCache? planCache = null,
        TimeSpan? planCacheTtl = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _styleCatalog = styleCatalog ?? throw new ArgumentNullException(nameof(styleCatalog));
        _planCache = planCache;
        _planCacheTtl = planCacheTtl ?? TimeSpan.FromMinutes(5);
    }

    public PromptPlan Plan(LibraryProfile profile, RecommendationRequest request, CancellationToken cancellationToken)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var selection = BuildStyleSelection(profile, request.Settings, request.StyleContext, cancellationToken);
        var signature = ComputeSamplingSignature(profile, request.Artists, request.Albums, selection, request.Settings);
        var seed = signature.Seed;
        var libraryFingerprint = signature.LibraryFingerprint;
        var planKey = BuildPlanCacheKey(request, selection, libraryFingerprint, seed);

        if (_planCache != null && _planCache.TryGet(planKey, out var cachedPlan))
        {
            return cachedPlan with
            {
                Compression = cachedPlan.Compression.Clone(),
                FromCache = true,
                PlanCacheKey = planKey
            };
        }

        var sample = BuildLibrarySample(
            request.Artists,
            request.Albums,
            request.StyleContext,
            selection,
            request.Settings,
            request.AvailableSamplingTokens,
            seed,
            cancellationToken);
        var fingerprint = ComputeSampleFingerprint(sample);
        var compression = new PromptCompressionState(sample.ArtistCount, sample.ArtistCount, 5);
        var stylesUsed = selection.Entries.Select(e => e.Slug).ToArray();

        var plan = new PromptPlan(sample, stylesUsed)
        {
            TargetTokens = request.TargetTokens,
            Profile = profile,
            Settings = request.Settings,
            StyleContext = selection,
            ShouldRecommendArtists = request.RecommendArtists,
            Compression = compression,
            SampleFingerprint = fingerprint,
            SampleSeed = seed.ToString(CultureInfo.InvariantCulture),
            RelaxedStyleMatching = selection.Relaxed,
            StyleCoverageSparse = selection.Sparse,
            TrimmedStyles = selection.TrimmedSlugs.ToArray(),
            InferredStyleSlugs = selection.InferredSlugs.ToArray(),
            StyleCoverage = new Dictionary<string, int>(selection.Coverage, StringComparer.OrdinalIgnoreCase),
            MatchedStyleCounts = new Dictionary<string, int>(selection.MatchedCounts, StringComparer.OrdinalIgnoreCase),
            LibraryFingerprint = libraryFingerprint,
            PlanCacheKey = planKey,
            FromCache = false
        };

        if (_planCache != null)
        {
            var cacheEntry = plan with
            {
                Compression = plan.Compression.Clone(),
                FromCache = false,
                PlanCacheKey = planKey
            };
            _planCache.Set(planKey, cacheEntry, _planCacheTtl);
        }

        return plan;
    }

    private StylePlanContext BuildStyleSelection(
        LibraryProfile profile,
        BrainarrSettings settings,
        LibraryStyleContext styleContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var coverage = styleContext?.StyleCoverage ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
            var dominant = styleContext?.DominantStyles ?? new List<string>();
            foreach (var slug in dominant)
            {
                if (inferred.Count >= settings.MaxSelectedStyles)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                inferred.Add(slug);
            }

            if (inferred.Count > 0)
            {
                normalized = inferred.ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }

        if (normalized.Count == 0 && settings.DiscoveryMode != DiscoveryMode.Similar)
        {
            var fallback = styleContext?.DominantStyles ?? new List<string>();
            if (fallback.Count > 0)
            {
                normalized = fallback.Take(settings.MaxSelectedStyles).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }

        if (normalized.Count == 0)
        {
            _logger.Debug("No style filters selected; falling back to discovery mode defaults");
        }

        var entries = normalized
            .Select(slug => _styleCatalog.GetBySlug(slug) ?? new StyleEntry { Name = slug, Slug = slug })
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Slug, StringComparer.OrdinalIgnoreCase)
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
                    if (similar.Score < threshold)
                    {
                        continue;
                    }

                    if (normalized.Contains(similar.Slug))
                    {
                        continue;
                    }

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

        adjacent = adjacent
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Slug, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selection = new StylePlanContext(
            normalized,
            expanded,
            entries,
            adjacent,
            coverage,
            relaxed,
            threshold,
            trimmed,
            inferred);

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

    private SamplingSignature ComputeSamplingSignature(
        LibraryProfile profile,
        IReadOnlyList<Artist> artists,
        IReadOnlyList<Album> albums,
        StylePlanContext selection,
        BrainarrSettings settings)
    {
        var components = new List<string>
        {
            (profile?.TotalArtists ?? 0).ToString(CultureInfo.InvariantCulture),
            (profile?.TotalAlbums ?? 0).ToString(CultureInfo.InvariantCulture),
            ((int)settings.DiscoveryMode).ToString(CultureInfo.InvariantCulture),
            ((int)settings.SamplingStrategy).ToString(CultureInfo.InvariantCulture),
            settings.RelaxStyleMatching ? "relaxed" : "strict",
            settings.MaxRecommendations.ToString(CultureInfo.InvariantCulture)
        };

        if (selection.SelectedSlugs != null)
        {
            foreach (var slug in selection.SelectedSlugs.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                components.Add($"style:{slug}");
            }
        }

        var artistIds = artists.Select(a => a.Id).OrderBy(id => id).Take(24).ToArray();
        foreach (var id in artistIds)
        {
            components.Add($"artist:{id.ToString(CultureInfo.InvariantCulture)}");
        }

        var albumIds = albums.Select(a => a.Id).OrderBy(id => id).Take(24).ToArray();
        foreach (var id in albumIds)
        {
            components.Add($"album:{id.ToString(CultureInfo.InvariantCulture)}");
        }

        var stable = ComputeStableHash(components);
        var seed = stable.Seed;

        _logger.Trace(
            "Computed sampling signature (seed={Seed}, hashPrefix={Prefix}, components={ComponentCount})",
            seed,
            stable.HashPrefix,
            stable.ComponentCount);

        return new SamplingSignature(seed, stable.FullHash, stable.HashPrefix);
    }

    private static string BuildPlanCacheKey(
        RecommendationRequest request,
        StylePlanContext selection,
        string libraryFingerprint,
        int seed)
    {
        var orderedStyles = selection.SelectedSlugs
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var styleKey = orderedStyles.Length > 0 ? string.Join("|", orderedStyles) : "_";
        var relaxed = selection.Relaxed ? "relaxed" : "strict";
        var recommend = request.RecommendArtists ? "artists" : "albums";
        var sparse = selection.Sparse ? "sparse" : "dense";

        var components = new[]
        {
            libraryFingerprint,
            request.ModelKey ?? string.Empty,
            request.ContextWindow.ToString(CultureInfo.InvariantCulture),
            request.TargetTokens.ToString(CultureInfo.InvariantCulture),
            request.Settings.DiscoveryMode.ToString(),
            request.Settings.SamplingStrategy.ToString(),
            request.Settings.MaxRecommendations.ToString(CultureInfo.InvariantCulture),
            request.Settings.RelaxStyleMatching ? "relaxed-matching" : "strict-matching",
            recommend,
            relaxed,
            sparse,
            seed.ToString(CultureInfo.InvariantCulture),
            styleKey
        };

        return string.Join('#', components);
    }

    private LibrarySample BuildLibrarySample(
        IReadOnlyList<Artist> allArtists,
        IReadOnlyList<Album> allAlbums,
        LibraryStyleContext styleContext,
        StylePlanContext selection,
        BrainarrSettings settings,
        int tokenBudget,
        int seed,
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

    private List<ArtistMatch> BuildArtistMatchList(IReadOnlyList<Artist> artists, LibraryStyleContext context, StylePlanContext selection)
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

                candidateArtists = filtered;
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

    private List<AlbumMatch> BuildAlbumMatchList(IReadOnlyList<Album> albums, LibraryStyleContext context, StylePlanContext selection)
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

                candidateAlbums = filtered;
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

    private bool TryMatchStyles(HashSet<string> itemSlugs, StylePlanContext selection, out HashSet<string> matched, out double score)
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
        IReadOnlyList<Album> allAlbums,
        StylePlanContext selection,
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
                if (used.Contains(match.Artist.Id))
                {
                    continue;
                }

                var sampleArtist = CreateSampleArtist(match, albumCounts);
                result.Add(sampleArtist);
                used.Add(match.Artist.Id);
                if (result.Count >= targetCount)
                {
                    break;
                }
            }
        }

        var topPct = mode == DiscoveryMode.Similar ? 60 : mode == DiscoveryMode.Adjacent ? 45 : 35;
        var recentPct = mode == DiscoveryMode.Similar ? 30 : mode == DiscoveryMode.Adjacent ? 35 : 35;
        var randomPct = Math.Max(0, 100 - topPct - recentPct);

        var topCount = Math.Max(1, targetCount * topPct / 100);
        AddRange(matches
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => albumCounts.TryGetValue(m.Artist.Id, out var count) ? count : 0)
            .ThenByDescending(m => DateUtil.NormalizeMin(m.Artist.Added))
            .ThenBy(m => m.Artist.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Artist.Id)
            .Take(topCount));

        if (result.Count < targetCount)
        {
            var recentCount = Math.Max(1, targetCount * recentPct / 100);
            AddRange(matches
                .Where(m => !used.Contains(m.Artist.Id))
                .OrderByDescending(m => DateUtil.NormalizeMin(m.Artist.Added))
                .Take(recentCount));
        }

        if (result.Count < targetCount && randomPct > 0)
        {
            var remaining = matches
                .Where(m => !used.Contains(m.Artist.Id))
                .ToList();
            CollectionsUtil.ShuffleInPlace(remaining, rng);
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
        StylePlanContext selection,
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
                if (used.Contains(match.Album.Id))
                {
                    continue;
                }

                var artistName = match.Album.ArtistMetadata?.Value?.Name;
                if (string.IsNullOrWhiteSpace(artistName))
                {
                    artistName = $"Artist {match.Album.ArtistId}";
                }

                var sample = new LibrarySampleAlbum
                {
                    AlbumId = match.Album.Id,
                    ArtistId = match.Album.ArtistId,
                    ArtistName = artistName,
                    Title = string.IsNullOrWhiteSpace(match.Album.Title) ? $"Album {match.Album.Id}" : match.Album.Title,
                    MatchedStyles = match.MatchedStyles.ToArray(),
                    MatchScore = match.Score,
                    Added = match.Album.Added,
                    Year = match.Album.ReleaseDate?.Year
                };
                result.Add(sample);
                used.Add(match.Album.Id);
                if (result.Count >= targetCount)
                {
                    break;
                }
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
            .ThenByDescending(m => DateUtil.NormalizeMin(m.Album.Added))
            .ThenByDescending(m => m.Album.ReleaseDate ?? DateTime.MinValue)
            .ThenBy(m => m.Album.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Album.Id)
            .Take(topCount));

        if (result.Count < targetCount)
        {
            var recentCount = Math.Max(1, targetCount * recentPct / 100);
            AddRange(matches
                .Where(m => !used.Contains(m.Album.Id))
                .OrderByDescending(m => DateUtil.NormalizeMin(m.Album.Added))
                .Take(recentCount));
        }

        if (result.Count < targetCount && randomPct > 0)
        {
            var remaining = matches
                .Where(m => !used.Contains(m.Album.Id))
                .ToList();
            var slots = Math.Max(0, targetCount - result.Count);
            if (slots > 0 && remaining.Count > 0)
            {
                CollectionsUtil.ShuffleInPlace(remaining, rng);
                var selected = remaining
                    .Take(slots)
                    .OrderByDescending(m => m.Score)
                    .ThenByDescending(m => m.Album.Ratings?.Value ?? 0)
                    .ThenByDescending(m => m.Album.Ratings?.Votes ?? 0)
                    .ThenByDescending(m => DateUtil.NormalizeMin(m.Album.Added))
                    .ThenByDescending(m => m.Album.ReleaseDate ?? DateTime.MinValue)
                    .ThenBy(m => m.Album.Title, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(m => m.Album.Id);

                AddRange(selected);
            }
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
        var fullHash = Convert.ToHexString(hash).ToLowerInvariant();

        return new StableHashResult(seed, hashPrefix, normalized.Length, fullHash);
    }

    private sealed record ArtistMatch(Artist Artist, HashSet<string> MatchedStyles, double Score);

    private sealed record AlbumMatch(Album Album, HashSet<string> MatchedStyles, double Score);

    private readonly struct SamplingSignature
    {
        public SamplingSignature(int seed, string libraryFingerprint, string hashPrefix)
        {
            Seed = seed;
            LibraryFingerprint = libraryFingerprint;
            HashPrefix = hashPrefix;
        }

        public int Seed { get; }

        public string LibraryFingerprint { get; }

        public string HashPrefix { get; }
    }

    internal readonly struct StableHashResult
    {
        public StableHashResult(int seed, string hashPrefix, int componentCount, string fullHash)
        {
            Seed = seed;
            HashPrefix = hashPrefix;
            ComponentCount = componentCount;
            FullHash = fullHash;
        }

        public int Seed { get; }

        public string HashPrefix { get; }

        public int ComponentCount { get; }

        public string FullHash { get; }
    }
}
