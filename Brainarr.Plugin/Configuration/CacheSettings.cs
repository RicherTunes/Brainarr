using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration;

public sealed record CacheSettings
{
    public const int MinCapacity = 16;
    public const int MaxCapacity = 1024;
    public const int DefaultCapacity = 256;

    public const int MinTtlMinutes = 1;
    public const int MaxTtlMinutes = 60;
    public const int DefaultTtlMinutes = 5;

    public int PlanCacheCapacity { get; init; } = DefaultCapacity;

    public TimeSpan PlanCacheTtl { get; init; } = TimeSpan.FromMinutes(DefaultTtlMinutes);

    public static CacheSettings Default { get; } = new CacheSettings();

    public CacheSettings Normalize()
    {
        var capacity = Math.Clamp(PlanCacheCapacity, MinCapacity, MaxCapacity);
        var ttl = ClampTtl(PlanCacheTtl);
        return this with
        {
            PlanCacheCapacity = capacity,
            PlanCacheTtl = ttl
        };
    }

    public CacheSettings WithCapacity(int capacity)
    {
        return this with
        {
            PlanCacheCapacity = Math.Clamp(capacity, MinCapacity, MaxCapacity)
        };
    }

    public CacheSettings WithTtl(TimeSpan ttl)
    {
        return this with { PlanCacheTtl = ClampTtl(ttl) };
    }

    private static TimeSpan ClampTtl(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return TimeSpan.FromMinutes(DefaultTtlMinutes);
        }

        var max = TimeSpan.FromMinutes(MaxTtlMinutes);
        if (value > max)
        {
            return max;
        }

        return value;
    }
}
