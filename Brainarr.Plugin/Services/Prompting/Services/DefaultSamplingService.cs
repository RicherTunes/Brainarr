using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;

public sealed class DefaultSamplingService : ISamplingService
{
    private readonly Logger _logger;
    private readonly IStyleCatalogService _styleCatalog;
    private readonly IContextPolicy _contextPolicy;
    private const int AbsoluteRelaxedCap = 1200;

    // internal for log-message accuracy unit tests (InternalsVisibleTo "Brainarr.Tests").
    // Pre-formatted (no structured-template hole) on purpose: passing the joined slug string as a
    // single template capture renders inside ONE quote pair under the host's NLog 5.x
    // (selected=["lofi-hip-hop, alternative-rock"]), which misreads as a single slug.
    internal static string FormatStrictOnlyLogMessage(string scope, int strictCount, IEnumerable<string> selectedSlugs)
        => $"{scope} style matches remain strict-only: count={strictCount}, selected=[{string.Join(", ", selectedSlugs)}]";

    public DefaultSamplingService(Logger logger, IStyleCatalogService styleCatalog, IContextPolicy contextPolicy)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _styleCatalog = styleCatalog ?? throw new ArgumentNullException(nameof(styleCatalog));
        _contextPolicy = contextPolicy ?? throw new ArgumentNullException(nameof(contextPolicy));
    }

    public LibrarySample Sample(
        IReadOnlyList<Artist> allArtists,
        IReadOnlyList<Album> allAlbums,
        LibraryStyleContext styleContext,
        StylePlanContext selection,
        BrainarrSettings settings,
        int tokenBudget,
        int seed,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        styleContext ??= new LibraryStyleContext();
        settings ??= new BrainarrSettings();

        var samplingShape = settings.EffectiveSamplingShape;

        var artistMatches = BuildArtistMatchList(allArtists, styleContext, selection, samplingShape);
        var albumMatches = BuildAlbumMatchList(allAlbums, styleContext, selection, samplingShape);

        if (selection.HasStyles && artistMatches.Count < 5)
        {
            selection.Sparse = true;
        }

        if (selection.HasStyles && albumMatches.Count < 5)
        {
            selection.Sparse = true;
        }

        var targetArtistCount = _contextPolicy.DetermineTargetArtistCount(allArtists.Count, tokenBudget);
        var targetAlbumCount = _contextPolicy.DetermineTargetAlbumCount(allAlbums.Count, tokenBudget);

        // ArtistMetadataId -> Artist map built once from the already-materialized artists list.
        // Used to resolve each sampled album's artist id/name WITHOUT dereferencing
        // Album.ArtistId / Album.Artist.Value / Album.ArtistMetadata.Value -- all of which are
        // LazyLoaded on the albums GetAllAlbums() returns and fire a per-row DB round trip.
        var artistByMetadataId = BuildArtistByMetadataIdMap(allArtists);

        var sample = new LibrarySample();
        var rng = new Random(seed);

        sample.Artists.AddRange(SampleArtists(artistMatches, allAlbums, selection, settings.DiscoveryMode, samplingShape, targetArtistCount, rng));
        sample.Albums.AddRange(SampleAlbums(albumMatches, selection, settings.DiscoveryMode, samplingShape, targetAlbumCount, rng, artistByMetadataId));

        // Ensure any albums bring along their artist to preserve prompt grouping
        var artistIndex = sample.Artists.ToDictionary(a => a.ArtistId);
        foreach (var album in sample.Albums)
        {
            if (artistIndex.ContainsKey(album.ArtistId))
            {
                continue;
            }

            var synthetic = new LibrarySampleArtist
            {
                ArtistId = album.ArtistId,
                Name = string.IsNullOrWhiteSpace(album.ArtistName) ? $"Artist {album.ArtistId}" : album.ArtistName,
                MatchedStyles = Array.Empty<string>(),
                MatchScore = album.MatchScore,
                Added = album.Added,
                Weight = 0.25
            };
            synthetic.Albums.Add(album);
            sample.Artists.Add(synthetic);
            artistIndex[synthetic.ArtistId] = synthetic;
        }

        return sample;
    }

    private List<ArtistMatch> BuildArtistMatchList(
        IReadOnlyList<Artist> artists,
        LibraryStyleContext context,
        StylePlanContext selection,
        SamplingShape samplingShape)
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
                var rawExpanded = context.StyleIndex.GetArtistsForStyles(selection.ExpandedSlugs);
                var strictCount = Math.Max(1, strictIds.Count);
                var ratioLimit = (int)Math.Ceiling(strictCount * samplingShape.MaxRelaxedInflation);
                var effectiveLimit = Math.Max(strictCount, Math.Min(ratioLimit, AbsoluteRelaxedCap));
                var workingExpanded = rawExpanded;

                if (rawExpanded.Count > effectiveLimit)
                {
                    workingExpanded = rawExpanded.Take(effectiveLimit).ToList();
                    relaxedTrimmed = true;
                }

                if (workingExpanded.Count > strictIds.Count)
                {
                    candidateIds = workingExpanded;
                    relaxedApplied = true;
                }

                expandedIds = workingExpanded;
            }

            if (candidateIds.Count == 0 && strictIds.Count == 0 && expandedIds.Count > 0)
            {
                candidateIds = expandedIds;
            }

            if (candidateIds.Count > 0)
            {
                var lookup = artists.ToDictionary(a => a.Id);
                var orderedIds = candidateIds.Distinct().OrderBy(id => id).ToList();
                var filtered = new List<Artist>(orderedIds.Count);
                foreach (var id in orderedIds)
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
                    "Relaxed artist style matches trimmed: strict={StrictCount}, candidate={CandidateCount}, limitFactor={Limit}, absoluteCap={AbsoluteCap}, slugs=[{Expanded}]",
                    strictIds.Count,
                    expandedIds.Count,
                    samplingShape.MaxRelaxedInflation,
                    AbsoluteRelaxedCap,
                    string.Join(", ", selection.ExpandedSlugs));
            }
            else if (!selection.ShouldUseRelaxedMatches)
            {
                _logger.Debug(FormatStrictOnlyLogMessage("Artist", strictIds.Count, selection.SelectedSlugs));
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

        // Style-seeded ("genre-first") dedup fallback: the user selected styles their library has
        // ZERO coverage of, so the strict+relaxed match lists came back empty and the prompt would
        // print "0 groups" — the model gets no "avoid these duplicates" signal. Surface the closest
        // library artists (adjacent styles, then the library's dominant artists, then any artist) as a
        // pure dedup audit. These carry NO matched styles and score 0 so they are NEVER registered as
        // seed-style matches: the genre-first decision gate (sum of selected-style coverage == 0,
        // computed identically by the renderer and RecommendationPipeline) is preserved.
        //
        // GATE on the genre-first condition (zero selected-style coverage) — NOT merely on an empty
        // match list. A library-aligned selection whose styles DO have coverage can still yield zero
        // matches (e.g. a below-threshold relaxed match, or artists missing style tags); that is a
        // sparse library-aligned case, not genre-first, and must keep its empty list (the prompt stays
        // grounded in collaborators). Only the true zero-coverage case gets the dedup audit.
        if (IsStyleSeededZeroCoverage(selection) && matches.Count == 0 && artists.Count > 0)
        {
            var fallbackArtists = SelectDedupFallbackArtists(artists, context, selection);
            foreach (var artist in fallbackArtists)
            {
                matches.Add(new ArtistMatch(artist, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0.0));
            }

            if (matches.Count > 0)
            {
                _logger.Debug(
                    "Style-seeded dedup fallback engaged for artists: zero seed-style coverage, surfaced {Count} library artists as a dedup audit (selected=[{Selected}])",
                    matches.Count,
                    string.Join(", ", selection.SelectedSlugs));
            }
        }

        return matches;
    }

    private List<Artist> SelectDedupFallbackArtists(
        IReadOnlyList<Artist> artists,
        LibraryStyleContext context,
        StylePlanContext selection)
    {
        var lookup = artists.ToDictionary(a => a.Id);

        // 1) Adjacent styles: the library lacks the seed styles, but it may have a sibling/parent the
        //    catalog considers similar. Those artists are the most relevant dedup candidates.
        var adjacentSlugs = CollectAdjacentSlugs(selection);
        if (adjacentSlugs.Count > 0 && context.StyleIndex != null)
        {
            var ids = context.StyleIndex.GetArtistsForStyles(adjacentSlugs);
            var resolved = ResolveArtists(ids, lookup);
            if (resolved.Count > 0)
            {
                return resolved;
            }
        }

        // 2) The library's dominant styles (its actual character) — still useful as a dedup list even
        //    when unrelated to the seed styles.
        if (context.DominantStyles.Count > 0 && context.StyleIndex != null)
        {
            var ids = context.StyleIndex.GetArtistsForStyles(context.DominantStyles);
            var resolved = ResolveArtists(ids, lookup);
            if (resolved.Count > 0)
            {
                return resolved;
            }
        }

        // 3) Last resort: the whole library is the dedup pool (SampleArtists caps it to the target).
        return artists.ToList();
    }

    private List<string> CollectAdjacentSlugs(StylePlanContext selection)
    {
        var adjacent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var seed in selection.SelectedSlugs)
        {
            foreach (var similar in _styleCatalog.GetSimilarSlugs(seed))
            {
                if (string.IsNullOrWhiteSpace(similar.Slug) || selection.SelectedSlugs.Contains(similar.Slug))
                {
                    continue;
                }

                adjacent.Add(similar.Slug);
            }
        }

        return adjacent.ToList();
    }

    private static List<Artist> ResolveArtists(IReadOnlyList<int> ids, Dictionary<int, Artist> lookup)
    {
        var resolved = new List<Artist>(ids.Count);
        foreach (var id in ids)
        {
            if (lookup.TryGetValue(id, out var artist))
            {
                resolved.Add(artist);
            }
        }

        return resolved;
    }

    private List<AlbumMatch> BuildAlbumMatchList(
        IReadOnlyList<Album> albums,
        LibraryStyleContext context,
        StylePlanContext selection,
        SamplingShape samplingShape)
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
                var rawExpanded = context.StyleIndex.GetAlbumsForStyles(selection.ExpandedSlugs);
                var strictCount = Math.Max(1, strictIds.Count);
                var ratioLimit = (int)Math.Ceiling(strictCount * samplingShape.MaxRelaxedInflation);
                var effectiveLimit = Math.Max(strictCount, Math.Min(ratioLimit, AbsoluteRelaxedCap));
                var workingExpanded = rawExpanded;

                if (rawExpanded.Count > effectiveLimit)
                {
                    workingExpanded = rawExpanded.Take(effectiveLimit).ToList();
                    relaxedTrimmed = true;
                }

                if (workingExpanded.Count > strictIds.Count)
                {
                    candidateIds = workingExpanded;
                    relaxedApplied = true;
                }

                expandedIds = workingExpanded;
            }

            if (candidateIds.Count == 0 && strictIds.Count == 0 && expandedIds.Count > 0)
            {
                candidateIds = expandedIds;
            }

            if (candidateIds.Count > 0)
            {
                var lookup = albums.ToDictionary(a => a.Id);
                var orderedIds = candidateIds.Distinct().OrderBy(id => id).ToList();
                var filtered = new List<Album>(orderedIds.Count);
                foreach (var id in orderedIds)
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
                    "Relaxed album style matches trimmed: strict={StrictCount}, candidate={CandidateCount}, limitFactor={Limit}, absoluteCap={AbsoluteCap}, slugs=[{Expanded}]",
                    strictIds.Count,
                    expandedIds.Count,
                    samplingShape.MaxRelaxedInflation,
                    AbsoluteRelaxedCap,
                    string.Join(", ", selection.ExpandedSlugs));
            }
            else if (!selection.ShouldUseRelaxedMatches)
            {
                _logger.Debug(FormatStrictOnlyLogMessage("Album", strictIds.Count, selection.SelectedSlugs));
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

        // Style-seeded dedup fallback (album side) — mirrors the artist fallback above. See its comment
        // for the genre-first-gate-preservation rationale. Score 0, no matched styles → dedup audit only.
        if (IsStyleSeededZeroCoverage(selection) && matches.Count == 0 && albums.Count > 0)
        {
            var fallbackAlbums = SelectDedupFallbackAlbums(albums, context, selection);
            foreach (var album in fallbackAlbums)
            {
                matches.Add(new AlbumMatch(album, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0.0));
            }

            if (matches.Count > 0)
            {
                _logger.Debug(
                    "Style-seeded dedup fallback engaged for albums: zero seed-style coverage, surfaced {Count} library albums as a dedup audit (selected=[{Selected}])",
                    matches.Count,
                    string.Join(", ", selection.SelectedSlugs));
            }
        }

        return matches;
    }

    private List<Album> SelectDedupFallbackAlbums(
        IReadOnlyList<Album> albums,
        LibraryStyleContext context,
        StylePlanContext selection)
    {
        var lookup = albums.ToDictionary(a => a.Id);

        var adjacentSlugs = CollectAdjacentSlugs(selection);
        if (adjacentSlugs.Count > 0 && context.StyleIndex != null)
        {
            var ids = context.StyleIndex.GetAlbumsForStyles(adjacentSlugs);
            var resolved = ResolveAlbums(ids, lookup);
            if (resolved.Count > 0)
            {
                return resolved;
            }
        }

        if (context.DominantStyles.Count > 0 && context.StyleIndex != null)
        {
            var ids = context.StyleIndex.GetAlbumsForStyles(context.DominantStyles);
            var resolved = ResolveAlbums(ids, lookup);
            if (resolved.Count > 0)
            {
                return resolved;
            }
        }

        return albums.ToList();
    }

    private static List<Album> ResolveAlbums(IReadOnlyList<int> ids, Dictionary<int, Album> lookup)
    {
        var resolved = new List<Album>(ids.Count);
        foreach (var id in ids)
        {
            if (lookup.TryGetValue(id, out var album))
            {
                resolved.Add(album);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Genre-first ("style-seeded") detector — TRUE when the user selected styles whose summed library
    /// coverage is zero. This is the SAME signal <c>LibraryPromptRenderer</c> and
    /// <c>RecommendationPipeline.IsStyleSeededDiscovery</c> compute (sum of
    /// <c>StyleContext.Coverage[selectedSlug]</c> == 0); gating the dedup fallback on it keeps the
    /// sampler, the prompt, and the post-filter in lockstep and prevents the fallback from firing on a
    /// merely-sparse library-aligned selection.
    /// </summary>
    private static bool IsStyleSeededZeroCoverage(StylePlanContext selection)
    {
        if (selection == null || !selection.HasStyles)
        {
            return false;
        }

        var selectedCoverage = selection.SelectedSlugs.Sum(slug =>
            selection.Coverage.TryGetValue(slug, out var c) ? c : 0);
        return selectedCoverage == 0;
    }

    private bool TryMatchStyles(HashSet<string> itemSlugs, StylePlanContext selection, out HashSet<string> matched, out double score)
    {
        matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

    private List<LibrarySampleArtist> SampleArtists(
        List<ArtistMatch> matches,
        IReadOnlyList<Album> allAlbums,
        StylePlanContext selection,
        DiscoveryMode mode,
        SamplingShape samplingShape,
        int targetCount,
        Random rng)
    {
        var result = new List<LibrarySampleArtist>();
        if (matches.Count == 0 || targetCount <= 0)
        {
            return result;
        }

        // Count albums per artist by grouping on ArtistMetadataId (a plain int column), NOT
        // Album.ArtistId. Album.ArtistId dereferences a LazyLoaded<Artist> that GetAllAlbums()
        // leaves unloaded, so grouping ALL albums by it fires one per-row ArtistRepository.Query()
        // DB round trip per album -- the N+1 that OOMed an ~11,700-artist library live. Counts are
        // only ever looked up below by match.Artist.Id, so translate the metadata-id counts back to
        // Artist.Id via the artists already carried on the matches (Artist.Id/.ArtistMetadataId are
        // eager). Provably equivalent: album.ArtistId == the Id of the artist whose
        // ArtistMetadataId == album.ArtistMetadataId, so the per-artist counts are identical.
        var albumCountByMetadataId = allAlbums
            .GroupBy(a => a.ArtistMetadataId)
            .ToDictionary(g => g.Key, g => g.Count());

        var albumCounts = new Dictionary<int, int>();
        foreach (var match in matches)
        {
            var artist = match.Artist;
            if (artist == null || albumCounts.ContainsKey(artist.Id))
            {
                continue;
            }

            albumCounts[artist.Id] = albumCountByMetadataId.TryGetValue(artist.ArtistMetadataId, out var albumCount)
                ? albumCount
                : 0;
        }

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

        var distribution = samplingShape.GetArtistDistribution(mode);
        var topPct = distribution.TopPercent;
        var recentPct = distribution.RecentPercent;
        var randomPct = distribution.RandomPercent;

        var topCount = Math.Max(1, targetCount * topPct / 100);
        AddRange(matches
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => albumCounts.TryGetValue(m.Artist.Id, out var count) ? count : 0)
            .ThenByDescending(m => NormalizeAdded(m.Artist.Added))
            .ThenBy(m => m.Artist.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Artist.Id)
            .Take(topCount));

        if (result.Count < targetCount)
        {
            var recentCount = Math.Max(1, targetCount * recentPct / 100);
            AddRange(matches
                .OrderByDescending(m => NormalizeAdded(m.Artist.Added))
                .ThenByDescending(m => m.Score)
                .ThenBy(m => m.Artist.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Artist.Id)
                .Take(recentCount));
        }

        if (result.Count < targetCount && randomPct > 0)
        {
            var slots = Math.Max(0, targetCount - result.Count);
            var randomCount = Math.Max(1, targetCount * randomPct / 100);
            var pool = matches
                .OrderByDescending(m => m.Score)
                .ThenByDescending(m => NormalizeAdded(m.Artist.Added))
                .ThenBy(m => m.Artist.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Artist.Id)
                .Take(randomCount * 2)
                .ToList();

            while (pool.Count > 0 && result.Count < targetCount && slots > 0)
            {
                var index = rng.Next(pool.Count);
                var candidate = pool[index];
                pool.RemoveAt(index);
                if (used.Contains(candidate.Artist.Id))
                {
                    continue;
                }

                var sampleArtist = CreateSampleArtist(candidate, albumCounts);
                result.Add(sampleArtist);
                used.Add(candidate.Artist.Id);
                slots--;
            }
        }

        if (selection.HasStyles)
        {
            foreach (var artist in result)
            {
                selection.RegisterMatch(artist.MatchedStyles);
            }
        }

        return result;
    }

    private List<LibrarySampleAlbum> SampleAlbums(
        List<AlbumMatch> matches,
        StylePlanContext selection,
        DiscoveryMode mode,
        SamplingShape samplingShape,
        int targetCount,
        Random rng,
        IReadOnlyDictionary<int, Artist> artistByMetadataId)
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

                var sampleAlbum = CreateSampleAlbum(match, artistByMetadataId);
                result.Add(sampleAlbum);
                used.Add(match.Album.Id);
                if (result.Count >= targetCount)
                {
                    break;
                }
            }
        }

        var distribution = samplingShape.GetAlbumDistribution(mode);
        var topPct = distribution.TopPercent;
        var recentPct = distribution.RecentPercent;
        var randomPct = distribution.RandomPercent;

        var topCount = Math.Max(1, targetCount * topPct / 100);
        AddRange(matches
            .OrderByDescending(m => m.Score)
            .ThenByDescending(m => m.Album.Ratings?.Value ?? 0)
            .ThenByDescending(m => m.Album.Ratings?.Votes ?? 0)
            .ThenByDescending(m => NormalizeAdded(m.Album.Added))
            .ThenByDescending(m => m.Album.ReleaseDate ?? DateTime.MinValue)
            .ThenBy(m => m.Album.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Album.Id)
            .Take(topCount));

        if (result.Count < targetCount)
        {
            var recentCount = Math.Max(1, targetCount * recentPct / 100);
            AddRange(matches
                .OrderByDescending(m => NormalizeAdded(m.Album.Added))
                .ThenByDescending(m => m.Album.ReleaseDate ?? DateTime.MinValue)
                .ThenByDescending(m => m.Score)
                .ThenBy(m => m.Album.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Album.Id)
                .Take(recentCount));
        }

        if (result.Count < targetCount && randomPct > 0)
        {
            var slots = Math.Max(0, targetCount - result.Count);
            var randomCount = Math.Max(1, targetCount * randomPct / 100);
            var pool = matches
                .OrderByDescending(m => m.Score)
                .ThenByDescending(m => NormalizeAdded(m.Album.Added))
                .ThenBy(m => m.Album.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.Album.Id)
                .Take(randomCount * 2)
                .ToList();

            while (pool.Count > 0 && result.Count < targetCount && slots > 0)
            {
                var index = rng.Next(pool.Count);
                var candidate = pool[index];
                pool.RemoveAt(index);
                if (used.Contains(candidate.Album.Id))
                {
                    continue;
                }

                var sampleAlbum = CreateSampleAlbum(candidate, artistByMetadataId);
                result.Add(sampleAlbum);
                used.Add(candidate.Album.Id);
                slots--;
            }
        }

        if (selection.HasStyles)
        {
            foreach (var album in result)
            {
                selection.RegisterMatch(album.MatchedStyles);
            }
        }

        return result;
    }

    private static LibrarySampleArtist CreateSampleArtist(ArtistMatch match, Dictionary<int, int> albumCounts)
    {
        var count = albumCounts.TryGetValue(match.Artist.Id, out var albums) ? albums : 0;
        var weight = ComputeArtistWeight(match.Artist, count, match.Score);

        return new LibrarySampleArtist
        {
            ArtistId = match.Artist.Id,
            Name = string.IsNullOrWhiteSpace(match.Artist.Name) ? $"Artist {match.Artist.Id}" : match.Artist.Name,
            MatchedStyles = match.MatchedStyles.ToArray(),
            MatchScore = match.Score,
            Added = NormalizeAdded(match.Artist.Added),
            Weight = weight
        };
    }

    private static LibrarySampleAlbum CreateSampleAlbum(AlbumMatch match, IReadOnlyDictionary<int, Artist> artistByMetadataId)
    {
        // Resolve the album's artist via the ArtistMetadataId map instead of match.Album.ArtistId /
        // match.Album.Artist.Value -- both LazyLoaded on GetAllAlbums() results, so touching them
        // per sampled album fires a per-row DB round trip (the same N+1 lazy-load hazard). The
        // resolved artist's Id equals Album.ArtistId (both hang off ArtistMetadataId), so the
        // stored LibrarySampleAlbum is unchanged.
        var artist = artistByMetadataId != null && artistByMetadataId.TryGetValue(match.Album.ArtistMetadataId, out var resolved)
            ? resolved
            : null;

        return new LibrarySampleAlbum
        {
            AlbumId = match.Album.Id,
            ArtistId = artist?.Id ?? 0,
            ArtistName = ResolveArtistName(match.Album, artist),
            Title = string.IsNullOrWhiteSpace(match.Album.Title) ? $"Album {match.Album.Id}" : match.Album.Title,
            MatchedStyles = match.MatchedStyles.ToArray(),
            MatchScore = match.Score,
            Added = NormalizeAdded(match.Album.Added),
            Year = match.Album.ReleaseDate?.Year
        };
    }

    private static Dictionary<int, Artist> BuildArtistByMetadataIdMap(IReadOnlyList<Artist> artists)
    {
        var map = new Dictionary<int, Artist>();
        if (artists == null)
        {
            return map;
        }

        foreach (var artist in artists)
        {
            if (artist == null)
            {
                continue;
            }

            // ArtistMetadataId is 1:1 with an Artist; last-wins is harmless for the degenerate
            // duplicate case.
            map[artist.ArtistMetadataId] = artist;
        }

        return map;
    }

    private static string ResolveArtistName(Album album, Artist artist)
    {
        if (album == null)
        {
            return "Artist 0";
        }

        if (!string.IsNullOrWhiteSpace(artist?.Name))
        {
            return artist.Name;
        }

        return $"Artist {artist?.Id ?? 0}";
    }

    private static double ComputeArtistWeight(Artist artist, int albumCount, double matchScore)
    {
        var added = NormalizeAdded(artist.Added);
        var recency = added == DateTime.MinValue ? 0.5 : 1.0;
        var productivity = Math.Clamp(albumCount / 5.0, 0.0, 1.0);
        return Math.Clamp((matchScore * 0.5) + (recency * 0.3) + (productivity * 0.2), 0.0, 1.0);
    }

    private static DateTime NormalizeAdded(DateTime? value)
    {
        if (!value.HasValue || value.Value == default)
        {
            return DateTime.MinValue;
        }

        return value.Value;
    }

    private sealed record ArtistMatch(Artist Artist, HashSet<string> MatchedStyles, double Score);

    private sealed record AlbumMatch(Album Album, HashSet<string> MatchedStyles, double Score);
}
