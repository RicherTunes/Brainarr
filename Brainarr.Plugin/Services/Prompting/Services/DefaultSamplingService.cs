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

        var sample = new LibrarySample();
        var rng = new Random(seed);

        sample.Artists.AddRange(SampleArtists(artistMatches, allAlbums, selection, settings.DiscoveryMode, samplingShape, targetArtistCount, rng));
        sample.Albums.AddRange(SampleAlbums(albumMatches, selection, settings.DiscoveryMode, samplingShape, targetAlbumCount, rng));

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
                expandedIds = context.StyleIndex.GetArtistsForStyles(selection.ExpandedSlugs);
                var strictCount = Math.Max(1, strictIds.Count);
                var relaxedLimit = strictCount * samplingShape.MaxRelaxedInflation;
                if (expandedIds.Count > relaxedLimit)
                {
                    relaxedTrimmed = true;
                }
                else if (expandedIds.Count > strictIds.Count)
                {
                    candidateIds = expandedIds;
                    relaxedApplied = true;
                }
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
                    "Relaxed artist style matches trimmed: strict={StrictCount}, candidate={CandidateCount}, limitFactor={Limit}, slugs=[{Expanded}]",
                    strictIds.Count,
                    expandedIds.Count,
                    samplingShape.MaxRelaxedInflation,
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
                expandedIds = context.StyleIndex.GetAlbumsForStyles(selection.ExpandedSlugs);
                var strictCount = Math.Max(1, strictIds.Count);
                var relaxedLimit = strictCount * samplingShape.MaxRelaxedInflation;
                if (expandedIds.Count > relaxedLimit)
                {
                    relaxedTrimmed = true;
                }
                else if (expandedIds.Count > strictIds.Count)
                {
                    candidateIds = expandedIds;
                    relaxedApplied = true;
                }
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
                    "Relaxed album style matches trimmed: strict={StrictCount}, candidate={CandidateCount}, limitFactor={Limit}, slugs=[{Expanded}]",
                    strictIds.Count,
                    expandedIds.Count,
                    samplingShape.MaxRelaxedInflation,
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

                var sampleAlbum = CreateSampleAlbum(match);
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

                var sampleAlbum = CreateSampleAlbum(candidate);
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

    private static LibrarySampleAlbum CreateSampleAlbum(AlbumMatch match)
    {
        return new LibrarySampleAlbum
        {
            AlbumId = match.Album.Id,
            ArtistId = match.Album.ArtistId,
            ArtistName = ResolveArtistName(match.Album),
            Title = string.IsNullOrWhiteSpace(match.Album.Title) ? $"Album {match.Album.Id}" : match.Album.Title,
            MatchedStyles = match.MatchedStyles.ToArray(),
            MatchScore = match.Score,
            Added = NormalizeAdded(match.Album.Added),
            Year = match.Album.ReleaseDate?.Year
        };
    }

    private static string ResolveArtistName(Album album)
    {
        if (album == null)
        {
            return "Artist 0";
        }

        var name = album.Artist?.Value?.Name ?? album.ArtistMetadata?.Value?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            return $"Artist {album.ArtistId}";
        }

        return name;
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
