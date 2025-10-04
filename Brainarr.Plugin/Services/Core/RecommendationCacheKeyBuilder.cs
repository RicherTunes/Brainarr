using System;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Deterministic;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface IPlannerVersionProvider
    {
        string GetConfigVersion();
    }

    internal sealed class DefaultPlannerVersionProvider : IPlannerVersionProvider
    {
        public string GetConfigVersion() => Services.Prompting.PlannerBuild.ConfigVersion;
    }

    public interface IRecommendationCacheKeyBuilder
    {
        string Build(BrainarrSettings settings, LibraryProfile profile);
    }

    public sealed class RecommendationCacheKeyBuilder : IRecommendationCacheKeyBuilder
    {
        private readonly IPlannerVersionProvider _planner;

        public RecommendationCacheKeyBuilder(IPlannerVersionProvider planner)
        {
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        }

        public string Build(BrainarrSettings settings, LibraryProfile profile)
        {
            var styles = settings?.StyleFilters == null ? Array.Empty<string>()
                : settings.StyleFilters.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();

            var genreKeys = profile?.TopGenres?.Keys == null ? Array.Empty<string>()
                : profile.TopGenres.Keys.OrderBy(k => k, StringComparer.Ordinal).Take(5).ToArray();

            var topArtists = profile?.TopArtists == null ? Array.Empty<string>()
                : profile.TopArtists.OrderBy(a => a, StringComparer.Ordinal).Take(5).ToArray();

            var effectiveModel = settings?.EffectiveModel ?? settings?.ModelSelection ?? string.Empty;

            var payload = new
            {
                cacheV = Configuration.BrainarrConstants.CacheKeyVersion,
                sanV = Configuration.BrainarrConstants.SanitizerVersion,
                planV = _planner.GetConfigVersion(),
                provider = settings.Provider.ToString(),
                mode = settings.DiscoveryMode.ToString(),
                recmode = settings.RecommendationMode.ToString(),
                sampling = settings.SamplingStrategy.ToString(),
                relax = settings.RelaxStyleMatching == true,
                model = effectiveModel,
                max = settings.MaxRecommendations,
                maxStyles = settings.MaxSelectedStyles,
                styles,
                genres = genreKeys,
                artists = topArtists,
                shape = new
                {
                    maxGroup = settings.EffectiveSamplingShape.MaxAlbumsPerGroupFloor,
                    inflation = settings.EffectiveSamplingShape.MaxRelaxedInflation
                }
            };

            return KeyBuilder.Build("rec", payload, version: 1, take: 24);
        }
    }
}
