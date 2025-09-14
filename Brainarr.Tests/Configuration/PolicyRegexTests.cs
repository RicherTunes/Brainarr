using System.Text.RegularExpressions;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class PolicyRegexTests
    {
        [Theory]
        [InlineData("25th Anniversary Edition", true)]
        [InlineData("Remastered 2024", true)]
        [InlineData("AI cover of classic", true)]
        [InlineData("Original pressing", false)]
        public void SuspiciousFuture_IdentifiesKeywords(string input, bool expected)
        {
            Policy.Regexes.SuspiciousFuture.IsMatch(input).Should().Be(expected);
        }

        [Fact]
        public void StripMarkdownFence_RemovesFences()
        {
            var input = "```json\n{ \"a\": 1 }\n```";
            var result = Policy.Regexes.StripMarkdownFence.Replace(input, string.Empty);
            result.Should().NotContain("```").And.Contain("{ \"a\": 1 }");
        }

        [Fact]
        public void NormalizeWhitespace_CompressesSpaces()
        {
            var input = "Hello\t   world\nfrom   Brainarr";
            var result = Policy.Regexes.NormalizeWhitespace.Replace(input, " ");
            result.Should().Be("Hello world from Brainarr");
        }
    }
}
