using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Computes deterministic sampling seeds from library profiles and settings.
    /// Pure functional logic extracted from LibraryAwarePromptBuilder (M6-2).
    /// </summary>
    internal class SamplingSeedComputer
    {
        private readonly Logger _logger;

        public SamplingSeedComputer(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

            var hashResult = StableHash.Compute(components);
            _logger.Trace(
                "Computed sampling seed from {ComponentCount} components (hash prefix {HashPrefix}) => {Seed}",
                hashResult.ComponentCount,
                hashResult.HashPrefix,
                hashResult.Seed);

            return hashResult.Seed;
        }

        internal static StableHash.StableHashResult ComputeStableHash(IEnumerable<string> components)
        {
            return StableHash.Compute(components);
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
    }
}
