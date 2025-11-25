using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;


internal static class PlannerBuild
{
    public const string ConfigVersion = "2025-09-30-a";
}

public class LibraryPromptPlanner : IPromptPlanner
{
    private readonly Logger _logger;
    private readonly IPlanCache? _planCache;
    private int _planCacheCapacity = CacheSettings.DefaultCapacity;
    private readonly IStyleSelectionService _styleSelectionService;
    private readonly ISamplingService _samplingService;
    private readonly ISignatureService _signatureService;
    private readonly ICompressionPolicy _compressionPolicy;
    private readonly IContextPolicy _contextPolicy;
    private TimeSpan _planCacheTtl;
    private readonly bool _ttlFixed;

    public LibraryPromptPlanner(
        Logger logger,
        IStyleCatalogService styleCatalog,
        IPlanCache? planCache = null,
        TimeSpan? planCacheTtl = null,
        IStyleSelectionService? styleSelectionService = null,
        ISamplingService? samplingService = null,
        ISignatureService? signatureService = null,
        ICompressionPolicy? compressionPolicy = null,
        IContextPolicy? contextPolicy = null)
    {
        if (logger == null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        if (styleCatalog == null)
        {
            throw new ArgumentNullException(nameof(styleCatalog));
        }

        _logger = logger;
        _planCache = planCache;
        _contextPolicy = contextPolicy ?? new DefaultContextPolicy();
        _compressionPolicy = compressionPolicy ?? new DefaultCompressionPolicy();
        _styleSelectionService = styleSelectionService ?? new DefaultStyleSelectionService(_logger, styleCatalog);
        _samplingService = samplingService ?? new DefaultSamplingService(_logger, styleCatalog, _contextPolicy);
        _signatureService = signatureService ?? new DefaultSignatureService();

        _ttlFixed = planCacheTtl.HasValue;
        ConfigureCacheTtl(planCacheTtl ?? TimeSpan.FromMinutes(CacheSettings.DefaultTtlMinutes));
    }

    public void ConfigureCacheTtl(TimeSpan ttl)
    {
        var max = TimeSpan.FromMinutes(CacheSettings.MaxTtlMinutes);

        if (ttl <= TimeSpan.Zero)
        {
            ttl = TimeSpan.FromMinutes(CacheSettings.DefaultTtlMinutes);
        }
        else if (ttl > max)
        {
            ttl = max;
        }

        _planCacheTtl = ttl;
    }

    private void ApplyCacheSettings(CacheSettings cacheSettings)
    {
        if (cacheSettings == null || _planCache == null)
        {
            return;
        }

        var desiredCapacity = Math.Clamp(cacheSettings.PlanCacheCapacity, CacheSettings.MinCapacity, CacheSettings.MaxCapacity);
        var desiredTtl = cacheSettings.PlanCacheTtl;
        var changed = false;

        if (desiredCapacity != _planCacheCapacity)
        {
            _planCache.Configure(desiredCapacity);
            _planCacheCapacity = desiredCapacity;
            changed = true;
        }

        if (!_ttlFixed && desiredTtl != _planCacheTtl)
        {
            ConfigureCacheTtl(desiredTtl);
            changed = true;
        }

        if (changed)
        {
            _logger.Info("Plan cache configured (version={Version}): capacity={Capacity}, ttl_minutes={Ttl}",
                PlannerBuild.ConfigVersion,
                _planCacheCapacity,
                (int)_planCacheTtl.TotalMinutes);
        }
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

        var now = DateTime.UtcNow;

        if (request.Settings != null)
        {
            ApplyCacheSettings(request.Settings.EffectiveCacheSettings);
        }

        var selection = _styleSelectionService.Build(
            profile,
            request.Settings,
            request.StyleContext,
            _compressionPolicy,
            cancellationToken);

        var stylelessPlanning = !selection.HasStyles;
        var effectiveDiscoveryMode = selection.HasStyles
            ? request.Settings.DiscoveryMode
            : DiscoveryMode.Similar;

        if (stylelessPlanning && request.Settings.DiscoveryMode != DiscoveryMode.Similar)
        {
            _logger.Debug("No style anchors detected; forcing discovery mode to Similar for planning");
        }

        var signature = _signatureService.Compose(
            profile,
            request.Artists,
            request.Albums,
            selection,
            request.Settings,
            request.RecommendArtists,
            request.ModelKey,
            request.ContextWindow,
            request.TargetTokens,
            _compressionPolicy);

        var seed = signature.Seed;
        var libraryFingerprint = signature.Fingerprint;
        var planKey = signature.CacheKey;

        if (_planCache != null && _planCache.TryGet(planKey, out var cachedPlan))
        {
            return cachedPlan with
            {
                Compression = cachedPlan.Compression.Clone(),
                FromCache = true,
                PlanCacheKey = planKey
            };
        }

        var samplingShape = request.Settings?.EffectiveSamplingShape ?? SamplingShape.Default;
        var sample = _samplingService.Sample(
            request.Artists,
            request.Albums,
            request.StyleContext,
            selection,
            request.Settings,
            request.AvailableSamplingTokens,
            seed,
            cancellationToken);

        var deterministicOrderingApplied = ApplyDeterministicOrdering(sample);

        var fingerprint = ComputeSampleFingerprint(sample);
        var minAlbumsPerGroup = Math.Max(_compressionPolicy.MinAlbumsPerGroup, samplingShape.MaxAlbumsPerGroupFloor);
        var compression = new PromptCompressionState(
            sample.ArtistCount,
            sample.ArtistCount,
            5,
            minAlbumsPerGroup);

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
            GeneratedAt = now,
            FromCache = false,
            DeterministicOrderingApplied = deterministicOrderingApplied
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

    private bool ApplyDeterministicOrdering(LibrarySample sample)
    {
        if (sample == null)
        {
            return false;
        }

        var changed = false;

        if (sample.Artists.Count > 1)
        {
            var orderedArtists = sample.Artists
                .OrderByDescending(a => a.MatchScore)
                .ThenByDescending(a => a.Albums?.Count ?? 0)
                .ThenByDescending(a => NormalizeAdded(a.Added))
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.ArtistId)
                .ToList();

            if (!IsSameOrder(sample.Artists, orderedArtists))
            {
                sample.Artists.Clear();
                sample.Artists.AddRange(orderedArtists);
                changed = true;
            }
        }

        foreach (var artist in sample.Artists)
        {
            if (artist.Albums.Count <= 1)
            {
                continue;
            }

            var orderedAlbums = artist.Albums
                .OrderByDescending(a => NormalizeAdded(a.Added))
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.AlbumId)
                .ToList();

            if (!IsSameOrder(artist.Albums, orderedAlbums))
            {
                artist.Albums.Clear();
                artist.Albums.AddRange(orderedAlbums);
                changed = true;
            }
        }

        if (sample.Albums.Count > 1)
        {
            var orderedGlobalAlbums = sample.Albums
                .OrderByDescending(a => a.MatchScore)
                .ThenByDescending(a => NormalizeAdded(a.Added))
                .ThenByDescending(a => a.Year ?? int.MinValue)
                .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.AlbumId)
                .ToList();

            if (!IsSameOrder(sample.Albums, orderedGlobalAlbums))
            {
                sample.Albums.Clear();
                sample.Albums.AddRange(orderedGlobalAlbums);
                changed = true;
            }
        }

