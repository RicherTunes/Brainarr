using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared;
using Xunit;

namespace Brainarr.Tests.Services.Providers.Shared
{
    public class CodexSseParserTests
    {
        [Fact]
        [Trait("Category", "Unit")]
        public void Parse_AccumulatesDeltas_AndReadsUsage()
        {
            var sse = string.Join("\n", new[]
            {
                "event: response.created",
                "data: {\"type\":\"response.created\"}",
                "",
                "event: response.output_text.delta",
                "data: {\"type\":\"response.output_text.delta\",\"delta\":\"Hello, \"}",
                "",
                "event: response.output_text.delta",
                "data: {\"type\":\"response.output_text.delta\",\"delta\":\"world\"}",
                "",
                "event: response.output_text.done",
                "data: {\"type\":\"response.output_text.done\",\"text\":\"Hello, world\"}",
                "",
                "event: response.completed",
                "data: {\"type\":\"response.completed\",\"response\":{\"status\":\"completed\",\"usage\":{\"input_tokens\":11,\"output_tokens\":5}}}",
                "",
            });

            var result = CodexSseParser.Parse(sse);

            result.Text.Should().Be("Hello, world");
            result.FinishReason.Should().Be("completed");
            result.InputTokens.Should().Be(11);
            result.OutputTokens.Should().Be(5);
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Parse_NoDeltas_FallsBackToDoneText()
        {
            var sse = string.Join("\n", new[]
            {
                "event: response.output_text.done",
                "data: {\"type\":\"response.output_text.done\",\"text\":\"just the final\"}",
                "",
            });

            CodexSseParser.Parse(sse).Text.Should().Be("just the final");
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void Parse_ToleratesMalformedEventsAndCrlf()
        {
            var sse = "event: response.output_text.delta\r\n"
                      + "data: {not json}\r\n"
                      + "\r\n"
                      + "event: response.output_text.delta\r\n"
                      + "data: {\"type\":\"response.output_text.delta\",\"delta\":\"ok\"}\r\n";

            CodexSseParser.Parse(sse).Text.Should().Be("ok");
        }

        [Theory]
        [Trait("Category", "Unit")]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Parse_EmptyBody_ReturnsEmptyText(string? body)
        {
            CodexSseParser.Parse(body).Text.Should().BeEmpty();
        }
    }
}
