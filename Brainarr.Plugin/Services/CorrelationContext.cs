using System;
using System.Threading;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Manages correlation IDs for request tracking across the call chain.
    /// Provides thread-safe correlation ID generation and context management.
    /// </summary>
    public static class CorrelationContext
    {
        private static readonly AsyncLocal<string?> _id = new();

        /// <summary>
        /// Gets or sets the current correlation ID. When not set, a new ID is generated lazily.
        /// Flows across async/await via AsyncLocal.
        /// </summary>
        public static string Current
        {
            get => _id.Value ??= GenerateCorrelationId();
            set => _id.Value = value;
        }

        /// <summary>
        /// Generates a new correlation ID and sets it as the current context.
        /// </summary>
        public static string StartNew()
        {
            var correlationId = GenerateCorrelationId();
            _id.Value = correlationId;
            return correlationId;
        }

        /// <summary>
        /// Begins a correlation scope that restores the previous ID on dispose.
        /// </summary>
        public static IDisposable BeginScope(string? id = null)
        {
            var previous = _id.Value;
            _id.Value = id ?? GenerateCorrelationId();
            return new Scope(() => _id.Value = previous);
        }

        /// <summary>
        /// Clears the current correlation ID context.
        /// </summary>
        public static void Clear()
        {
            _id.Value = null;
        }

        /// <summary>
        /// Returns the current correlation id without creating one.
        /// </summary>
        public static bool TryPeek(out string? id)
        {
            id = _id.Value;
            return id != null;
        }

        /// <summary>
        /// Returns true if a correlation id is already present in the current async context.
        /// </summary>
        public static bool HasCurrent => _id.Value != null;

        /// <summary>
        /// Generates a new unique correlation ID.
        /// Format: timestamp_randomhex (e.g., 20241219_a3f7b2c1)
        /// </summary>
        public static string GenerateCorrelationId()
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var random = Guid.NewGuid().ToString("N")[..8]; // First 8 characters of GUID
            return $"{timestamp}_{random}";
        }
    }

    /// <summary>
    /// Disposable scope for managing correlation ID context.
    /// Automatically restores the previous correlation ID when disposed.
    /// </summary>
    [Obsolete("Use CorrelationContext.BeginScope(id) instead.")]
    public class CorrelationScope : IDisposable
    {
        private readonly string? _previousCorrelationId;
        private bool _disposed;

        /// <summary>
        /// Creates a new correlation scope with the specified correlation ID.
        /// </summary>
        /// <param name="correlationId">The correlation ID to set for this scope</param>
        public CorrelationScope(string correlationId)
        {
            _previousCorrelationId = CorrelationContext.Current; // capture before override
            CorrelationContext.Current = correlationId;
        }

        /// <summary>
        /// Creates a new correlation scope with a newly generated correlation ID.
        /// </summary>
        public CorrelationScope() : this(CorrelationContext.GenerateCorrelationId())
        {
        }

        /// <summary>
        /// The correlation ID for this scope.
        /// </summary>
        public string CorrelationId => CorrelationContext.Current;

        /// <summary>
        /// Disposes the scope and restores the previous correlation ID.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            CorrelationContext.Current = _previousCorrelationId;
            _disposed = true;
        }
    }

    internal sealed class Scope : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;
        public Scope(Action onDispose) => _onDispose = onDispose;
        public void Dispose()
        {
            if (_disposed) return;
            _onDispose();
            _disposed = true;
        }
    }

    /// <summary>
    /// Extension methods for adding correlation ID support to loggers.
    /// </summary>
    public static class LoggerExtensions
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _warnOnceKeys = new();

        /// <summary>
        /// Logs a warning with EventId, once per unique key, with correlation ID.
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <param name="eventId">Event id to attach (e.g., 12001)</param>
        /// <param name="onceKey">Uniqueness key (e.g., provider name)</param>
        /// <param name="message">Message to log</param>
        public static void WarnOnceWithEvent(this Logger logger, int eventId, string onceKey, string message)
        {
            if (logger == null) return;
            if (string.IsNullOrWhiteSpace(onceKey)) onceKey = "_";
            if (!_warnOnceKeys.TryAdd($"{eventId}:{onceKey}", 1)) return;

            var evt = new LogEventInfo(LogLevel.Warn, logger.Name, $"[{CorrelationContext.Current}] {message}");
            try { evt.Properties["EventId"] = eventId; } catch { }
            logger.Log(evt);
        }
        /// <summary>
        /// Logs a debug message with correlation ID.
        /// </summary>
        public static void DebugWithCorrelation(this Logger logger, string message)
        {
            logger.Debug($"[{CorrelationContext.Current}] {message}");
        }

        /// <summary>
        /// Logs an info message with correlation ID.
        /// </summary>
        public static void InfoWithCorrelation(this Logger logger, string message)
        {
            logger.Info($"[{CorrelationContext.Current}] {message}");
        }

        /// <summary>
        /// Logs a warning message with correlation ID.
        /// </summary>
        public static void WarnWithCorrelation(this Logger logger, string message)
        {
            logger.Warn($"[{CorrelationContext.Current}] {message}");
        }

        /// <summary>
        /// Logs an error message with correlation ID.
        /// </summary>
        public static void ErrorWithCorrelation(this Logger logger, string message)
        {
            logger.Error($"[{CorrelationContext.Current}] {message}");
        }

        /// <summary>
        /// Logs an error message with exception and correlation ID.
        /// </summary>
        public static void ErrorWithCorrelation(this Logger logger, Exception exception, string message)
        {
            logger.Error(exception, $"[{CorrelationContext.Current}] {message}");
        }

        /// <summary>
        /// Logs a debug message with correlation ID and formatted parameters.
        /// </summary>
        public static void DebugWithCorrelation(this Logger logger, string message, params object[] args)
        {
            logger.Debug($"[{CorrelationContext.Current}] {message}", args);
        }

        /// <summary>
        /// Logs an info message with correlation ID and formatted parameters.
        /// </summary>
        public static void InfoWithCorrelation(this Logger logger, string message, params object[] args)
        {
            logger.Info($"[{CorrelationContext.Current}] {message}", args);
        }

        /// <summary>
        /// Logs a warning message with correlation ID and formatted parameters.
        /// </summary>
        public static void WarnWithCorrelation(this Logger logger, string message, params object[] args)
        {
            logger.Warn($"[{CorrelationContext.Current}] {message}", args);
        }

        /// <summary>
        /// Logs an error message with correlation ID and formatted parameters.
        /// </summary>
        public static void ErrorWithCorrelation(this Logger logger, string message, params object[] args)
        {
            logger.Error($"[{CorrelationContext.Current}] {message}", args);
        }

        /// <summary>
        /// Test-only utility: clears the warn-once cache so tests can assert first-run warnings deterministically.
        /// Safe in production; no effect unless invoked explicitly by tests.
        /// </summary>
        public static void ClearWarnOnceKeysForTests()
        {
            _warnOnceKeys.Clear();
        }

        /// <summary>
        /// Test-only utility: checks whether a warn-once event has been emitted for the given key.
        /// </summary>
        public static bool HasWarnedOnceForTests(int eventId, string onceKey)
        {
            return _warnOnceKeys.ContainsKey($"{eventId}:{onceKey}");
        }
    }

    /// <summary>
    /// Utility class for sanitizing URLs in log messages to remove sensitive information.
    /// </summary>
    public static class UrlSanitizer
    {
        /// <summary>
        /// Sanitizes a URL by removing sensitive query parameters and keeping only safe components.
        /// </summary>
        /// <param name="url">The URL to sanitize</param>
        /// <returns>Sanitized URL with sensitive data removed</returns>
        public static string SanitizeUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            try
            {
                var uri = new Uri(url);

                // Return only scheme, host, port, and path - remove query string and fragment
                var sanitized = $"{uri.Scheme}://{uri.Host}";

                if (!uri.IsDefaultPort)
                {
                    sanitized += $":{uri.Port}";
                }

                if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
                {
                    sanitized += uri.AbsolutePath;
                }

                return sanitized;
            }
            catch (UriFormatException)
            {
                // If URL parsing fails, return a generic sanitized version
                return SanitizeUrlFallback(url);
            }
        }

        /// <summary>
        /// Fallback method for sanitizing URLs when URI parsing fails.
        /// Removes common sensitive patterns from URL strings.
        /// </summary>
        private static string SanitizeUrlFallback(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            // Remove query parameters that might contain API keys or tokens
            var questionMarkIndex = url.IndexOf('?');
            if (questionMarkIndex >= 0)
            {
                url = url.Substring(0, questionMarkIndex);
            }

            // Remove fragments
            var hashIndex = url.IndexOf('#');
            if (hashIndex >= 0)
            {
                url = url.Substring(0, hashIndex);
            }

            return url;
        }

        /// <summary>
        /// Sanitizes an API endpoint URL for logging, preserving the endpoint structure
        /// while removing sensitive authentication parameters.
        /// </summary>
        /// <param name="url">The API URL to sanitize</param>
        /// <returns>Sanitized URL safe for logging</returns>
        public static string SanitizeApiUrl(string url)
        {
            var sanitized = SanitizeUrl(url);

            // Additionally mask common API key patterns in path
            if (sanitized.Contains("/api/"))
            {
                // Keep the API structure but remove potential key components
                return sanitized.Replace("api_key=", "api_key=***")
                              .Replace("token=", "token=***")
                              .Replace("key=", "key=***");
            }

            return sanitized;
        }
    }
}
