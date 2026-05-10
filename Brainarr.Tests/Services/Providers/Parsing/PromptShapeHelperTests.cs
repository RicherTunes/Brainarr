using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;
using Xunit;

namespace Brainarr.Tests.Services.Providers.Parsing
{
    [Trait("Category", "Unit")]
    public class PromptShapeHelperTests
    {
        // ---- IsArtistOnly ----

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void IsArtistOnly_NullOrWhitespace_ReturnsFalse(string? input)
        {
            PromptShapeHelper.IsArtistOnly(input).Should().BeFalse();
        }

        [Theory]
        [InlineData("Please give NEW ARTIST RECOMMENDATIONS based on my library")]
        [InlineData("Focus on Artists similar to my taste")]
        public void IsArtistOnly_HeuristicMatches_ReturnsTrue(string prompt)
        {
            PromptShapeHelper.IsArtistOnly(prompt).Should().BeTrue();
        }

        [Fact]
        public void IsArtistOnly_ReturnExactlyArtist_NoAlbumWord_ReturnsTrue()
        {
            PromptShapeHelper.IsArtistOnly("Return exactly 10 artist names").Should().BeTrue();
        }

        [Fact]
        public void IsArtistOnly_ReturnExactlyArtistAndAlbum_ReturnsFalse()
        {
            PromptShapeHelper.IsArtistOnly("Return exactly 10 artist and album pairs").Should().BeFalse();
        }

        [Fact]
        public void IsArtistOnly_AlbumPrompt_ReturnsFalse()
        {
            PromptShapeHelper.IsArtistOnly("Recommend albums similar to my favorites").Should().BeFalse();
        }

        // ---- ExtractSystemAvoid ----

        [Fact]
        public void ExtractSystemAvoid_NoMarker_ReturnsOriginalPrompt()
        {
            var result = PromptShapeHelper.ExtractSystemAvoid("Hello world");
            result.CleanedPrompt.Should().Be("Hello world");
            result.AvoidNames.Should().BeEmpty();
            result.HasAvoidList.Should().BeFalse();
            result.Count.Should().Be(0);
        }

        [Fact]
        public void ExtractSystemAvoid_NullInput_ReturnsEmptyResult()
        {
            var result = PromptShapeHelper.ExtractSystemAvoid(null);
            result.CleanedPrompt.Should().Be(string.Empty);
            result.AvoidNames.Should().BeEmpty();
        }

        [Fact]
        public void ExtractSystemAvoid_WithMarker_StripsAndParses()
        {
            var result = PromptShapeHelper.ExtractSystemAvoid("[[SYSTEM_AVOID:Foo|Bar|Baz]] do thing");
            result.CleanedPrompt.Should().Be("do thing");
            result.AvoidNames.Should().BeEquivalentTo(new[] { "Foo", "Bar", "Baz" });
            result.HasAvoidList.Should().BeTrue();
            result.Count.Should().Be(3);
        }

        [Fact]
        public void ExtractSystemAvoid_EmptyInner_ReturnsCleanedAndEmptyNames()
        {
            var result = PromptShapeHelper.ExtractSystemAvoid("[[SYSTEM_AVOID:]]rest");
            result.CleanedPrompt.Should().Be("rest");
            result.AvoidNames.Should().BeEmpty();
        }

        [Fact]
        public void ExtractSystemAvoid_FiltersWhitespaceTokens()
        {
            var result = PromptShapeHelper.ExtractSystemAvoid("[[SYSTEM_AVOID:Foo|  |Bar|]] body");
            result.AvoidNames.Should().BeEquivalentTo(new[] { "Foo", "Bar" });
        }

        [Fact]
        public void ExtractSystemAvoid_MarkerPrefixWithoutTerminator_ReturnsOriginal()
        {
            // Starts with prefix but no closing ]] -> endIdx <= 0 path
            var input = "[[SYSTEM_AVOID:Foo no closing";
            var result = PromptShapeHelper.ExtractSystemAvoid(input);
            result.CleanedPrompt.Should().Be(input);
            result.AvoidNames.Should().BeEmpty();
        }

        // ---- BuildAvoidInstruction ----

        [Fact]
        public void BuildAvoidInstruction_Null_ReturnsEmpty()
        {
            PromptShapeHelper.BuildAvoidInstruction(null).Should().BeEmpty();
        }

        [Fact]
        public void BuildAvoidInstruction_Empty_ReturnsEmpty()
        {
            PromptShapeHelper.BuildAvoidInstruction(System.Array.Empty<string>()).Should().BeEmpty();
        }

        [Fact]
        public void BuildAvoidInstruction_WithNames_FormatsCommaSeparated()
        {
            var s = PromptShapeHelper.BuildAvoidInstruction(new[] { "A", "B", "C" });
            s.Should().Contain("A, B, C");
            s.Should().StartWith(" Additionally");
            s.TrimEnd().Should().EndWith(".");
        }
    }
}
