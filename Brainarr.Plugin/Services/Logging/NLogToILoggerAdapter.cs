using System;
using Microsoft.Extensions.Logging;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Logging
{
    /// <summary>
    /// Adapter that wraps NLog Logger to implement Microsoft.Extensions.Logging.ILogger.
    /// Enables Brainarr to use Lidarr.Plugin.Common services that depend on ILogger.
    /// </summary>
    public class NLogToILoggerAdapter : Microsoft.Extensions.Logging.ILogger
    {
        private readonly Logger _nlogLogger;

        public NLogToILoggerAdapter(Logger nlogLogger)
        {
            _nlogLogger = nlogLogger ?? throw new ArgumentNullException(nameof(nlogLogger));
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            // NLog doesn't have a direct equivalent, return a no-op disposable
            return new NoOpDisposable();
        }

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            return _nlogLogger.IsEnabled(MapToNLogLevel(logLevel));
        }

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            var nlogLevel = MapToNLogLevel(logLevel);

            if (exception != null)
            {
                _nlogLogger.Log(nlogLevel, exception, message);
            }
            else
            {
                _nlogLogger.Log(nlogLevel, message);
            }
        }

        private static NLog.LogLevel MapToNLogLevel(Microsoft.Extensions.Logging.LogLevel level)
        {
            return level switch
            {
                Microsoft.Extensions.Logging.LogLevel.Trace => NLog.LogLevel.Trace,
                Microsoft.Extensions.Logging.LogLevel.Debug => NLog.LogLevel.Debug,
                Microsoft.Extensions.Logging.LogLevel.Information => NLog.LogLevel.Info,
                Microsoft.Extensions.Logging.LogLevel.Warning => NLog.LogLevel.Warn,
                Microsoft.Extensions.Logging.LogLevel.Error => NLog.LogLevel.Error,
                Microsoft.Extensions.Logging.LogLevel.Critical => NLog.LogLevel.Fatal,
                Microsoft.Extensions.Logging.LogLevel.None => NLog.LogLevel.Off,
                _ => NLog.LogLevel.Info
            };
        }

        private sealed class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    /// <summary>
    /// Factory for creating NLog to ILogger adapters.
    /// </summary>
    public static class NLogAdapterFactory
    {
        /// <summary>
        /// Creates an ILogger adapter from an NLog Logger.
        /// </summary>
        public static Microsoft.Extensions.Logging.ILogger CreateILogger(Logger nlogLogger)
        {
            return new NLogToILoggerAdapter(nlogLogger);
        }

        /// <summary>
        /// Creates an ILogger adapter for the current class using NLog.
        /// </summary>
        public static Microsoft.Extensions.Logging.ILogger CreateILogger()
        {
            return new NLogToILoggerAdapter(LogManager.GetCurrentClassLogger());
        }
    }
}
