using System;
using System.Collections.Generic;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Brainarr.TestKit.Providers.Logging
{
    public sealed class TestLoggerSink : IDisposable
    {
        private readonly MemoryTarget _target;
        private readonly LoggingRule _rule;

        public TestLoggerSink(string loggerNamePattern = "*")
        {
            _target = new MemoryTarget("TestLoggerSinkTarget")
            {
                Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}|${event-properties:EventId}" // capture EventId if present
            };

            _rule = new LoggingRule(loggerNamePattern, NLog.LogLevel.Trace, _target);

            var config = LogManager.Configuration ?? new LoggingConfiguration();
            config.AddTarget(_target);
            config.LoggingRules.Add(_rule);
            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers();
        }

        public IReadOnlyList<string> Messages => _target.Logs;

        public int CountWarningsContaining(string contains)
        {
            int count = 0;
            foreach (var line in _target.Logs)
            {
                if (line.Contains("|WARN|", StringComparison.OrdinalIgnoreCase) && line.Contains(contains, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
            return count;
        }

        public int CountEventId(int eventId)
        {
            int count = 0;
            foreach (var line in _target.Logs)
            {
                if (line.EndsWith("|" + eventId))
                {
                    count++;
                }
            }
            return count;
        }

        public void Dispose()
        {
            var config = LogManager.Configuration;
            if (config != null)
            {
                config.LoggingRules.Remove(_rule);
                config.RemoveTarget(_target.Name);
                LogManager.Configuration = config;
                LogManager.ReconfigExistingLoggers();
            }
            _target.Dispose();
        }
    }
}
