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
            // Ensure our in-memory target sees all messages first
            config.AddTarget(_target);
            if (config.LoggingRules == null) { config.LoggingRules = new LoggingRuleCollection(); }
            config.LoggingRules.Insert(0, _rule);
            LogManager.Configuration = config;
            LogManager.ReconfigExistingLoggers();
        }

        public IReadOnlyList<string> Messages => new List<string>(_target.Logs).AsReadOnly();

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
