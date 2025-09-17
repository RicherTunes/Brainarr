using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Tokens;
using Xunit;

namespace Brainarr.Tests.Services.Tokens
{
    public class TokenBudgeterTests
    {
        [Fact]
        public async Task Should_build_plan_with_sampling_constraints()
        {
            var provider = new ModelRegistry.ProviderDescriptor
            {
                Name = "Gemini",
                Slug = "gemini",
                Models =
                {
                    new ModelRegistry.ModelDescriptor
                    {
                        Id = "gemini-1.5-flash",
                        ContextTokens = 4096,
                        Capabilities = new ModelRegistry.CapabilitiesDescriptor { JsonMode = true, Stream = true, Tools = false }
                    }
                }
            };

            var model = provider.Models[0];
            var sampling = BuildSamplingPreview(250);
            var budgeter = new TokenBudgeter(new ApproximateTokenCounter());

            var plan = await budgeter.BuildAsync(
                provider,
                model,
                "system prompt",
                "{ \"type\": \"object\" }",
                sampling,
                targetRecommendations: 50,
                cancellationToken: CancellationToken.None);

            plan.AllowedInputTokens.Should().BeGreaterThan(0);
            (plan.AllowedInputTokens + plan.ReservedOutputTokens).Should().BeLessOrEqualTo(model.ContextTokens);
            plan.SamplingItems.Should().BeGreaterThan(0);
            plan.SamplingItems.Should().BeLessOrEqualTo(250);
            plan.BatchSize.Should().BeGreaterThan(0);
            plan.BatchSize.Should().BeLessOrEqualTo(50);
            plan.Batches.Should().BeGreaterThan(0);
            (plan.BatchSize * plan.Batches).Should().BeGreaterOrEqualTo(50);
            plan.Rationale.Should().NotBeNullOrWhiteSpace();
        }

        [Fact]
        public async Task Should_handle_empty_sampling_and_zero_target()
        {
            var provider = new ModelRegistry.ProviderDescriptor
            {
                Name = "Local",
                Slug = "local",
                Models =
                {
                    new ModelRegistry.ModelDescriptor
                    {
                        Id = "qwen",
                        ContextTokens = 2048,
                        Capabilities = new ModelRegistry.CapabilitiesDescriptor { JsonMode = false, Stream = true, Tools = false }
                    }
                }
            };

            var model = provider.Models[0];
            var budgeter = new TokenBudgeter(new ApproximateTokenCounter());

            var plan = await budgeter.BuildAsync(
                provider,
                model,
                systemPrompt: null,
                toolJsonSchema: null,
                samplingPreview: string.Empty,
                targetRecommendations: 0,
                cancellationToken: CancellationToken.None);

            plan.AllowedInputTokens.Should().BeGreaterThan(0);
            plan.SamplingItems.Should().Be(0);
            plan.BatchSize.Should().Be(1);
            plan.Batches.Should().Be(1);
        }

        private static string BuildSamplingPreview(int count)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                if (builder.Length > 0)
                {
                    builder.Append("\n\n");
                }

                builder.Append("Artist ").Append(i).Append('\n').Append("Top albums: Example");
            }

            return builder.ToString();
        }
    }
}
