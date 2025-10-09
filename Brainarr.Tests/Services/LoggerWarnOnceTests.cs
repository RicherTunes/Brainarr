using System;
using System.Linq;
using Brainarr.Tests.Helpers;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    [Collection("LoggingTests")] // share logger config with LoggerExtensionsTests
    public class LoggerWarnOnceTests
    {
        private readonly Logger _logger;

        public LoggerWarnOnceTests()
        {
            _logger = TestLogger.Create("LoggerWarnOnceTests");
            TestLogger.ClearLoggedMessages();
        }

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Logging")]
        public void WarnOnceWithEvent_Logs_OnlyOnce_PerKey()
        {
            var correlation = CorrelationContext.StartNew();

            _logger.WarnOnceWithEvent(12001, "provider:openai", "IHttpResilience not injected; using static fallback");
            _logger.WarnOnceWithEvent(12001, "provider:openai", "IHttpResilience not injected; using static fallback");
            _logger.WarnOnceWithEvent(12001, "provider:openai", "IHttpResilience not injected; using static fallback");

            var logs = TestLogger.GetLoggedMessages();
            Assert.True(logs.Count(l => l.Contains("IHttpResilience not injected")) == 1, "expected only one warning to be logged");
            Assert.Contains(logs, l => l.Contains(correlation));
        }
    }
}
