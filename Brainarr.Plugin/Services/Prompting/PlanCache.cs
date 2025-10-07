using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Time;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public sealed class PlanCache : IPlanCache
{
    private readonly object _gate = new();
    private int _capacity;
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _fingerprintIndex = new(StringComparer.Ordinal);
    private readonly IMetrics _metrics;
    private readonly IClock _clock;
    private readonly IReadOnlyDictionary<string, string> _metricTags;
    private DateTime _lastSweep;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

    private readonly struct CacheEntry
    {
        public CacheEntry(string key, PromptPlan plan, DateTime expires, string fingerprint, TimeSpan ttl)
        {
            Key = key;
            Plan = plan;
            Expires = expires;
            Fingerprint = fingerprint;
            Ttl = ttl;
        }

        public string Key { get; }

        public PromptPlan Plan { get; }

        public DateTime Expires { get; }

        public string Fingerprint { get; }

        public TimeSpan Ttl { get; }
    }

    private static PromptPlan ClonePlan(PromptPlan source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var styleCoverage = source.StyleCoverage != null
            ? new Dictionary<string, int>(source.StyleCoverage, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var matchedStyleCounts = source.MatchedStyleCounts != null
            ? new Dictionary<string, int>(source.MatchedStyleCounts, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var trimmed = source.TrimmedStyles?.ToArray() ?? Array.Empty<string>();
        var inferred = source.InferredStyleSlugs?.ToArray() ?? Array.Empty<string>();

        return source with
        {
            Compression = source.Compression.Clone(),
            StyleCoverage = styleCoverage,
            MatchedStyleCounts = matchedStyleCounts,
            TrimmedStyles = trimmed,
            InferredStyleSlugs = inferred
        };
    }

    public PlanCache(int capacity = 256, IMetrics? metrics = null, IClock? clock = null)
    {
        // Allow small capacities for unit testing of eviction semantics
        _capacity = Math.Max(1, capacity);
        _metrics = metrics ?? new NoOpMetrics();
        _clock = clock ?? SystemClock.Instance;
        _metricTags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cache"] = "prompt_plan"
        };
        _lastSweep = _clock.UtcNow;
    }

    public void Configure(int capacity)
    {
        var normalized = Math.Max(1, capacity);

        lock (_gate)
        {
            if (_capacity == normalized)
            {
                return;
            }

            _capacity = normalized;

            while (_map.Count > _capacity)
            {
                var tail = _lru.Last;
                if (tail == null)
                {
                    break;
                }

                RemoveNode(tail);
                RecordEvict();
            }

            RecordSize();
        }
    }
    public bool TryGet(string key, out PromptPlan plan)
    {
        lock (_gate)
        {
            var now = _clock.UtcNow;
            MaybeSweepExpired(now);

            if (!_map.TryGetValue(key, out var node))
            {
                plan = default!;
                RecordMiss();
                return false;
            }

            if (node.Value.Expires <= now)
            {
                RemoveNode(node);
                plan = default!;
                RecordEvict();
                RecordMiss();
                RecordSize();
                return false;
            }

            var ttl = node.Value.Ttl;
            var refreshed = new CacheEntry(node.Value.Key, node.Value.Plan, now + ttl, node.Value.Fingerprint, ttl);
            _lru.Remove(node);
            node.Value = refreshed;
            _lru.AddFirst(node);
            plan = ClonePlan(node.Value.Plan);
            RecordHit();
            return true;
        }
    }

    public void Set(string key, PromptPlan plan, TimeSpan ttl)
    {
        var now = _clock.UtcNow;
        var expires = now + ttl;
        var fingerprint = plan.LibraryFingerprint ?? string.Empty;

        lock (_gate)
        {
            MaybeSweepExpired(now);

            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                var updated = new CacheEntry(key, ClonePlan(plan), expires, fingerprint, ttl);
                var node = new LinkedListNode<CacheEntry>(updated);
                _lru.AddFirst(node);
                _map[key] = node;
                UpdateFingerprintIndex(existing.Value.Fingerprint, key, removeOnly: true);
                UpdateFingerprintIndex(fingerprint, key, removeOnly: false);
                RecordSize();
                return;
            }

            var entry = new CacheEntry(key, ClonePlan(plan), expires, fingerprint, ttl);
            var newNode = new LinkedListNode<CacheEntry>(entry);
            _lru.AddFirst(newNode);
            _map[key] = newNode;
            UpdateFingerprintIndex(fingerprint, key, removeOnly: false);
            RecordSize();

            if (_map.Count <= _capacity)
            {
                return;
            }

            var tail = _lru.Last;
            if (tail != null)
            {
                RemoveNode(tail);
                RecordEvict();
                RecordSize();
            }
        }
    }
    public void InvalidateByFingerprint(string libraryFingerprint)
    {
        if (string.IsNullOrWhiteSpace(libraryFingerprint))
        {
            return;
        }

        lock (_gate)
        {
            if (!_fingerprintIndex.TryGetValue(libraryFingerprint, out var keys) || keys.Count == 0)
            {
                return;
            }

            foreach (var key in keys.ToArray())
            {
                if (_map.TryGetValue(key, out var node))
                {
                    RemoveNode(node);
                    RecordEvict();
                }
            }

            _fingerprintIndex.Remove(libraryFingerprint);
            RecordSize();
        }
    }

    public bool TryRemove(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_gate)
        {
            var now = _clock.UtcNow;
            MaybeSweepExpired(now);

            if (!_map.TryGetValue(key, out var node))
            {
                return false;
            }

            RemoveNode(node);
            RecordEvict();
            RecordSize();
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _lru.Clear();
            _map.Clear();
            _fingerprintIndex.Clear();
            RecordSize();
        }
    }

    private void MaybeSweepExpired(DateTime now)
    {
        if (now - _lastSweep < SweepInterval)
        {
            return;
        }

        SweepExpiredEntries(now);
        _lastSweep = now;
    }

    private void SweepExpiredEntries(DateTime now)
    {
        if (_lru.Count == 0)
        {
            return;
        }

        var removed = false;
        for (var node = _lru.Last; node != null;)
        {
            var previous = node.Previous;
            if (node.Value.Expires <= now)
            {
                RemoveNode(node);
                RecordEvict();
                removed = true;
            }

            node = previous;
        }

        if (removed)
        {
            RecordSize();
        }
    }
    private void RemoveNode(LinkedListNode<CacheEntry> node)
    {
        _lru.Remove(node);
        _map.Remove(node.Value.Key);
        UpdateFingerprintIndex(node.Value.Fingerprint, node.Value.Key, removeOnly: true);
    }

    private void UpdateFingerprintIndex(string fingerprint, string key, bool removeOnly)
    {
        if (!_fingerprintIndex.TryGetValue(fingerprint, out var keys))
        {
            if (removeOnly)
            {
                return;
            }

            keys = new HashSet<string>(StringComparer.Ordinal);
            _fingerprintIndex[fingerprint] = keys;
        }

        if (removeOnly)
        {
            keys.Remove(key);
            if (keys.Count == 0)
            {
                _fingerprintIndex.Remove(fingerprint);
            }
        }
        else
        {
            keys.Add(key);
        }
    }

    private void RecordHit()
    {
        _metrics.Record(MetricsNames.PromptPlanCacheHit, 1, _metricTags);
    }

    private void RecordMiss()
    {
        _metrics.Record(MetricsNames.PromptPlanCacheMiss, 1, _metricTags);
    }

    private void RecordEvict()
    {
        _metrics.Record(MetricsNames.PromptPlanCacheEvict, 1, _metricTags);
    }

    private void RecordSize()
    {
        _metrics.Record(MetricsNames.PromptPlanCacheSize, _map.Count, _metricTags);
    }
}
