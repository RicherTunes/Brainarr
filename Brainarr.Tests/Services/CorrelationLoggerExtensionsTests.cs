using NLog;
using Xunit;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace Brainarr.Tests.Services
{
    [Trait("Category", "Unit")]
    public class CorrelationLoggerExtensionsTests
    {
        [Fact]
        public void LoggerExtensions_Invoke_All_Overloads_NoThrow()
        {
            var logger = LogManager.GetCurrentClassLogger();
            var original = CorrelationContext.Current;
            try
            {
                CorrelationContext.Current = "corr-test-123";

                logger.DebugWithCorrelation("dbg message");
                logger.InfoWithCorrelation("info message");
                logger.WarnWithCorrelation("warn message");
                logger.ErrorWithCorrelation("error message");

                logger.DebugWithCorrelation("dbg {0}", 123);
                logger.InfoWithCorrelation("info {0}", 456);
                logger.WarnWithCorrelation("warn {0}", 789);
                logger.ErrorWithCorrelation("err {0}", 111);

                logger.ErrorWithCorrelation(new System.InvalidOperationException("boom"), "with ex");
            }
            finally
            {
                CorrelationContext.Current = original;
            }
        }

        [Fact]
        public void CorrelationScope_Restores_Previous_Id()
        {
            var before = CorrelationContext.Current;
            string during;
            using (var scope = new CorrelationScope("unit-scope"))
            {
                scope.CorrelationId.Should().Be("unit-scope");
                during = CorrelationContext.Current;
            }
            CorrelationContext.Current.Should().Be(before);
            during.Should().Be("unit-scope");
        }
    }
}
