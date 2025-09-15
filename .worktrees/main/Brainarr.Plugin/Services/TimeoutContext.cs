using System;
using System.Threading;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    // Async-scoped timeout context for provider requests
    internal static class TimeoutContext
    {
        private static readonly AsyncLocal<int> _requestTimeoutSeconds = new AsyncLocal<int>();

        public static int RequestTimeoutSeconds
        {
            get => _requestTimeoutSeconds.Value;
            set => _requestTimeoutSeconds.Value = value;
        }

        public static IDisposable Push(int seconds)
        {
            var prev = RequestTimeoutSeconds;
            RequestTimeoutSeconds = seconds > 0 ? seconds : prev;
            return new Scope(() => RequestTimeoutSeconds = prev);
        }

        public static int GetSecondsOrDefault(int fallback)
        {
            return RequestTimeoutSeconds > 0 ? RequestTimeoutSeconds : fallback;
        }

        private sealed class Scope : IDisposable
        {
            private readonly Action _onDispose;
            private bool _disposed;
            public Scope(Action onDispose) { _onDispose = onDispose; }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _onDispose?.Invoke();
            }
        }
    }
}
