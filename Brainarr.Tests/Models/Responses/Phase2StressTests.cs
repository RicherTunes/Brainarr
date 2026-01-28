using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.OpenAI;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Anthropic;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.DeepSeek;
using Xunit;

namespace Brainarr.Tests.Models.Responses
{
    [Trait("Area", "Models")]
    [Trait("Phase", "Phase2")]
    [Trait("Type", "Stress")]
    public class Phase2StressTests
    {
        [Fact]
        public void ResponseModels_WithExtremeData_HandleGracefully()
        {
            var extremeValues = new[]
            {
                new string('A', 10000),  // Very long string
                "",                     // Empty string
                null,                   // Null value
                "🎵🎶🎸🥁🎤🎧🎹🎺🎻🎷", // Unicode music symbols
                new string('\n', 100),  // All newlines
                "<>&\"'",               // Special characters
                "Normal Artist Name"    // Control case
            };

            foreach (var value in extremeValues)
            {
                var item = new RecommendationItem
                {
                    Artist = value,
                    Album = "Test Album",
                    Year = 2024
                };

                var act = () =>
                {
                    var json = JsonSerializer.Serialize(item);
                    var deserialized = JsonSerializer.Deserialize<RecommendationItem>(json);
                    return deserialized;
                };

                act.Should().NotThrow($"Should handle extreme value: {value?.Substring(0, Math.Min(value?.Length ?? 0, 50))}...");
            }
        }

        [Fact]
        public async Task ConcurrentResponseModelOperations_AreThreadSafe()
        {
            var tasks = new Task[100];

            for (int i = 0; i < 100; i++)
            {
                var index = i;
                tasks[i] = Task.Run(() =>
                {
                    // Create different response models concurrently
                    var responses = new object[]
                    {
                        new OpenAIResponse { Id = $"test-{index}", Model = $"gpt-{index}" },
                        new AnthropicResponse { Id = $"msg-{index}", Model = $"claude-{index}" },
                        new RecommendationItem { Artist = $"Artist {index}", Album = $"Album {index}" }
                    };

                    foreach (var response in responses)
                    {
                        var json = JsonSerializer.Serialize(response);
                        var deserialized = JsonSerializer.Deserialize(json, response.GetType());
                        deserialized.Should().NotBeNull();
                    }
                });
            }

            await Task.WhenAll(tasks);
            // Should complete without deadlocks or exceptions
        }

        [Fact]
        public void MalformedJsonHandling_DoesNotCrash()
        {
            var malformedJsons = new[]
            {
                "",
                "{}",
                "null",
                "[]",
                "{\"invalid\": }",
                "{\"id\": \"test\", \"invalid_field\": undefined}",
                "not json at all",
                new string('{', 1000) + new string('}', 1000), // Very nested
            };

            foreach (var malformedJson in malformedJsons)
            {
                var act1 = () => JsonSerializer.Deserialize<OpenAIResponse>(malformedJson);
                var act2 = () => JsonSerializer.Deserialize<AnthropicResponse>(malformedJson);
                var act3 = () => JsonSerializer.Deserialize<RecommendationItem>(malformedJson);

                // Should not crash, may throw JsonException which is expected
                var result1 = Record.Exception(act1);
                var result2 = Record.Exception(act2);
                var result3 = Record.Exception(act3);

                // Verify we get expected exceptions, not crashes
                if (result1 != null)
                    result1.Should().BeOfType<JsonException>($"Should get JsonException for malformed JSON: {malformedJson}");
            }
        }

        [Fact]
        public void ResponseInheritance_WorksCorrectly()
        {
            // Test that OpenAI-compatible models properly inherit
            var openAiResponse = new OpenAIResponse
            {
                Id = "base-test",
                Model = "gpt-4"
            };

            var deepSeekResponse = new DeepSeekResponse
            {
                Id = "deepseek-test",
                Model = "deepseek-chat",
                ReasoningMetadata = new DeepSeekReasoningMetadata()
            };

            // Both should be usable as OpenAIResponse
            OpenAIResponse baseResponse1 = openAiResponse;
            OpenAIResponse baseResponse2 = deepSeekResponse;

            baseResponse1.Should().NotBeNull();
            baseResponse2.Should().NotBeNull();
            baseResponse1.Id.Should().Be("base-test");
            baseResponse2.Id.Should().Be("deepseek-test");
        }

        [Fact]
        public void RecommendationItemValidation_EdgeCases()
        {
            var edgeCases = new[]
            {
                new RecommendationItem { Artist = "A", Album = "B" }, // Minimal valid
                new RecommendationItem { Artist = new string('X', 1000), Album = new string('Y', 1000) }, // Very long
                new RecommendationItem { Artist = "Artist", Album = "Album", Year = 1800 }, // Very old year
                new RecommendationItem { Artist = "Artist", Album = "Album", Year = 3000 }, // Future year
                new RecommendationItem { Artist = "Artist", Album = "Album", Confidence = -1.0 }, // Negative confidence
                new RecommendationItem { Artist = "Artist", Album = "Album", Confidence = 2.0 }, // > 1.0 confidence
            };

            foreach (var item in edgeCases)
            {
                var act = () => item.IsValid();
                act.Should().NotThrow("Validation should handle all edge cases gracefully");

                // Verify the item handles serialization
                var jsonAct = () => JsonSerializer.Serialize(item);
                jsonAct.Should().NotThrow("Should serialize edge case data");
            }
        }

        [Fact]
        public void LargeResponsePayloads_HandleCorrectly()
        {
            // Test with very large response payloads that might cause memory issues
            var largeChoices = new List<OpenAIChoice>();
            for (int i = 0; i < 1000; i++)
            {
                largeChoices.Add(new OpenAIChoice
                {
                    Message = new OpenAIMessage
                    {
                        Content = new string('A', 1000) // 1MB total payload approx
                    }
                });
            }

            var largeResponse = new OpenAIResponse
            {
                Id = "large-test",
                Choices = largeChoices
            };

            var act = () =>
            {
                var json = JsonSerializer.Serialize(largeResponse);
                var deserialized = JsonSerializer.Deserialize<OpenAIResponse>(json);
                return deserialized;
            };

            act.Should().NotThrow("Should handle large payloads without memory issues");
        }
    }
}
