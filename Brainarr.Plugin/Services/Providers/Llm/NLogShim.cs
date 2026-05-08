using System;
using Microsoft.Extensions.Logging;
using NLogLogger = NLog.Logger;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;
using MelEventId = Microsoft.Extensions.Logging.EventId;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// Adapts an NLog <see cref="Logger"/> to <see cref="Microsoft.Extensions.Logging.ILogger"/>
    /// so brainarr can use common's <see cref="Lidarr.Plugin.Common.Observability.LlmLoggerExtensions"/>
    /// without rewriting the host's NLog plumbing.
    ///
    /// <para>
    /// NLog is the canonical logger inside the Lidarr host. Common's structured logging is built
    /// on Microsoft.Extensions.Logging. This shim is the smallest viable bridge; it forwards each
    /// MEL log entry to the equivalent NLog level and preserves the original event-id-bearing
    /// format string in the rendered message.
    /// </para>
    /// </summary>
    internal static class NLogShim
    {
        public static Microsoft.Extensions.Logging.ILogger For(NLogLogger nlog)
            => new NLogMelAdapter(nlog ?? throw new ArgumentNullException(nameof(nlog)));

        private sealed class NLogMelAdapter : Microsoft.Extensions.Logging.ILogger
        {
            private readonly NLogLogger _logger;

            public NLogMelAdapter(NLogLogger logger) => _logger = logger;

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(MelLogLevel logLevel) => logLevel switch
            {
                MelLogLevel.Trace => _logger.IsTraceEnabled,
                MelLogLevel.Debug => _logger.IsDebugEnabled,
                MelLogLevel.Information => _logger.IsInfoEnabled,
                MelLogLevel.Warning => _logger.IsWarnEnabled,
                MelLogLevel.Error => _logger.IsErrorEnabled,
                MelLogLevel.Critical => _logger.IsFatalEnabled,
                _ => true,
            };

            public void Log<TState>(
                MelLogLevel logLevel,
                MelEventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (formatter == null) return;
                var message = formatter(state, exception);
                if (string.IsNullOrEmpty(message) && exception == null) return;

                // Prefix with event-id name so NLog rendering preserves the structured tag.
                var prefixed = eventId.Name != null ? $"[{eventId.Name}] {message}" : message;

                switch (logLevel)
                {
                    case MelLogLevel.Trace:
                        _logger.Trace(exception, prefixed);
                        break;
                    case MelLogLevel.Debug:
                        _logger.Debug(exception, prefixed);
                        break;
                    case MelLogLevel.Information:
                        _logger.Info(exception, prefixed);
                        break;
                    case MelLogLevel.Warning:
                        _logger.Warn(exception, prefixed);
                        break;
                    case MelLogLevel.Error:
                        _logger.Error(exception, prefixed);
                        break;
                    case MelLogLevel.Critical:
                        _logger.Fatal(exception, prefixed);
                        break;
                }
            }

            private sealed class NullScope : IDisposable
            {
                public static NullScope Instance { get; } = new();
                public void Dispose() { }
            }
        }
    }
}
