using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Targets;
using NzbDrone.Core.ImportLists.Brainarr.Services.Logging;
using Xunit;

namespace Brainarr.Tests.Services.Logging
{
    [Trait("Category", "Unit")]
    public class NLogToILoggerAdapterTests : IDisposable
    {
        private readonly MemoryTarget _memTarget;
        private readonly Logger _nlogLogger;
        private readonly NLogToILoggerAdapter _adapter;

        public NLogToILoggerAdapterTests()
        {
            // Build an isolated NLog config that captures to an in-memory list.
            _memTarget = new MemoryTarget("mem") { Layout = "${level}|${message}|${exception}" };
            var config = new LoggingConfiguration();
            config.AddRuleForAllLevels(_memTarget);
            var factory = new LogFactory(config);
            _nlogLogger = factory.GetLogger("TestLogger");
            _adapter = new NLogToILoggerAdapter(_nlogLogger);
        }

        public void Dispose()
        {
            _nlogLogger.Factory.Flush();
        }

        [Fact]
        public void Constructor_NullLogger_Throws()
        {
            Action act = () => _ = new NLogToILoggerAdapter(null);
            act.Should().Throw<ArgumentNullException>().WithParameterName("nlogLogger");
        }

        [Theory]
        [InlineData(Microsoft.Extensions.Logging.LogLevel.Trace, "Trace")]
        [InlineData(Microsoft.Extensions.Logging.LogLevel.Debug, "Debug")]
        [InlineData(Microsoft.Extensions.Logging.LogLevel.Information, "Info")]
        [InlineData(Microsoft.Extensions.Logging.LogLevel.Warning, "Warn")]
        [InlineData(Microsoft.Extensions.Logging.LogLevel.Error, "Error")]
        [InlineData(Microsoft.Extensions.Logging.LogLevel.Critical, "Fatal")]
        public void Log_MapsLevelCorrectly(Microsoft.Extensions.Logging.LogLevel msLevel, string nlogLevelName)
        {
            _adapter.Log(msLevel, new EventId(0), "hello", null, (s, _) => s.ToString());

            _memTarget.Logs.Should().ContainMatch($"{nlogLevelName}|hello|*");
        }

        [Fact]
        public void Log_WithException_IncludesExceptionText()
        {
            var ex = new InvalidOperationException("boom");
            _adapter.Log(Microsoft.Extensions.Logging.LogLevel.Error, new EventId(1), "msg", ex, (s, e) => s.ToString());

            _memTarget.Logs.Should().ContainMatch("Error|msg|*boom*");
        }

        [Fact]
        public void IsEnabled_ReflectsNLogLevel()
        {
            // NLog is configured for all levels in this test; all should be enabled.
            _adapter.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Trace).Should().BeTrue();
            _adapter.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Debug).Should().BeTrue();
            _adapter.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Information).Should().BeTrue();
            _adapter.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Warning).Should().BeTrue();
            _adapter.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Error).Should().BeTrue();
            _adapter.IsEnabled(Microsoft.Extensions.Logging.LogLevel.Critical).Should().BeTrue();
        }

        [Fact]
        public void IsEnabled_None_ReturnsFalse()
        {
            _adapter.IsEnabled(Microsoft.Extensions.Logging.LogLevel.None).Should().BeFalse();
        }

        [Fact]
        public void BeginScope_ReturnsNonNull_AndDisposable()
        {
            var scope = _adapter.BeginScope("context");
            scope.Should().NotBeNull();
            // Dispose should not throw.
            var act = () => scope.Dispose();
            act.Should().NotThrow();
        }

        [Fact]
        public void Log_Disabled_DoesNotWrite()
        {
            // Reconfigure NLog to Off for this test (create a fresh factory with Off level).
            var config = new LoggingConfiguration();
            var target = new MemoryTarget("mem2") { Layout = "${message}" };
            config.AddRule(NLog.LogLevel.Off, NLog.LogLevel.Off, target, "TestOff");
            var factory = new LogFactory(config);
            var offLogger = factory.GetLogger("TestOff");
            var adapter = new NLogToILoggerAdapter(offLogger);

            adapter.Log(Microsoft.Extensions.Logging.LogLevel.Information, new EventId(0), "should not appear", null, (s, _) => s.ToString());

            target.Logs.Should().BeEmpty();
        }
    }
}
