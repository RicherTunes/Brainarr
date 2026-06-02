using System;
using System.Threading;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    // Async-scoped per-request LLM budget (timeout + output-token cap) for provider requests.
    internal static class TimeoutContext
    {
        private static readonly AsyncLocal<int> _requestTimeoutSeconds = new AsyncLocal<int>();
        private static readonly AsyncLocal<int> _maxOutputTokens = new AsyncLocal<int>();

        public static int RequestTimeoutSeconds
        {
            get => _requestTimeoutSeconds.Value;
            set => _requestTimeoutSeconds.Value = value;
        }

        /// <summary>
        /// Output-token (completion) budget for the current request scope. 0 = unset (caller falls
        /// back to its own default). Pushed alongside the timeout so a slow model granted more time
        /// also gets a larger max_tokens and can finish the full list in one request.
        /// </summary>
        public static int MaxOutputTokens
        {
            get => _maxOutputTokens.Value;
            set => _maxOutputTokens.Value = value;
        }

        public static IDisposable Push(int seconds)
        {
            var prev = RequestTimeoutSeconds;
            RequestTimeoutSeconds = seconds > 0 ? seconds : prev;
            return new Scope(() => RequestTimeoutSeconds = prev);
        }

        /// <summary>Push both the per-request timeout and the output-token budget together.</summary>
        public static IDisposable Push(int seconds, int maxOutputTokens)
        {
            var prevSeconds = RequestTimeoutSeconds;
            var prevTokens = MaxOutputTokens;
            RequestTimeoutSeconds = seconds > 0 ? seconds : prevSeconds;
            MaxOutputTokens = maxOutputTokens > 0 ? maxOutputTokens : prevTokens;
            return new Scope(() =>
            {
                RequestTimeoutSeconds = prevSeconds;
                MaxOutputTokens = prevTokens;
            });
        }

        public static int GetSecondsOrDefault(int fallback)
        {
            return RequestTimeoutSeconds > 0 ? RequestTimeoutSeconds : fallback;
        }

        public static int GetMaxOutputTokensOrDefault(int fallback)
        {
            return MaxOutputTokens > 0 ? MaxOutputTokens : fallback;
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
