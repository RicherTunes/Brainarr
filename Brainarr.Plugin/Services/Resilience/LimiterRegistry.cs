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

        private static int DefaultConcurrencyFor(string provider)
        {
            // Conservative defaults; can be tuned later or surfaced via settings
            switch ((provider ?? string.Empty).ToLowerInvariant())
            {
                case "ollama":
                case "lmstudio":
                    return 128; // local backends – effectively no cap in practice
                case "openai":
                case "anthropic":
                case "openrouter":
                case "groq":
                case "perplexity":
                case "deepseek":
                case "gemini":
                    return 64; // cloud backends – high to avoid unintended throttling in tests
                default:
                    return 64;
            }
        }

        public async Task<IDisposable> AcquireAsync(ModelKey key, CancellationToken cancellationToken)
        {
            var sem = _semaphores.GetOrAdd(key, k => new SemaphoreSlim(DefaultConcurrencyFor(k.Provider)));
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
    }
}
