using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;

public sealed class DefaultSignatureService : ISignatureService
{
    public (int Seed, string Fingerprint, string CacheKey) Compose(
        LibraryProfile profile,
        IReadOnlyList<Artist> artists,
        IReadOnlyList<Album> albums,
        StylePlanContext selection,
        BrainarrSettings settings,
        bool recommendArtists,
        string modelKey,
        int contextWindow,
        int targetTokens,
        ICompressionPolicy compressionPolicy)
    {
        var components = new List<string>
        {
            (profile?.TotalArtists ?? 0).ToString(CultureInfo.InvariantCulture),
            (profile?.TotalAlbums ?? 0).ToString(CultureInfo.InvariantCulture),
            ((int)(settings?.DiscoveryMode ?? DiscoveryMode.Similar)).ToString(CultureInfo.InvariantCulture),
            ((int)(settings?.SamplingStrategy ?? SamplingStrategy.Balanced)).ToString(CultureInfo.InvariantCulture),
            settings?.RelaxStyleMatching == true ? "relaxed" : "strict",
            (settings?.MaxRecommendations ?? 20).ToString(CultureInfo.InvariantCulture)
        };
        var maxSelectedStyles = settings?.MaxSelectedStyles ?? 10;
        var relaxedThreshold = selection?.Threshold ?? 1.0;
        var compressionIdentity = GetCompressionIdentity(compressionPolicy);

        components.Add($"maxStyles:{maxSelectedStyles.ToString(CultureInfo.InvariantCulture)}");
        components.Add($"threshold:{relaxedThreshold.ToString("F2", CultureInfo.InvariantCulture)}");
        components.Add($"compression:{compressionIdentity}");

        if (selection?.SelectedSlugs != null)
        {
            components.AddRange(selection.SelectedSlugs
                .OrderBy(slug => slug, StringComparer.OrdinalIgnoreCase)
                .Select(slug => $"style:{slug}"));
        }

        if (artists != null)
        {
            components.AddRange(artists
                .Select(a => a.Id)
                .OrderBy(id => id)
                .Take(24)
                .Select(id => $"artist:{id.ToString(CultureInfo.InvariantCulture)}"));
        }

        if (albums != null)
        {
            components.AddRange(albums
                .Select(a => a.Id)
                .OrderBy(id => id)
                .Take(24)
                .Select(id => $"album:{id.ToString(CultureInfo.InvariantCulture)}"));
        }

        var stable = StableHash.Compute(components);
        var seed = stable.Seed;
        var fingerprint = stable.FullHash;

        var orderedStyles = selection?.SelectedSlugs
            .OrderBy(slug => slug, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? Array.Empty<string>();
        var styleKey = orderedStyles.Length > 0 ? string.Join("|", orderedStyles) : "_";

        var relaxed = selection?.Relaxed == true ? "relaxed" : "strict";
        var recommend = recommendArtists ? "artists" : "albums";
        var sparse = selection?.Sparse == true ? "sparse" : "dense";

        var cacheKey = string.Join('#', new[]
        {
            fingerprint,
            modelKey ?? string.Empty,
            contextWindow.ToString(CultureInfo.InvariantCulture),
            targetTokens.ToString(CultureInfo.InvariantCulture),
            (settings?.DiscoveryMode ?? DiscoveryMode.Similar).ToString(),
            (settings?.SamplingStrategy ?? SamplingStrategy.Balanced).ToString(),
            (settings?.MaxRecommendations ?? 20).ToString(CultureInfo.InvariantCulture),
            settings?.RelaxStyleMatching == true ? "relaxed-matching" : "strict-matching",
            $"maxStyles={maxSelectedStyles.ToString(CultureInfo.InvariantCulture)}",
            $"relaxedThreshold={relaxedThreshold.ToString("F2", CultureInfo.InvariantCulture)}",
            $"compression={compressionIdentity}",
            recommend,
            relaxed,
            sparse,
            seed.ToString(CultureInfo.InvariantCulture),
            styleKey
        });

        return (seed, fingerprint, cacheKey);
    }

    private static string GetCompressionIdentity(ICompressionPolicy compressionPolicy)
    {
        if (compressionPolicy is null)
        {
            return "unknown";
        }

        var typeName = compressionPolicy.GetType().Name;
        var minAlbums = compressionPolicy.MinAlbumsPerGroup.ToString(CultureInfo.InvariantCulture);
        var inflation = compressionPolicy.MaxRelaxedInflation.ToString("F2", CultureInfo.InvariantCulture);
        var cap = compressionPolicy.AbsoluteRelaxedCap.ToString(CultureInfo.InvariantCulture);
        return string.Join(":", typeName, minAlbums, inflation, cap);
    }
}
