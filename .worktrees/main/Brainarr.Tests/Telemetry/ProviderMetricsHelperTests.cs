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
        public void Build_metric_names_are_consistent()
        {
            var name = ProviderMetricsHelper.BuildLatencyMetric("OpenAI", "gpt-4o-mini");
            name.Should().Be("provider.latency.openai.gpt-4o-mini");
        }
    }
}
