using System;
using System.Collections.Generic;
using System.Text.Json;
using Xunit;
using Brainarr.Plugin.Services.Security;
using Brainarr.Plugin.Models;

namespace Brainarr.Tests.Security
{
    /// <summary>
    /// Security tests for JSON deserialization to prevent attacks
    /// </summary>
    public class JsonDeserializationSecurityTests
    {
        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Reject_Prototype_Pollution_Attack()
        {
            // Arrange
            var maliciousJson = @"{
                ""__proto__"": {
                    ""isAdmin"": true
                },
                ""artist"": ""Test Artist"",
                ""album"": ""Test Album""
            }";

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(maliciousJson));

            Assert.Contains("malicious", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Reject_Type_Injection_Attack()
        {
            // Arrange
            var maliciousJson = @"{
                ""$type"": ""System.IO.FileInfo, System.IO"",
                ""fileName"": ""/etc/passwd"",
                ""artist"": ""Test Artist""
            }";

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(maliciousJson));

            Assert.Contains("malicious", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Reject_Function_Constructor_Attack()
        {
            // Arrange
            var maliciousJson = @"{
                ""artist"": ""Test"",
                ""album"": ""Function(alert('XSS'))""
            }";

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(maliciousJson));

            Assert.Contains("malicious", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Reject_Eval_Injection()
        {
            // Arrange
            var maliciousJson = @"{
                ""artist"": ""eval('malicious code')"",
                ""album"": ""Test Album""
            }";

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(maliciousJson));

            Assert.Contains("malicious", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Reject_Script_Injection()
        {
            // Arrange
            var maliciousJson = @"{
                ""artist"": ""<script>alert('XSS')</script>"",
                ""album"": ""Test Album""
            }";

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(maliciousJson));

            Assert.Contains("malicious", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Reject_Javascript_Protocol()
        {
            // Arrange
            var maliciousJson = @"{
                ""artist"": ""javascript:void(0)"",
                ""album"": ""Test Album""
            }";

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(maliciousJson));

            Assert.Contains("malicious", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Reject_Excessive_Nesting()
        {
            // Arrange - Create deeply nested JSON
            var depth = 25;
            var json = string.Empty;
            for (int i = 0; i < depth; i++)
            {
                json += @"{""nested"":";
            }
            json += "null";
            for (int i = 0; i < depth; i++)
            {
                json += "}";
            }

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.ParseDocument(json));

            Assert.Contains("nesting depth", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Reject_Oversized_JSON()
        {
            // Arrange - Create JSON larger than 10MB
            var largeJson = @"{""data"":""" + new string('A', 11 * 1024 * 1024) + @"""}";

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.Deserialize<Dictionary<string, string>>(largeJson));

            Assert.Contains("exceeds maximum", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Reject_Reference_Loop_Attack()
        {
            // Arrange
            var maliciousJson = @"{
                ""$id"": ""1"",
                ""artist"": ""Test"",
                ""$ref"": ""1""
            }";

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(maliciousJson));

            Assert.Contains("malicious", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Accept_Valid_JSON()
        {
            // Arrange
            var validJson = @"{
                ""artist"": ""Pink Floyd"",
                ""album"": ""The Dark Side of the Moon"",
                ""genre"": ""Progressive Rock"",
                ""year"": 1973,
                ""confidence"": 0.95,
                ""reason"": ""Classic progressive rock masterpiece""
            }";

            // Act
            var result = SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(validJson);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Pink Floyd", result.Artist);
            Assert.Equal("The Dark Side of the Moon", result.Album);
            Assert.Equal("Progressive Rock", result.Genre);
            Assert.Equal(1973, result.Year);
            Assert.Equal(0.95, result.Confidence);
            Assert.True(result.IsValid());
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Handle_Null_Values()
        {
            // Arrange
            var jsonWithNulls = @"{
                ""artist"": ""Test Artist"",
                ""album"": ""Test Album"",
                ""genre"": null,
                ""year"": null,
                ""confidence"": null
            }";

            // Act
            var result = SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(jsonWithNulls);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Test Artist", result.Artist);
            Assert.Equal("Test Album", result.Album);
            Assert.Null(result.Genre);
            Assert.Null(result.Year);
            Assert.Null(result.Confidence);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Use_Strict_Mode_When_Requested()
        {
            // Arrange
            var json = @"{
                ""Artist"": ""Test"",  // Wrong case in strict mode
                ""album"": ""Test Album""
            }";

            // Act - Strict mode should fail
            var strictResult = SecureJsonSerializer.TryDeserialize<ProviderResponses.RecommendationItem>(
                json, out var strictObj, out var strictError);

            // Non-strict mode should succeed (case insensitive)
            var result = SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(json, strict: false);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Test", result.Artist);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Limit_Array_Size()
        {
            // Arrange
            var maliciousJson = @"{
                ""recommendations"": [" + new string('1', 10000000) + @"]
            }";

            // Act & Assert - Should reject suspiciously large arrays
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.ParseDocument(maliciousJson));
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Reject_Event_Handler_Injection()
        {
            // Arrange
            var maliciousJson = @"{
                ""artist"": ""Test"",
                ""album"": ""onclick='alert(1)'""
            }";

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(maliciousJson));

            Assert.Contains("malicious", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void SecureJsonSerializer_Should_Reject_Data_URI_Injection()
        {
            // Arrange
            var maliciousJson = @"{
                ""artist"": ""data:text/html,<script>alert(1)</script>"",
                ""album"": ""Test""
            }";

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(maliciousJson));

            Assert.Contains("malicious", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [Trait("Area", "Security")]
        [InlineData("__defineGetter__")]
        [InlineData("__defineSetter__")]
        [InlineData("__lookupGetter__")]
        [InlineData("__lookupSetter__")]
        [InlineData("constructor")]
        [InlineData("setTimeout(")]
        [InlineData("setInterval(")]
        [InlineData("vbscript:")]
        [InlineData("onerror")]
        [InlineData("onload")]
        public void SecureJsonSerializer_Should_Reject_Known_Attack_Patterns(string attackPattern)
        {
            // Arrange
            var maliciousJson = $@"{{
                ""artist"": ""{attackPattern}"",
                ""album"": ""Test Album""
            }}";

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() =>
                SecureJsonSerializer.Deserialize<ProviderResponses.RecommendationItem>(maliciousJson));

            Assert.Contains("malicious", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        [Trait("Area", "Security")]
        public void ParseDocument_Should_Provide_Safe_Inspection()
        {
            // Arrange
            var json = @"{
                ""artist"": ""Test Artist"",
                ""album"": ""Test Album"",
                ""tracks"": [
                    { ""name"": ""Track 1"", ""duration"": 180 },
                    { ""name"": ""Track 2"", ""duration"": 240 }
                ]
            }";

            // Act
            using var document = SecureJsonSerializer.ParseDocument(json);
            var root = document.RootElement;

            // Assert
            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.True(root.TryGetProperty("artist", out var artist));
            Assert.Equal("Test Artist", artist.GetString());
            Assert.True(root.TryGetProperty("tracks", out var tracks));
            Assert.Equal(JsonValueKind.Array, tracks.ValueKind);
            Assert.Equal(2, tracks.GetArrayLength());
        }

        [Fact]
        [Trait("Area", "Security")]
        public void CreateOptions_Should_Cap_MaxDepth_For_Safety()
        {
            // Arrange & Act
            var options = SecureJsonSerializer.CreateOptions(maxDepth: 100); // Try to set excessive depth

            // Assert
            Assert.True(options.MaxDepth <= 20); // Should be capped at 20
        }
    }
}
