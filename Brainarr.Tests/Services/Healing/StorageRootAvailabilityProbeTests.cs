using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using System.Reflection;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public sealed class StorageRootAvailabilityProbeTests
{
    [Fact]
    public void IsOnline_ShouldCacheCompletedProbeResult()
    {
        var calls = 0;
        var probe = new StorageRootAvailabilityProbe(
            _ =>
            {
                Interlocked.Increment(ref calls);
                return true;
            },
            timeout: TimeSpan.FromSeconds(1),
            offlineTtl: TimeSpan.FromMinutes(1),
            onlineTtl: TimeSpan.FromMinutes(1),
            maxConcurrentProbes: 1);

        probe.IsOnline("/mnt/music").Should().BeTrue();
        probe.IsOnline("/mnt/music").Should().BeTrue();

        Volatile.Read(ref calls).Should().Be(1);
    }

    [Fact]
    public void IsOnline_ShouldFailClosedAndCache_WhenProbeTimesOut()
    {
        using var release = new ManualResetEventSlim(false);
        var calls = 0;
        var probe = new StorageRootAvailabilityProbe(
            _ =>
            {
                Interlocked.Increment(ref calls);
                release.Wait();
                return true;
            },
            timeout: TimeSpan.FromMilliseconds(20),
            offlineTtl: TimeSpan.FromMinutes(1),
            onlineTtl: TimeSpan.FromMinutes(1),
            maxConcurrentProbes: 1);

        try
        {
            probe.IsOnline(@"\\offline-nas\Music").Should().BeFalse();
            probe.IsOnline(@"\\offline-nas\Music").Should().BeFalse();

            Volatile.Read(ref calls).Should().Be(1);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void IsOnline_ShouldFailClosedWithoutStartingMoreWorkers_WhenProbeSlotsAreSaturated()
    {
        using var release = new ManualResetEventSlim(false);
        var calls = 0;
        var probe = new StorageRootAvailabilityProbe(
            _ =>
            {
                Interlocked.Increment(ref calls);
                release.Wait();
                return true;
            },
            timeout: TimeSpan.FromMilliseconds(20),
            offlineTtl: TimeSpan.FromMinutes(1),
            onlineTtl: TimeSpan.FromMinutes(1),
            maxConcurrentProbes: 1);

        try
        {
            probe.IsOnline(@"\\offline-nas-a\Music").Should().BeFalse();
            probe.IsOnline(@"\\offline-nas-b\Music").Should().BeFalse();

            Volatile.Read(ref calls).Should().Be(1);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public void IsOnline_ShouldBoundCachedRoots_WhenManyDistinctRootsAreProbed()
    {
        var probe = new StorageRootAvailabilityProbe(
            _ => false,
            timeout: TimeSpan.FromSeconds(1),
            offlineTtl: TimeSpan.FromMinutes(30),
            onlineTtl: TimeSpan.FromMinutes(30),
            maxConcurrentProbes: 1);

        for (var i = 0; i < 200; i++)
        {
            probe.IsOnline("/mnt/offline-root-" + i).Should().BeFalse();
        }

        CacheCount(probe).Should().BeLessThanOrEqualTo(128);
    }

    private static int CacheCount(StorageRootAvailabilityProbe probe)
    {
        var field = typeof(StorageRootAvailabilityProbe).GetField("_cache", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        var cache = field!.GetValue(probe);
        cache.Should().NotBeNull();
        var count = cache!.GetType().GetProperty("Count");
        count.Should().NotBeNull();
        return (int)count!.GetValue(cache)!;
    }
}
