using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Time;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public sealed class PlanCache : IPlanCache
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _fingerprintIndex = new(StringComparer.Ordinal);
    private readonly IMetrics _metrics;
    private readonly IClock _clock;
    private readonly IReadOnlyDictionary<string, string> _metricTags;

    private readonly struct CacheEntry
    {
        public CacheEntry(string key, PromptPlan plan, DateTime expires, string fingerprint)
        {
            Key = key;
            Plan = plan;
            Expires = expires;
            Fingerprint = fingerprint;
        }

        public string Key { get; }

        public PromptPlan Plan { get; }

        public DateTime Expires { get; }

        public string Fingerprint { get; }
    }

    public PlanCache(int capacity = 256, IMetrics? metrics = null, IClock? clock = null)
    {
        _capacity = Math.Max(16, capacity);
        _metrics = metrics ?? new NoOpMetrics();
        _clock = clock ?? SystemClock.Instance;
        _metricTags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cache"] = "prompt_plan"
        };
    }

    public bool TryGet(string key, out PromptPlan plan)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                plan = default!;
                RecordMiss();
                return false;
            }

            if (node.Value.Expires <= _clock.UtcNow)
            {
                RemoveNode(node);
                plan = default!;
                RecordEvict();
                RecordMiss();
                RecordSize();
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            plan = node.Value.Plan;
            RecordHit();
            return true;
        }
    }

    public void Set(string key, PromptPlan plan, TimeSpan ttl)
    {
        var expires = _clock.UtcNow + ttl;
        var fingerprint = plan.LibraryFingerprint ?? string.Empty;

        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                var updated = new CacheEntry(key, plan, expires, fingerprint);
                var node = new LinkedListNode<CacheEntry>(updated);
                _lru.AddFirst(node);
                _map[key] = node;
                UpdateFingerprintIndex(existing.Value.Fingerprint, key, removeOnly: true);
                UpdateFingerprintIndex(fingerprint, key, removeOnly: false);
                RecordSize();
                return;
            }

            var entry = new CacheEntry(key, plan, expires, fingerprint);
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
        _metrics.Record("prompt.plan_cache_hit", 1, _metricTags);
    }

    private void RecordMiss()
    {
        _metrics.Record("prompt.plan_cache_miss", 1, _metricTags);
    }

    private void RecordEvict()
    {
        _metrics.Record("prompt.plan_cache_evict", 1, _metricTags);
    }

    private void RecordSize()
    {
        _metrics.Record("prompt.plan_cache_size", _map.Count, _metricTags);
    }
}
