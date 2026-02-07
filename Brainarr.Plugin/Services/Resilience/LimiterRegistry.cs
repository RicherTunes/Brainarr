using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Resilience
{
    public interface ILimiterRegistry
    {
        /// <summary>
        /// Acquire a concurrency slot for the given model key. Dispose the returned lease to release.
        /// </summary>
        Task<IDisposable> AcquireAsync(ModelKey key, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Lightweight concurrency limiter per provider+model using SemaphoreSlim.
    /// Designed to be a safe default even on older target frameworks.
    /// </summary>
    public sealed class LimiterRegistry : ILimiterRegistry
    {
        private readonly ConcurrentDictionary<ModelKey, SemaphoreSlim> _semaphores = new();
        private static readonly ConcurrentDictionary<ModelKey, SemaphoreSlim> _throttledSemaphores = new();
        private static readonly ConcurrentDictionary<string, int> _overrides = new(); // provider -> concurrency
        private static readonly ConcurrentDictionary<string, (DateTimeOffset start, DateTimeOffset until, int cap)> _throttleUntil = new(); // key: provider:model
        private static readonly ConcurrentDictionary<ModelKey, int> _throttleCaps = new();
        private static volatile bool _adaptiveEnabled = false;
        private static int? _adaptiveCloudCap = null;
        private static int? _adaptiveLocalCap = null;
        private static int _adaptiveSeconds = 60;
        private static readonly Timer _maintenanceTimer;

        static LimiterRegistry()
        {
            // Periodic cleanup of expired throttle entries to prevent unbounded growth.
            _maintenanceTimer = new Timer(_ =>
            {
                try { MaintenanceSweep(); } catch (Exception) { /* Non-critical */ }
            }, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
        }

        public static void ConfigureFromSettings(NzbDrone.Core.ImportLists.Brainarr.BrainarrSettings settings)
        {
            if (settings == null) return;
            void Set(string prov, int? value)
            {
                if (value.HasValue && value.Value > 0)
                {
                    _overrides[prov] = value.Value;
                }
            }
            // Local providers
            Set("ollama", settings.MaxConcurrentPerModelLocal);
            Set("lmstudio", settings.MaxConcurrentPerModelLocal);
            // Cloud providers
            Set("openai", settings.MaxConcurrentPerModelCloud);
            Set("anthropic", settings.MaxConcurrentPerModelCloud);
            Set("openrouter", settings.MaxConcurrentPerModelCloud);
            Set("groq", settings.MaxConcurrentPerModelCloud);
            Set("perplexity", settings.MaxConcurrentPerModelCloud);
            Set("deepseek", settings.MaxConcurrentPerModelCloud);
            Set("gemini", settings.MaxConcurrentPerModelCloud);

            // Adaptive throttling
            _adaptiveEnabled = settings.EnableAdaptiveThrottling;
            _adaptiveCloudCap = settings.AdaptiveThrottleCloudCap;
            _adaptiveLocalCap = settings.AdaptiveThrottleLocalCap;
            _adaptiveSeconds = Math.Max(5, settings.AdaptiveThrottleSeconds > 0 ? settings.AdaptiveThrottleSeconds : 60);
        }

        private static int DefaultConcurrencyFor(string provider)
        {
            // Conservative defaults; can be tuned later or surfaced via settings
            switch ((provider ?? string.Empty).ToLowerInvariant())
            {
                case "ollama":
                case "lmstudio":
                    if (_overrides.TryGetValue("ollama", out var vLocal)) return vLocal;
                    return 128; // local backends – effectively no cap in practice
                case "openai":
                case "anthropic":
                case "openrouter":
                case "groq":
                case "perplexity":
                case "deepseek":
                case "gemini":
                    if (_overrides.TryGetValue("openai", out var v)) return v; // use any cloud override
                    return 64; // cloud backends – high to avoid unintended throttling in tests
                default:
                    if (_overrides.TryGetValue((provider ?? string.Empty).ToLowerInvariant(), out var vx)) return vx;
                    return 64;
            }
        }

        public async Task<IDisposable> AcquireAsync(ModelKey key, CancellationToken cancellationToken)
        {
            var useThrottle = IsThrottleActive(key, out var baseCap, out var start, out var until);
            var cap = baseCap;
            if (useThrottle)
            {
                // Simple 2-step decay: first half -> base cap, second half -> double cap (up to default), last 20% -> default
                var now = DateTimeOffset.UtcNow;
                var total = (until - start).TotalSeconds;
                var elapsed = (now - start).TotalSeconds;
                if (total > 0)
                {
                    var frac = elapsed / total;
                    var defCap = DefaultConcurrencyFor(key.Provider);
                    if (frac >= 0.8)
                    {
                        cap = defCap;
                    }
                    else if (frac >= 0.5)
                    {
                        cap = Math.Min(defCap, Math.Max(baseCap + 1, baseCap * 2));
                    }
                    else
                    {
                        cap = baseCap;
                    }
                }
            }
            SemaphoreSlim sem;
            if (useThrottle)
            {
                sem = _throttledSemaphores.GetOrAdd(key, k => new SemaphoreSlim(Math.Max(1, Math.Min(DefaultConcurrencyFor(k.Provider), cap))))
                      ?? _semaphores.GetOrAdd(key, k => new SemaphoreSlim(DefaultConcurrencyFor(k.Provider)));
                // If cap increased since last time, release additional tokens to grow concurrency
                var current = _throttleCaps.GetOrAdd(key, _ => cap);
                if (cap > current)
                {
                    var delta = cap - current;
                    try { sem.Release(delta); } catch { /* ignore over-release errors */ }
                    _throttleCaps[key] = cap;
                }
            }
            else
            {
                sem = _semaphores.GetOrAdd(key, k => new SemaphoreSlim(DefaultConcurrencyFor(k.Provider)));
            }
            await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(sem);
        }

        private sealed class Releaser : IDisposable
        {
            private readonly SemaphoreSlim _sem;
            private bool _released;
            public Releaser(SemaphoreSlim sem) => _sem = sem;
            public void Dispose()
            {
                if (_released) return;
                _released = true;
                _sem.Release();
            }
        }

        private static bool IsThrottleActive(ModelKey key, out int cap, out DateTimeOffset start, out DateTimeOffset until)
        {
            cap = 0;
            start = DateTimeOffset.MinValue;
            until = DateTimeOffset.MinValue;
            var origin = $"{(key.Provider ?? string.Empty).ToLowerInvariant()}:{(key.ModelId ?? string.Empty).ToLowerInvariant()}";
            if (_throttleUntil.TryGetValue(origin, out var entry))
            {
                if (DateTimeOffset.UtcNow < entry.until)
                {
                    cap = entry.cap;
                    start = entry.start;
                    until = entry.until;
                    return true;
                }
                // Expired: evict throttle entries and allow default path
                _throttleUntil.TryRemove(origin, out _);
                try
                {
                    var k = new ModelKey((key.Provider ?? string.Empty).ToLowerInvariant(), (key.ModelId ?? string.Empty).ToLowerInvariant());
                    _throttleCaps.TryRemove(k, out _);
                    // Best-effort eviction of throttled semaphore. Do not Dispose due to possible in-flight holders.
                    _throttledSemaphores.TryRemove(k, out _);
                }
                catch (Exception) { /* Non-critical */ }
            }
            return false;
        }

        public static void RegisterThrottle(string origin, TimeSpan ttl, int cap)
        {
            if (!_adaptiveEnabled || string.IsNullOrWhiteSpace(origin)) return;
            var start = DateTimeOffset.UtcNow;
            var until = start + (ttl <= TimeSpan.Zero ? TimeSpan.FromSeconds(_adaptiveSeconds) : ttl);
            var norm = origin.Trim().ToLowerInvariant();
            var isLocal = norm.StartsWith("ollama:") || norm.StartsWith("lmstudio:");
            var effCap = isLocal ? (_adaptiveLocalCap ?? cap) : (_adaptiveCloudCap ?? cap);
            _throttleUntil[norm] = (start, until, Math.Max(1, effCap));
        }

        private static void MaintenanceSweep()
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var kv in _throttleUntil)
                {
                    if (kv.Value.until <= now)
                    {
                        _throttleUntil.TryRemove(kv.Key, out _);
                        try
                        {
                            var parts = kv.Key.Split(':');
                            var provider = parts.Length > 0 ? parts[0] : string.Empty;
                            var model = parts.Length > 1 ? parts[1] : string.Empty;
                            var k = new ModelKey(provider, model);
                            _throttleCaps.TryRemove(k, out _);
                            _throttledSemaphores.TryRemove(k, out _);
                        }
                        catch (Exception) { /* Non-critical */ }
                    }
                }
            }
            catch (Exception) { /* Non-critical */ }
        }

        // Test-only helpers (internal) to validate maintenance without exposing public surface.
        internal static void RunMaintenanceOnce() => MaintenanceSweep();

        /// <summary>
        /// <b>TEST-ONLY:</b> Clears all static dictionaries and resets adaptive flags
        /// so parallel test classes don't cross-contaminate via shared state.
        /// </summary>
        internal static void ResetForTesting()
        {
            _throttledSemaphores.Clear();
            _overrides.Clear();
            _throttleUntil.Clear();
            _throttleCaps.Clear();
            _adaptiveEnabled = false;
            _adaptiveCloudCap = null;
            _adaptiveLocalCap = null;
            _adaptiveSeconds = 60;
        }

        internal static bool HasThrottleFor(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin)) return false;
            return _throttleUntil.ContainsKey(origin.Trim().ToLowerInvariant());
        }
    }
}
