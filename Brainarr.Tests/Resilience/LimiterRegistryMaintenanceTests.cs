using System;
using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Resilience
{
    public class LimiterRegistryMaintenanceTests
    {
        [Fact]
        public async Task Expired_throttle_entries_are_evicted_by_maintenance()
        {
            // Enable adaptive throttling so RegisterThrottle is honored
            var settings = new BrainarrSettings { EnableAdaptiveThrottling = true, AdaptiveThrottleSeconds = 1 };
            LimiterRegistry.ConfigureFromSettings(settings);

            var origin = "openai:unit-test-model";
            LimiterRegistry.RegisterThrottle(origin, TimeSpan.FromMilliseconds(100), 2);

            LimiterRegistry.HasThrottleFor(origin).Should().BeTrue();

            await Task.Delay(250);

            // Force a maintenance sweep and verify eviction
            LimiterRegistry.RunMaintenanceOnce();
            LimiterRegistry.HasThrottleFor(origin).Should().BeFalse();
        }
    }
}
