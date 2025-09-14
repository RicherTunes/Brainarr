using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    public class StopSensitivityJsonConverterTests
    {
        private static readonly JsonSerializerOptions CamelCase = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly JsonSerializerOptions LidarrLike = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        static StopSensitivityJsonConverterTests()
        {
            LidarrLike.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, true));
        }

        [Theory]
        [InlineData("\"balanced\"", StopSensitivity.Normal)]
        [InlineData("\"lenient\"", StopSensitivity.Lenient)]
        [InlineData("\"strict\"", StopSensitivity.Strict)]
        [InlineData("\"aggressive\"", StopSensitivity.Aggressive)]
        [InlineData("2", StopSensitivity.Normal)]
        [InlineData("0", StopSensitivity.Off)]
        public void should_deserialize_stop_sensitivity_aliases(string jsonValue, StopSensitivity expected)
        {
            var json = "{\"topUpStopSensitivity\": " + jsonValue + "}";
            var settings = JsonSerializer.Deserialize<BrainarrSettings>(json, CamelCase);
            settings.Should().NotBeNull();
            settings!.TopUpStopSensitivity.Should().Be(expected);
        }

        [Theory]
        [InlineData("\"balanced\"", StopSensitivity.Normal)]
        [InlineData("\"lenient\"", StopSensitivity.Lenient)]
        [InlineData("\"strict\"", StopSensitivity.Strict)]
        [InlineData("\"aggressive\"", StopSensitivity.Aggressive)]
        public void should_also_work_with_lidarr_string_enum_converter(string jsonValue, StopSensitivity expected)
        {
            var json = "{\"topUpStopSensitivity\": " + jsonValue + "}";
            var settings = JsonSerializer.Deserialize<BrainarrSettings>(json, LidarrLike);
            settings.Should().NotBeNull();
            settings!.TopUpStopSensitivity.Should().Be(expected);
        }
    }
}
