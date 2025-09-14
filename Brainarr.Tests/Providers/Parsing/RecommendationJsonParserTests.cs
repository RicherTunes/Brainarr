using System.Collections.Generic;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Parsing;
using Xunit;

namespace Brainarr.Tests.Providers.Parsing
{
    public class RecommendationJsonParserTests
    {
        private static readonly Logger L = LogManager.GetCurrentClassLogger();

        [Fact]
        public void Parse_array_root_returns_items()
        {
            var json = "[ { \"artist\": \"Artist A\", \"album\": \"Album A\", \"confidence\": 0.9 }, { \"a\": \"Artist B\" } ]";
            var list = RecommendationJsonParser.Parse(json, L);
            list.Should().HaveCount(2);
            list[0].Artist.Should().Be("Artist A");
            list[1].Artist.Should().Be("Artist B");
        }

        [Fact]
        public void Parse_object_with_recommendations_property()
        {
            var json = "{ \"recommendations\": [ { \"artist\": \"X\" }, { \"a\": \"Y\" } ] }";
            var list = RecommendationJsonParser.Parse(json, L);
            list.Should().HaveCount(2);
            list[0].Artist.Should().Be("X");
            list[1].Artist.Should().Be("Y");
        }

        [Fact]
        public void Parse_single_object_maps_item()
        {
            var json = "{ \"artist\": \"Solo\", \"album\": \"One\" }";
            var list = RecommendationJsonParser.Parse(json, L);
            list.Should().HaveCount(1);
            list[0].Artist.Should().Be("Solo");
            list[0].Album.Should().Be("One");
        }

        [Fact]
        public void Parse_fallback_array_extraction_when_wrapped_text()
        {
            var json = "Some header text ```json [ { \"artist\": \"AA\" } ] ``` footer";
            var list = RecommendationJsonParser.Parse(json, L);
            list.Should().HaveCount(1);
            list[0].Artist.Should().Be("AA");
        }
    }
}
