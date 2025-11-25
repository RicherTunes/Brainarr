using System;
using System.Text.Json.Serialization;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration;

public sealed record SamplingShape
{
    public ModeShape Artist { get; init; } = ModeShape.CreateArtistDefaults();

    public ModeShape Album { get; init; } = ModeShape.CreateAlbumDefaults();

    public int MaxAlbumsPerGroupFloor { get; init; } = 3;

    public double MaxRelaxedInflation { get; init; } = 3.0;

    [JsonIgnore]
    public static SamplingShape Default { get; } = new();

    public ModeDistribution GetArtistDistribution(DiscoveryMode mode)
    {
        return Artist.For(mode);
    }

    public ModeDistribution GetAlbumDistribution(DiscoveryMode mode)
    {
        return Album.For(mode);
    }

    public sealed record ModeShape
    {
        public ModeDistribution Similar { get; init; } = new ModeDistribution(TopPercent: 60, RecentPercent: 30);

        public ModeDistribution Adjacent { get; init; } = new ModeDistribution(TopPercent: 45, RecentPercent: 35);

        public ModeDistribution Exploratory { get; init; } = new ModeDistribution(TopPercent: 35, RecentPercent: 40);

        public static ModeShape CreateArtistDefaults()
        {
            return new ModeShape
            {
                Similar = new ModeDistribution(TopPercent: 60, RecentPercent: 30),
                Adjacent = new ModeDistribution(TopPercent: 45, RecentPercent: 35),
                Exploratory = new ModeDistribution(TopPercent: 35, RecentPercent: 40)
            };
        }

        public static ModeShape CreateAlbumDefaults()
        {
            return new ModeShape
            {
                Similar = new ModeDistribution(TopPercent: 55, RecentPercent: 30),
                Adjacent = new ModeDistribution(TopPercent: 45, RecentPercent: 35),
                Exploratory = new ModeDistribution(TopPercent: 35, RecentPercent: 40)
            };
        }

        public ModeDistribution For(DiscoveryMode mode)
        {
            return mode switch
            {
                DiscoveryMode.Similar => Similar,
                DiscoveryMode.Adjacent => Adjacent,
                DiscoveryMode.Exploratory => Exploratory,
                _ => Similar
            };
        }
    }

    public readonly record struct ModeDistribution(int TopPercent, int RecentPercent)
    {
        public int RandomPercent => Math.Max(0, 100 - TopPercent - RecentPercent);
    }
}
