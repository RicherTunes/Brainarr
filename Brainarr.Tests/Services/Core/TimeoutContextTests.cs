using System;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class TimeoutContextTests
    {
        [Fact]
        public void GetSecondsOrDefault_returns_fallback_when_no_override()
        {
            // ensure clean state
            TimeoutContext.RequestTimeoutSeconds = 0;
            TimeoutContext.GetSecondsOrDefault(30).Should().Be(30);
        }

        [Fact]
        public void Push_sets_and_restores_scope_value()
        {
            TimeoutContext.RequestTimeoutSeconds = 0;
            using (TimeoutContext.Push(45))
            {
                TimeoutContext.RequestTimeoutSeconds.Should().Be(45);
                TimeoutContext.GetSecondsOrDefault(10).Should().Be(45);
            }
            // Restored to previous (0 => fallback applies)
            TimeoutContext.RequestTimeoutSeconds.Should().Be(0);
            TimeoutContext.GetSecondsOrDefault(10).Should().Be(10);
        }

        [Fact]
        public void Push_with_non_positive_seconds_keeps_previous_value()
        {
            TimeoutContext.RequestTimeoutSeconds = 20;
            using (TimeoutContext.Push(0))
            {
                TimeoutContext.RequestTimeoutSeconds.Should().Be(20);
                TimeoutContext.GetSecondsOrDefault(5).Should().Be(20);
            }
            TimeoutContext.RequestTimeoutSeconds.Should().Be(20);
        }
    }
}
