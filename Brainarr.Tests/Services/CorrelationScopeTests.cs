using System;
using Brainarr.Tests.Helpers;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class CorrelationScopeTests
    {
        private readonly Logger _logger = TestLogger.CreateNullLogger();

        [Fact]
        [Trait("Area", "Logging")]
        public void CorrelationScope_Restores_Previous_Id_On_Dispose()
        {
            var original = CorrelationContext.StartNew();
            string innerId;

            using (var scope = new CorrelationScope())
            {
                innerId = scope.CorrelationId;
                Assert.False(string.IsNullOrWhiteSpace(innerId));
                Assert.NotEqual(original, innerId);
            }

            Assert.Equal(original, CorrelationContext.Current);
        }
    }
}
