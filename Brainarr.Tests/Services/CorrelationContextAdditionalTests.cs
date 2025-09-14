using System;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    [Trait("Category", "Unit")]
    public class CorrelationContextAdditionalTests
    {
        [Fact]
        public void GenerateCorrelationId_Format_And_Clear_Behavior()
        {
            var before = CorrelationContext.Current;
            var id = CorrelationContext.GenerateCorrelationId();
            id.Should().MatchRegex(@"^\d{14}_[a-f0-9]{8}$");

            CorrelationContext.Current = id;
            CorrelationContext.Current.Should().Be(id);

            CorrelationContext.Clear();
            // After clear, reading Current should regenerate a new id
            var regenerated = CorrelationContext.Current;
            regenerated.Should().NotBeNullOrEmpty();
            regenerated.Should().NotBe(id);

            // restore
            CorrelationContext.Current = before;
        }
    }
}
