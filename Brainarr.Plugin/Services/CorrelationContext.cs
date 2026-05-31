using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using NLog;
using CommonLoggerExtensions = Lidarr.Plugin.Common.Observability.LoggerExtensions;
using MELNullLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Manages correlation IDs for request tracking across the call chain.
    /// Provides thread-safe correlation ID generation and context management.
    /// Async-local correlation flow is brainarr-specific; the supporting NLog logger
    /// extensions below route through common's event-ID surface where applicable.
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
    /// NLog extension methods that emit messages with the current correlation ID.
    /// Brainarr targets NLog (Lidarr's host logger), so it cannot use common's
    /// <c>Lidarr.Plugin.Common.Observability.LlmLoggerExtensions</c> directly (those target
    /// <c>Microsoft.Extensions.Logging.ILogger</c>). Where event semantics align, use common's
    /// <c>LlmEventIds</c> via the overloads that accept <see cref="EventId"/>.
    /// <para>
    /// Phase 5f: the warn-once tracking is now delegated to
    /// <c>Lidarr.Plugin.Common.Observability.LoggerExtensions.LogWarningOnce</c>. Brainarr keeps
    /// the NLog-flavoured <see cref="WarnOnceWithEvent(Logger, EventId, string, string)"/>
    /// extension (it adds the brainarr correlation id to the rendered message), but the
    /// process-wide seen-key set lives in common — so calls from common-side adapters and
    /// brainarr-side providers cannot duplicate the same warning.
    /// </para>
    /// </summary>
    public static class LoggerExtensions
    {
        /// <summary>
        /// Logs a warning with EventId, once per unique key, with correlation ID.
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <param name="eventId">Event id to attach (e.g., <see cref="BrainarrEventIds.ResilienceFallback"/>)</param>
        /// <param name="onceKey">Uniqueness key (e.g., provider name)</param>
        /// <param name="message">Message to log</param>
        public static void WarnOnceWithEvent(this Logger logger, EventId eventId, string onceKey, string message)
        {
            if (logger == null) return;
            if (string.IsNullOrWhiteSpace(onceKey)) onceKey = "_";

            // Use common's process-wide once-tracking via NullLogger so the seen-key set is
            // unified with any common-side LogWarningOnce calls. Common's LogWarningOnce
            // returns true on first emission (key newly added) and false on duplicates.
            var key = BuildOnceKey(eventId, onceKey);
            if (!CommonLoggerExtensions.LogWarningOnce(MELNullLogger.Instance, key, eventId, string.Empty)) return;

            var evt = new LogEventInfo(NLog.LogLevel.Warn, logger.Name, $"[{CorrelationContext.Current}] {message}");
            try { evt.Properties["EventId"] = eventId.Id; } catch (Exception) { /* Non-critical */ }
            try { if (!string.IsNullOrEmpty(eventId.Name)) evt.Properties["EventName"] = eventId.Name; } catch (Exception) { /* Non-critical */ }
            logger.Log(evt);
        }

        /// <summary>
        /// Compatibility overload: accepts a raw int event id. Prefer the
        /// <see cref="EventId"/> overload with named ids from <see cref="BrainarrEventIds"/> or
        /// <c>Lidarr.Plugin.Common.Observability.LlmEventIds</c>.
        /// </summary>
        public static void WarnOnceWithEvent(this Logger logger, int eventId, string onceKey, string message)
            => WarnOnceWithEvent(logger, new EventId(eventId), onceKey, message);

        private static string BuildOnceKey(EventId eventId, string onceKey)
            => $"brainarr.warn-once:{eventId.Id}:{onceKey}";

        /// <summary>
        /// Logs a debug message with correlation ID.
        /// </summary>
        public static void DebugWithCorrelation(this Logger logger, string message)
        {
            if (!logger.IsDebugEnabled) return;
            logger.Debug($"[{CorrelationContext.Current}] {message}");
        }

        /// <summary>
        /// Logs an info message with correlation ID.
        /// </summary>
        public static void InfoWithCorrelation(this Logger logger, string message)
        {
            if (!logger.IsInfoEnabled) return;
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
        /// <remarks>
        /// Phase 5f: delegates to common's <see cref="CommonLoggerExtensions.ResetOnceKeys"/>. Both
        /// brainarr-side and common-side once-loggers now share a single seen-key set, so a single
        /// reset call clears both.
        /// </remarks>
        public static void ClearWarnOnceKeysForTests()
        {
            CommonLoggerExtensions.ResetOnceKeys();
        }

        /// <summary>
        /// Test-only utility: checks whether a warn-once event has been emitted for the given key.
        /// </summary>
        /// <remarks>
        /// Phase 5f: introspects common's seen-set indirectly. We attempt to acquire the key via
        /// <see cref="CommonLoggerExtensions.LogWarningOnce(ILogger, string, EventId, string, object?[])"/>
        /// against <see cref="MELNullLogger.Instance"/>; a return of <see langword="false"/> means the key
        /// was already in the set (i.e., already warned). When the key was unseen, this method also
        /// removes the side-effect by clearing the once-set is undesirable, so we keep the registration
        /// (the warning was inert because <see cref="MELNullLogger"/> swallows it).
        /// </remarks>
        public static bool HasWarnedOnceForTests(int eventId, string onceKey)
        {
            var key = BuildOnceKey(new EventId(eventId), onceKey);
            // First invocation with this key returns true (newly added). If it returns false,
            // the key was already seen — i.e., a previous warn-once already registered it.
            return !CommonLoggerExtensions.LogWarningOnce(MELNullLogger.Instance, key, default, string.Empty);
        }
    }

}
