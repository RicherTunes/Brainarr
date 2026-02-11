using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using Xunit;

namespace Brainarr.Tests.Helpers;

public sealed class PassThroughBreakerRegistryTests
{
    [Fact]
    public async Task CreateMock_ReturnsNonNullBreaker_ForConcreteKey()
    {
        var registry = PassThroughBreakerRegistry.CreateMock().Object;

        var breaker = registry.Get(new ModelKey("openai", "gpt-4"), logger: null);
        breaker.Should().NotBeNull();

        var result = await breaker.ExecuteAsync(async () => await Task.FromResult(123));
        result.Should().Be(123);
    }
}
