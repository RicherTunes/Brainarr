using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Sockets;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Health
{
    /// <summary>
    /// Short-lived in-process cache that records whether a local AI backend
    /// (Ollama / LM Studio) is known-down due to a connection-class failure.
    ///
    /// When a ModelDetection call fails with a socket-level error (connection
    /// refused, host unreachable, name resolution failure) the cache records
    /// the backend as "down" for <see cref="BrainarrConstants.BackendDownGraceSeconds"/>
    /// seconds. Subsequent callers asking <see cref="IsKnownDown"/> within the
    /// grace window get an immediate false answer — without issuing any HTTP
    /// request or burning a retry budget — reducing the thunderstorm of retries
    /// visible in the logs when the user's local backend is simply not running.
    ///
    /// Thread-safety: all mutations go through <see cref="ConcurrentDictionary{TKey,TValue}"/>;
    /// no additional locking is required.
    /// </summary>
    public sealed class BackendHealthCache
    {
        // Shared singleton for production use (same pattern as warn-once latches).
        // Tests create their own instances via the public constructor to get isolation.
        private static readonly BackendHealthCache _shared = new BackendHealthCache();

        /// <summary>Gets the process-wide singleton instance.</summary>
        public static BackendHealthCache Shared => _shared;

        private readonly ConcurrentDictionary<string, DownEntry> _entries =
            new ConcurrentDictionary<string, DownEntry>(StringComparer.OrdinalIgnoreCase);

        private readonly TimeProvider _timeProvider;

        /// <summary>Creates an instance that uses the real wall clock.</summary>
        public BackendHealthCache() : this(TimeProvider.System) { }

        /// <summary>Creates an instance with an injected <see cref="TimeProvider"/> for testing.</summary>
        public BackendHealthCache(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        // ------------------------------------------------------------------ //
        // Public API
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Records that the backend identified by <paramref name="provider"/> /
        /// <paramref name="baseUrl"/> failed with a connection-class exception.
        /// The backend is considered "down" for
        /// <see cref="BrainarrConstants.BackendDownGraceSeconds"/> seconds from now.
        /// </summary>
        public void MarkDown(string provider, string baseUrl, Exception exception)
        {
            if (!IsConnectionClassFailure(exception))
                return;

            var key = MakeKey(provider, baseUrl);
            var expiresAt = _timeProvider.GetUtcNow().UtcDateTime
                            + TimeSpan.FromSeconds(BrainarrConstants.BackendDownGraceSeconds);
            var reason = BuildReason(provider, baseUrl, exception);
            _entries[key] = new DownEntry(expiresAt, reason);
        }

        /// <summary>
        /// Records that the backend identified by <paramref name="provider"/> /
        /// <paramref name="baseUrl"/> responded successfully. Clears any existing
        /// down-state immediately.
        /// </summary>
        public void MarkUp(string provider, string baseUrl)
        {
            var key = MakeKey(provider, baseUrl);
            _entries.TryRemove(key, out _);
        }

        /// <summary>
        /// Returns <c>true</c> if the backend is known-down and the grace window
        /// has not yet expired, in which case callers should fail fast rather than
        /// retrying. <paramref name="reason"/> is a human-readable explanation
        /// suitable for a Debug log line.
        /// </summary>
        public bool IsKnownDown(string provider, string baseUrl, out string reason)
        {
            var key = MakeKey(provider, baseUrl);
            if (_entries.TryGetValue(key, out var entry))
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                if (now < entry.ExpiresAt)
                {
                    var remaining = (int)(entry.ExpiresAt - now).TotalSeconds;
                    reason = $"{entry.Reason} (known-down for another ~{remaining}s)";
                    return true;
                }

                // Grace period expired — evict stale entry
                _entries.TryRemove(key, out _);
            }

            reason = null;
            return false;
        }

        // ------------------------------------------------------------------ //
        // Internal helpers (accessible from Brainarr.Tests via InternalsVisibleTo)
        // ------------------------------------------------------------------ //

        /// <summary>TEST-ONLY: clears all entries for isolation.</summary>
        internal void ClearAllForTests() => _entries.Clear();

        // ------------------------------------------------------------------ //
        // Private helpers
        // ------------------------------------------------------------------ //

        /// <summary>
        /// Determines whether <paramref name="exception"/> is a connection-class
        /// failure that justifies marking a backend as down.
        ///
        /// Conservative scope — only <see cref="HttpRequestException"/> wrapping a
        /// <see cref="SocketException"/> qualifies. This avoids false-positives for
        /// transient HTTP errors (4xx, 5xx) or timeouts that could stem from a
        /// slow-but-alive backend.
        /// </summary>
        public static bool IsConnectionClassFailure(Exception exception)
        {
            if (exception == null)
                return false;

            // Walk the inner-exception chain to handle wrapping patterns.
            var ex = exception;
            while (ex != null)
            {
                if (ex is SocketException)
                    return true;

                // HttpRequestException wrapping NameResolutionFailure surfaces as
                // a SocketException with SocketErrorCode == HostNotFound on the
                // inner exception — caught above. Belt-and-suspenders: also check
                // the HttpRequestException.HttpRequestError property if available.
                if (ex is HttpRequestException hre)
                {
                    // .NET 5+ exposes HttpRequestError enum on the exception
                    // so we can check without depending on message strings.
#if NET5_0_OR_GREATER
                    if (hre.HttpRequestError == System.Net.Http.HttpRequestError.ConnectionError ||
                        hre.HttpRequestError == System.Net.Http.HttpRequestError.NameResolutionError)
                    {
                        return true;
                    }
#endif
                }

                ex = ex.InnerException;
            }

            return false;
        }

        private static string MakeKey(string provider, string baseUrl)
        {
            var normalizedUrl = (baseUrl ?? string.Empty).TrimEnd('/').ToLowerInvariant();
            var normalizedProvider = (provider ?? string.Empty).ToLowerInvariant();
            return $"{normalizedProvider}|{normalizedUrl}";
        }

        private static string BuildReason(string provider, string baseUrl, Exception exception)
        {
            var innerMsg = exception?.InnerException?.Message ?? exception?.Message ?? "unknown error";
            return $"{provider} backend at {baseUrl} is unreachable ({innerMsg})";
        }

        private readonly struct DownEntry
        {
            public readonly DateTime ExpiresAt;
            public readonly string Reason;

            public DownEntry(DateTime expiresAt, string reason)
            {
                ExpiresAt = expiresAt;
                Reason = reason;
            }
        }
    }
}
