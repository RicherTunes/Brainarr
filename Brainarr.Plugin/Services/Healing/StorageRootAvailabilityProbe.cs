using System;
using System.Collections.Generic;
using System.Threading;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

internal sealed class StorageRootAvailabilityProbe
{
    private const int MaxCacheEntries = 128;

    private readonly Func<string, bool> _probe;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _offlineTtl;
    private readonly TimeSpan _onlineTtl;
    private readonly SemaphoreSlim _slots;
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public StorageRootAvailabilityProbe(
        Func<string, bool> probe,
        TimeSpan? timeout = null,
        TimeSpan? offlineTtl = null,
        TimeSpan? onlineTtl = null,
        int maxConcurrentProbes = 2)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _timeout = timeout ?? TimeSpan.FromSeconds(2);
        _offlineTtl = offlineTtl ?? TimeSpan.FromMinutes(5);
        _onlineTtl = onlineTtl ?? TimeSpan.FromSeconds(30);
        _slots = new SemaphoreSlim(Math.Max(1, maxConcurrentProbes));
    }

    public bool IsOnline(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var key = HealerStorageRoot.Key(root);
        if (TryGetCached(key, out var cached))
        {
            return cached;
        }

        if (!_slots.Wait(0))
        {
            Cache(key, false);
            return false;
        }

        var releaseByCaller = 1;
        var slotReleased = 0;
        void ReleaseSlotOnce()
        {
            if (Interlocked.Exchange(ref slotReleased, 1) == 0)
            {
                _slots.Release();
            }
        }

        try
        {
            var completed = new ManualResetEventSlim(false);
            Exception? failure = null;
            var online = false;
            var worker = new Thread(() =>
            {
                try
                {
                    online = _probe(root);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                finally
                {
                    completed.Set();
                    if (Volatile.Read(ref releaseByCaller) == 0)
                    {
                        ReleaseSlotOnce();
                    }
                }
            })
            {
                IsBackground = true,
                Name = "Brainarr storage-root availability probe",
            };

            worker.Start();
            if (!completed.Wait(_timeout))
            {
                Volatile.Write(ref releaseByCaller, 0);
                if (completed.IsSet)
                {
                    ReleaseSlotOnce();
                }

                Cache(key, false);
                return false;
            }

            if (failure is not null)
            {
                throw failure;
            }

            Cache(key, online);
            return online;
        }
        catch
        {
            Cache(key, false);
            return false;
        }
        finally
        {
            if (Volatile.Read(ref releaseByCaller) == 1)
            {
                ReleaseSlotOnce();
            }
        }
    }

    private bool TryGetCached(string key, out bool online)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (_cache.TryGetValue(key, out var entry) && entry.ExpiresAt > now)
            {
                online = entry.Online;
                return true;
            }

            _cache.Remove(key);
        }

        online = false;
        return false;
    }

    private void Cache(string key, bool online)
    {
        var ttl = online ? _onlineTtl : _offlineTtl;
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            if (!_cache.ContainsKey(key) && _cache.Count >= MaxCacheEntries)
            {
                PruneExpired(now);
            }

            if (!_cache.ContainsKey(key) && _cache.Count >= MaxCacheEntries)
            {
                EvictOldest();
            }

            _cache[key] = new CacheEntry(online, now.Add(ttl));
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        List<string>? expired = null;
        foreach (var pair in _cache)
        {
            if (pair.Value.ExpiresAt > now)
            {
                continue;
            }

            expired ??= new List<string>();
            expired.Add(pair.Key);
        }

        if (expired is null)
        {
            return;
        }

        foreach (var key in expired)
        {
            _cache.Remove(key);
        }
    }

    private void EvictOldest()
    {
        string? oldestKey = null;
        DateTimeOffset oldestExpiry = DateTimeOffset.MaxValue;
        foreach (var pair in _cache)
        {
            if (pair.Value.ExpiresAt >= oldestExpiry)
            {
                continue;
            }

            oldestKey = pair.Key;
            oldestExpiry = pair.Value.ExpiresAt;
        }

        if (oldestKey is not null)
        {
            _cache.Remove(oldestKey);
        }
    }

    private readonly record struct CacheEntry(bool Online, DateTimeOffset ExpiresAt);
}
