using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;

public sealed class PlanCache : IPlanCache
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _map = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _fingerprintIndex = new(StringComparer.Ordinal);

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

    public PlanCache(int capacity = 256)
    {
        _capacity = Math.Max(16, capacity);
    }

    public bool TryGet(string key, out PromptPlan plan)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out var node))
            {
                plan = default!;
                return false;
            }

            if (node.Value.Expires <= DateTime.UtcNow)
            {
                RemoveNode(node);
                plan = default!;
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            plan = node.Value.Plan;
            return true;
        }
    }

    public void Set(string key, PromptPlan plan, TimeSpan ttl)
    {
        var expires = DateTime.UtcNow + ttl;
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
                return;
            }

            var entry = new CacheEntry(key, plan, expires, fingerprint);
            var newNode = new LinkedListNode<CacheEntry>(entry);
            _lru.AddFirst(newNode);
            _map[key] = newNode;
            UpdateFingerprintIndex(fingerprint, key, removeOnly: false);

            if (_map.Count <= _capacity)
            {
                return;
            }

            var tail = _lru.Last;
            if (tail != null)
            {
                RemoveNode(tail);
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
                }
            }

            _fingerprintIndex.Remove(libraryFingerprint);
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
}