        return changed;
    }

    private static bool IsSameOrder<T>(IReadOnlyList<T> original, IReadOnlyList<T> ordered) where T : class
    {
        if (original.Count != ordered.Count)
        {
            return false;
        }

        for (var i = 0; i < original.Count; i++)
        {
            if (!ReferenceEquals(original[i], ordered[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static DateTime NormalizeAdded(DateTime? value)
    {
        if (!value.HasValue || value.Value == default)
        {
            return DateTime.MinValue;
        }

        return value.Value;
    }

    private string ComputeSampleFingerprint(LibrarySample sample)
    {
        if (sample == null)
        {
            throw new ArgumentNullException(nameof(sample));
        }

        var sb = new StringBuilder();
        foreach (var artist in sample.Artists
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.ArtistId))
        {
            sb.Append(artist.Name).Append('|');
            foreach (var album in artist.Albums
                .OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(a => a.AlbumId))
            {
                sb.Append(album.Title).Append(';');
            }
            sb.Append('#');
        }

        foreach (var album in sample.Albums
            .OrderBy(a => a.ArtistName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.AlbumId))
        {
            sb.Append(album.ArtistName).Append('-').Append(album.Title).Append('|');
        }

        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = sha.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", string.Empty, StringComparison.Ordinal);
    }
}
