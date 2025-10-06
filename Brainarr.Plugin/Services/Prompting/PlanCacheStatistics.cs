using System;
using System.Collections.Generic;
using Lidarr.Plugin.Common.Services.Caching;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting
{
    internal sealed class PlanCacheStatistics : ICacheStatistics
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, int> _hitCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _missCounts = new(StringComparer.Ordinal);
        private int _totalHits;
        private int _totalMisses;

        public int TotalHits
        {
            get
            {
                lock (_gate)
                {
                    return _totalHits;
                }
            }
        }

        public int TotalMisses
        {
            get
            {
                lock (_gate)
                {
                    return _totalMisses;
                }
            }
        }

        public double HitRate
        {
            get
            {
                lock (_gate)
                {
                    var total = _totalHits + _totalMisses;
                    if (total == 0)
                    {
                        return 0d;
                    }

                    return (double)_totalHits / total;
                }
            }
        }

        public void RecordHit(string key)
        {
            if (key == null)
            {
                return;
            }

            lock (_gate)
            {
                _totalHits++;
                if (_hitCounts.TryGetValue(key, out var count))
                {
                    _hitCounts[key] = count + 1;
                }
                else
                {
                    _hitCounts[key] = 1;
                }
            }
        }

        public void RecordMiss(string key)
        {
            if (key == null)
            {
                return;
            }

            lock (_gate)
            {
                _totalMisses++;
                if (_missCounts.TryGetValue(key, out var count))
                {
                    _missCounts[key] = count + 1;
                }
                else
                {
                    _missCounts[key] = 1;
                }
            }
        }

        public IEnumerable<string> GetHitKeys()
        {
            lock (_gate)
            {
                return new List<string>(_hitCounts.Keys);
            }
        }

        public IEnumerable<string> GetMissKeys()
        {
            lock (_gate)
            {
                return new List<string>(_missCounts.Keys);
            }
        }

        public int GetHitCount(string key)
        {
            lock (_gate)
            {
                return _hitCounts.TryGetValue(key, out var count) ? count : 0;
            }
        }

        public int GetMissCount(string key)
        {
            lock (_gate)
            {
                return _missCounts.TryGetValue(key, out var count) ? count : 0;
            }
        }

        public CacheStatisticsSnapshot GetStatistics(int totalEntries, int uniqueArtists, int uniqueAlbums)
        {
            lock (_gate)
            {
                var total = _totalHits + _totalMisses;
                var hitRate = total == 0 ? 0d : (double)_totalHits / total;
                var averageHits = totalEntries == 0 ? 0d : (double)_totalHits / totalEntries;

                return new CacheStatisticsSnapshot
                {
                    TotalEntries = totalEntries,
                    TotalHits = _totalHits,
                    TotalMisses = _totalMisses,
                    UniqueArtists = uniqueArtists,
                    UniqueAlbums = uniqueAlbums,
                    AverageHitsPerEntry = averageHits,
                    CacheSizeBytes = 0,
                    HitRate = hitRate,
                    Timestamp = DateTime.UtcNow
                };
            }
        }

        public void RemoveKey(string key)
        {
            if (key == null)
            {
                return;
            }

            lock (_gate)
            {
                _hitCounts.Remove(key);
                _missCounts.Remove(key);
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _hitCounts.Clear();
                _missCounts.Clear();
                _totalHits = 0;
                _totalMisses = 0;
            }
        }
    }
}
