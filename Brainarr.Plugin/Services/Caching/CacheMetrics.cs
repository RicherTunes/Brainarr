using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Caching
{
    internal class CacheMetrics
    {
        private long _totalHits;
        private long _totalMisses;
        private long _totalErrors;
        private readonly ConcurrentDictionary<CacheLevel, long> _hitsByLevel = new();
        private readonly ConcurrentBag<double> _accessTimes = new();

        public long TotalHits => _totalHits;
        public long TotalMisses => _totalMisses;
        public DateTime? LastMaintenanceRun { get; set; }

        public void RecordHit(CacheLevel level, TimeSpan duration)
        {
            Interlocked.Increment(ref _totalHits);
            _hitsByLevel.AddOrUpdate(level, 1, (_, count) => count + 1);
            RecordAccessTime(duration.TotalMilliseconds);
        }

        public void RecordMiss(TimeSpan duration)
        {
            Interlocked.Increment(ref _totalMisses);
            RecordAccessTime(duration.TotalMilliseconds);
        }

        public void RecordError()
        {
            Interlocked.Increment(ref _totalErrors);
        }

        public void RecordSet(int itemCount)
        {
            // Track set operations if needed
        }

        public double GetHitRatio()
        {
            var total = _totalHits + _totalMisses;
            return total > 0 ? (double)_totalHits / total : 0;
        }

        public double GetAverageAccessTime()
        {
            return _accessTimes.Any() ? _accessTimes.Average() : 0;
        }

        public Dictionary<CacheLevel, long> GetHitsByLevel()
        {
            return new Dictionary<CacheLevel, long>(_hitsByLevel);
        }

        public void Reset()
        {
            _totalHits = 0;
            _totalMisses = 0;
            _totalErrors = 0;
            _hitsByLevel.Clear();
            _accessTimes.Clear();
        }

        private void RecordAccessTime(double milliseconds)
        {
            _accessTimes.Add(milliseconds);

            // Keep only last 1000 access times
            while (_accessTimes.Count > 1000)
            {
                _accessTimes.TryTake(out _);
            }
        }
    }
}
