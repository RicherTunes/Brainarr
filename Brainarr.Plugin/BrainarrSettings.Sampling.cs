using System;
using System.Text.Json.Serialization;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    public partial class BrainarrSettings
    {
        private SamplingShape _samplingShape = SamplingShape.Default;
        private CacheSettings? _cacheSettings;

        [JsonIgnore]
        internal CacheSettings EffectiveCacheSettings => GetCacheSettings();

        // Advanced sampling controls (visible under Advanced settings).
        [JsonPropertyName("sampling_shape")]
        public SamplingShape SamplingShape
        {
            get => _samplingShape;
            set => _samplingShape = value ?? SamplingShape.Default;
        }


        [JsonIgnore]
        internal SamplingShape EffectiveSamplingShape => _samplingShape ?? SamplingShape.Default;

        private SamplingShape GetSamplingShape() => _samplingShape ?? SamplingShape.Default;

        private CacheSettings GetCacheSettings() => (_cacheSettings ?? CacheSettings.Default).Normalize();

        private void UpdateCacheSettings(Func<CacheSettings, CacheSettings> mutator)
        {
            _cacheSettings = mutator(GetCacheSettings()).Normalize();
        }

        private void UpdateSamplingShape(Func<SamplingShape, SamplingShape> mutator)
        {
            _samplingShape = mutator(GetSamplingShape()) ?? SamplingShape.Default;
        }

        private static SamplingShape.ModeDistribution UpdateDistribution(
            SamplingShape.ModeDistribution distribution,
            int? topPercent = null,
            int? recentPercent = null)
        {
            var top = Math.Clamp(topPercent ?? distribution.TopPercent, 0, 100);
            var recent = Math.Clamp(recentPercent ?? distribution.RecentPercent, 0, 100 - top);
            return new SamplingShape.ModeDistribution(top, recent);
        }

        private void UpdateArtistDistribution(DiscoveryMode mode, int? topPercent = null, int? recentPercent = null)
        {
            UpdateSamplingShape(shape =>
            {
                var artist = shape.Artist ?? SamplingShape.ModeShape.CreateArtistDefaults();
                var updatedArtist = mode switch
                {
                    DiscoveryMode.Similar => artist with { Similar = UpdateDistribution(artist.Similar, topPercent, recentPercent) },
                    DiscoveryMode.Adjacent => artist with { Adjacent = UpdateDistribution(artist.Adjacent, topPercent, recentPercent) },
                    DiscoveryMode.Exploratory => artist with { Exploratory = UpdateDistribution(artist.Exploratory, topPercent, recentPercent) },
                    _ => artist
                };

                return shape with { Artist = updatedArtist };
            });
        }

        private void UpdateAlbumDistribution(DiscoveryMode mode, int? topPercent = null, int? recentPercent = null)
        {
            UpdateSamplingShape(shape =>
            {
                var album = shape.Album ?? SamplingShape.ModeShape.CreateAlbumDefaults();
                var updatedAlbum = mode switch
                {
                    DiscoveryMode.Similar => album with { Similar = UpdateDistribution(album.Similar, topPercent, recentPercent) },
                    DiscoveryMode.Adjacent => album with { Adjacent = UpdateDistribution(album.Adjacent, topPercent, recentPercent) },
                    DiscoveryMode.Exploratory => album with { Exploratory = UpdateDistribution(album.Exploratory, topPercent, recentPercent) },
                    _ => album
                };

                return shape with { Album = updatedAlbum };
            });
        }

        private const string SamplingShapeSection = "Sampling Shape (Advanced)";
        private const string CacheSection = "Plan Cache (Advanced)";
        private const string RenderingSection = "Prompt Rendering (Advanced)";

        [FieldDefinition(160, Label = "Artist Similar Top %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of artist candidates drawn from top matches when discovery mode is Similar.")]
        public int SamplingArtistSimilarTopPercent
        {
            get => GetSamplingShape().Artist.Similar.TopPercent;
            set => UpdateArtistDistribution(DiscoveryMode.Similar, topPercent: value);
        }

        [FieldDefinition(161, Label = "Artist Similar Recent %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of artist candidates drawn from recent additions when discovery mode is Similar.")]
        public int SamplingArtistSimilarRecentPercent
        {
            get => GetSamplingShape().Artist.Similar.RecentPercent;
            set => UpdateArtistDistribution(DiscoveryMode.Similar, recentPercent: value);
        }

        [FieldDefinition(162, Label = "Artist Adjacent Top %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of artist candidates drawn from top matches when discovery mode is Adjacent.")]
        public int SamplingArtistAdjacentTopPercent
        {
            get => GetSamplingShape().Artist.Adjacent.TopPercent;
            set => UpdateArtistDistribution(DiscoveryMode.Adjacent, topPercent: value);
        }

        [FieldDefinition(163, Label = "Artist Adjacent Recent %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of artist candidates drawn from recent additions when discovery mode is Adjacent.")]
        public int SamplingArtistAdjacentRecentPercent
        {
            get => GetSamplingShape().Artist.Adjacent.RecentPercent;
            set => UpdateArtistDistribution(DiscoveryMode.Adjacent, recentPercent: value);
        }

        [FieldDefinition(164, Label = "Artist Exploratory Top %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of artist candidates drawn from top matches when discovery mode is Exploratory.")]
        public int SamplingArtistExploratoryTopPercent
        {
            get => GetSamplingShape().Artist.Exploratory.TopPercent;
            set => UpdateArtistDistribution(DiscoveryMode.Exploratory, topPercent: value);
        }

        [FieldDefinition(165, Label = "Artist Exploratory Recent %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of artist candidates drawn from recent additions when discovery mode is Exploratory.")]
        public int SamplingArtistExploratoryRecentPercent
        {
            get => GetSamplingShape().Artist.Exploratory.RecentPercent;
            set => UpdateArtistDistribution(DiscoveryMode.Exploratory, recentPercent: value);
        }

        [FieldDefinition(166, Label = "Album Similar Top %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of album candidates drawn from top matches when discovery mode is Similar.")]
        public int SamplingAlbumSimilarTopPercent
        {
            get => GetSamplingShape().Album.Similar.TopPercent;
            set => UpdateAlbumDistribution(DiscoveryMode.Similar, topPercent: value);
        }

        [FieldDefinition(167, Label = "Album Similar Recent %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of album candidates drawn from recent additions when discovery mode is Similar.")]
        public int SamplingAlbumSimilarRecentPercent
        {
            get => GetSamplingShape().Album.Similar.RecentPercent;
            set => UpdateAlbumDistribution(DiscoveryMode.Similar, recentPercent: value);
        }

        [FieldDefinition(168, Label = "Album Adjacent Top %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of album candidates drawn from top matches when discovery mode is Adjacent.")]
        public int SamplingAlbumAdjacentTopPercent
        {
            get => GetSamplingShape().Album.Adjacent.TopPercent;
            set => UpdateAlbumDistribution(DiscoveryMode.Adjacent, topPercent: value);
        }

        [FieldDefinition(169, Label = "Album Adjacent Recent %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of album candidates drawn from recent additions when discovery mode is Adjacent.")]
        public int SamplingAlbumAdjacentRecentPercent
        {
            get => GetSamplingShape().Album.Adjacent.RecentPercent;
            set => UpdateAlbumDistribution(DiscoveryMode.Adjacent, recentPercent: value);
        }

        [FieldDefinition(170, Label = "Album Exploratory Top %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of album candidates drawn from top matches when discovery mode is Exploratory.")]
        public int SamplingAlbumExploratoryTopPercent
        {
            get => GetSamplingShape().Album.Exploratory.TopPercent;
            set => UpdateAlbumDistribution(DiscoveryMode.Exploratory, topPercent: value);
        }

        [FieldDefinition(171, Label = "Album Exploratory Recent %", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Percentage of album candidates drawn from recent additions when discovery mode is Exploratory.")]
        public int SamplingAlbumExploratoryRecentPercent
        {
            get => GetSamplingShape().Album.Exploratory.RecentPercent;
            set => UpdateAlbumDistribution(DiscoveryMode.Exploratory, recentPercent: value);
        }

        [FieldDefinition(172, Label = "Minimum Albums per Artist", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Floor for albums per artist after compression. Increase to emphasise depth per artist.")]
        public int SamplingMaxAlbumsPerGroupFloor
        {
            get => GetSamplingShape().MaxAlbumsPerGroupFloor;
            set => UpdateSamplingShape(shape => shape with { MaxAlbumsPerGroupFloor = Math.Clamp(value, 0, 10) });
        }

        [FieldDefinition(173, Label = "Relaxed Match Inflation", Type = FieldType.Number, Advanced = true, Section = SamplingShapeSection,
            HelpText = "Maximum multiplier applied when relaxed style matching expands artist/album pools.")]
        public double SamplingMaxRelaxedInflation
        {
            get => GetSamplingShape().MaxRelaxedInflation;
            set => UpdateSamplingShape(shape => shape with { MaxRelaxedInflation = Math.Clamp(value, 1.0, 5.0) });
        }

        [FieldDefinition(174, Label = "Plan Cache Capacity", Type = FieldType.Number, Advanced = true, Section = CacheSection,
            HelpText = "Maximum prompt plans retained in the planner cache. Lower values reduce memory; higher values favour warm cache hits.")]
        public int PlanCacheCapacity
        {
            get => GetCacheSettings().PlanCacheCapacity;
            set => UpdateCacheSettings(settings => settings.WithCapacity(value));
        }

        [FieldDefinition(175, Label = "Plan Cache TTL (minutes)", Type = FieldType.Number, Advanced = true, Section = CacheSection,
            HelpText = "How long to keep cached plans warm before expiry. Increase for large libraries; decrease to favour fresher sampling.")]
        public int PlanCacheTtlMinutes
        {
            get => (int)Math.Round(GetCacheSettings().PlanCacheTtl.TotalMinutes);
            set => UpdateCacheSettings(settings => settings.WithTtl(TimeSpan.FromMinutes(value)));
        }

        [FieldDefinition(176, Label = "Minimal Prompt Formatting", Type = FieldType.Checkbox, Advanced = true, Section = RenderingSection,
            HelpText = "Swap emoji headings for ASCII-only equivalents and tighten whitespace. Enable for providers that require strict JSON or minimal token overhead.")]
        public bool PreferMinimalPromptFormatting { get; set; }
    }
}
