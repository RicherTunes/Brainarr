using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using Xunit;

namespace Brainarr.Tests.Telemetry
{
    public class ProviderMetricsHelperTests
    {
        [Fact]
        public void SanitizeName_removes_problem_chars()
        {
            ProviderMetricsHelper.SanitizeName("OpenAI:GPT-4o mini/2024-08").Should().Be("openai-gpt-4o-mini-2024-08");
        }

        [Fact]
        public void Build_metric_tags_and_names_are_consistent()
        {
            var tags = ProviderMetricsHelper.BuildTags("OpenAI", "gpt-4o-mini");
            tags["provider"].Should().Be("openai");
            tags["model"].Should().Be("gpt-4o-mini");
            ProviderMetricsHelper.ProviderLatencyMs.Should().Be("provider.latency");
        }
    }
}
