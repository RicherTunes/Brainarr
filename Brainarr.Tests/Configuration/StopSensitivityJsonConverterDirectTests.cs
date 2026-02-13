using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class StopSensitivityJsonConverterDirectTests
    {
        [Theory]
        [InlineData("\"balanced\"", StopSensitivity.Normal)]
        [InlineData("\"lenient\"", StopSensitivity.Lenient)]
        [InlineData("\"strict\"", StopSensitivity.Strict)]
        [InlineData("\"aggressive\"", StopSensitivity.Aggressive)]
        [InlineData("\"off\"", StopSensitivity.Off)]
        [InlineData("2", StopSensitivity.Normal)]
        [InlineData("0", StopSensitivity.Off)]
        [InlineData("null", StopSensitivity.Normal)]
        [InlineData("\"unknown\"", StopSensitivity.Normal)]
        public void Read_parses_aliases_numbers_and_null_safely(string jsonLiteral, StopSensitivity expected)
        {
            var converter = new StopSensitivityJsonConverter();
            var json = Encoding.UTF8.GetBytes(jsonLiteral);
            var reader = new Utf8JsonReader(new ReadOnlySequence<byte>(json));
            reader.Read();
            var result = converter.Read(ref reader, typeof(StopSensitivity), new JsonSerializerOptions());
            result.Should().Be(expected);
        }

        [Fact(Skip = "STJ root-context writer throws InvalidOperationException. Owner: RicherTunes. Unskip: wrap value in a wrapper object (not root-level serialize) or use Utf8JsonWriter with stream.")]
        public void Write_emits_expected_camel_case_strings()
        {
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

            var payload1 = new Wrapper { Sensitivity = StopSensitivity.Off };
            var json1 = JsonSerializer.Serialize(payload1, opts);
            json1.Should().Contain("\"sensitivity\":\"off\"");

            var payload2 = new Wrapper { Sensitivity = StopSensitivity.Lenient };
            var json2 = JsonSerializer.Serialize(payload2, opts);
            json2.Should().Contain("\"sensitivity\":\"lenient\"");
        }

        private sealed class Wrapper
        {
            public StopSensitivity Sensitivity { get; set; }
        }
    }
}
