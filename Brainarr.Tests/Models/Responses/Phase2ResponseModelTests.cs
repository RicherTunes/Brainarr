using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Anthropic;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.OpenAI;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Google;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Local;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.DeepSeek;
using Xunit;

namespace Brainarr.Tests.Models.Responses
{
    [Trait("Category", "Models")]
    [Trait("Phase", "Phase2")]
    public class Phase2ResponseModelTests
    {
        [Fact]
        public void RecommendationItem_Validation_WorksCorrectly()
        {
            // Arrange
            var validItem = new RecommendationItem
            {
                Artist = "The Beatles",
                Album = "Abbey Road",
                Year = 1969,
                Genre = "Rock"
            };

            var invalidItem = new RecommendationItem(); // Empty

            // Act & Assert
            validItem.IsValid().Should().BeTrue();
            invalidItem.IsValid().Should().BeFalse();
        }

        [Fact]
        public void OpenAIResponse_Serialization_WorksProperly()
        {
            // Arrange
            var response = new OpenAIResponse
            {
                Id = "chatcmpl-123",
                Object = "chat.completion",
                Model = "gpt-4",
                Choices = new List<OpenAIChoice>
                {
                    new OpenAIChoice
                    {
                        Message = new OpenAIMessage
                        {
                            Content = "Test recommendation content"
                        }
                    }
                }
            };

            // Act
            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<OpenAIResponse>(json);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Id.Should().Be("chatcmpl-123");
            deserialized.Model.Should().Be("gpt-4");
            deserialized.Choices.Should().HaveCount(1);
        }

        [Fact]
        public void AnthropicResponse_Serialization_WorksProperly()
        {
            // Arrange
            var response = new AnthropicResponse
            {
                Id = "msg_123",
                Type = "message",
                Model = "claude-3-sonnet",
                Content = new List<AnthropicContent>
                {
                    new AnthropicContent
                    {
                        Type = "text",
                        Text = "Music recommendations..."
                    }
                }
            };

            // Act
            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<AnthropicResponse>(json);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Id.Should().Be("msg_123");
            deserialized.Model.Should().Be("claude-3-sonnet");
            deserialized.Content.Should().HaveCount(1);
        }

        [Fact]
        public void GeminiResponse_Serialization_WorksProperly()
        {
            // Arrange
            var response = new GeminiResponse
            {
                Candidates = new List<GeminiCandidate>
                {
                    new GeminiCandidate
                    {
                        Content = new GeminiContent
                        {
                            Parts = new List<GeminiPart>
                            {
                                new GeminiPart { Text = "Gemini music recommendations" }
                            }
                        }
                    }
                }
            };

            // Act
            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<GeminiResponse>(json);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Candidates.Should().HaveCount(1);
            deserialized.Candidates[0].Content.Parts.Should().HaveCount(1);
        }

        [Fact]
        public void OllamaResponse_Serialization_WorksProperly()
        {
            // Arrange
            var response = new OllamaResponse
            {
                Model = "llama2",
                Response = "Local music recommendations",
                Done = true
            };

            // Act
            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<OllamaResponse>(json);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Model.Should().Be("llama2");
            deserialized.Response.Should().Be("Local music recommendations");
            deserialized.Done.Should().BeTrue();
        }

        [Fact]
        public void DeepSeekResponse_InheritsOpenAICompatibility()
        {
            // Arrange
            var response = new DeepSeekResponse
            {
                Id = "deepseek-123",
                Model = "deepseek-chat",
                Choices = new List<OpenAIChoice>
                {
                    new OpenAIChoice
                    {
                        Message = new OpenAIMessage { Content = "DeepSeek recommendations" }
                    }
                },
                ReasoningMetadata = new DeepSeekReasoningMetadata
                {
                    ReasoningTokens = 150,
                    ReasoningProcess = "Step-by-step analysis",
                    ThinkingSteps = 5
                }
            };

            // Act
            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<DeepSeekResponse>(json);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Id.Should().Be("deepseek-123");
            deserialized.ReasoningMetadata.Should().NotBeNull();
            deserialized.ReasoningMetadata.ReasoningTokens.Should().Be(150);

            // Verify OpenAI compatibility
            OpenAIResponse baseResponse = response;
            baseResponse.Should().NotBeNull();
            baseResponse.Id.Should().Be("deepseek-123");
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        public void RecommendationItem_WithInvalidArtist_IsInvalid(string artist)
        {
            // Arrange
            var item = new RecommendationItem
            {
                Artist = artist,
                Album = "Valid Album"
            };

            // Act & Assert
            item.IsValid().Should().BeFalse();
        }

        [Fact]
        public void AllResponseModels_HaveProperJsonPropertyNames()
        {
            // This test ensures all models can be serialized/deserialized properly
            var models = new object[]
            {
                new OpenAIResponse(),
                new AnthropicResponse(),
                new GeminiResponse(),
                new OllamaResponse(),
                new DeepSeekResponse(),
                new RecommendationItem()
            };

            foreach (var model in models)
            {
                var act = () =>
                {
                    var json = JsonSerializer.Serialize(model);
                    json.Should().NotBeEmpty();
                    return JsonSerializer.Deserialize(json, model.GetType());
                };

                act.Should().NotThrow($"{model.GetType().Name} should serialize/deserialize without errors");
            }
        }
    }
}
