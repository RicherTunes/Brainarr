using System;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class CorrelationAsyncFlowTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        [Fact]
        [Trait("Category", "Unit")]
        [Trait("Category", "Logging")]
        public async Task Correlation_Id_Flows_Across_Awaits()
        {
            var original = CorrelationContext.StartNew();

            async Task<string> InnerAsync()
            {
                await Task.Delay(10);
                return CorrelationContext.Current;
            }

            var seen = await InnerAsync();
            Assert.Equal(original, seen);
        }
    }
}

