using System;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    public class CorrelationAndUrlSanitizerTests
    {
        [Fact]
        public void CorrelationScope_restores_previous()
        {
            var original = CorrelationContext.Current;
            string innerId;
            using (var scope = CorrelationContext.BeginScope("test-corr"))
            {
                CorrelationContext.Current.Should().Be("test-corr");
                innerId = CorrelationContext.Current;
            }
            innerId.Should().Be("test-corr");
            CorrelationContext.Current.Should().NotBeNull();
        }
    }
}
