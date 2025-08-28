using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Anthropic;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Google;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Local;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.OpenAI;
using Xunit;

namespace Brainarr.Tests.Models.Responses
{
    public class ResponseModelTests
    {
        [Fact]
        public void RecommendationItem_Should_Validate_Required_Fields()
        {
            // Arrange
            var validItem = new RecommendationItem
            {
                Artist = "Pink Floyd",
                Album = "Dark Side of the Moon"
            };

            var invalidItem = new RecommendationItem
            {
                Artist = "",
                Album = "  "
            };

            // Act & Assert
            validItem.IsValid().Should().BeTrue();
            invalidItem.IsValid().Should().BeFalse();
        }

        [Fact]
        public void RecommendationItem_Should_Normalize_Confidence()
        {
            // Arrange
            var items = new[]
            {
                new RecommendationItem { Confidence = 0.75 },
                new RecommendationItem { Confidence = 1.5 },
                new RecommendationItem { Confidence = -0.1 },
                new RecommendationItem { Confidence = null }
            };

            // Act & Assert
            items[0].GetNormalizedConfidence().Should().Be(0.75);
            items[1].GetNormalizedConfidence().Should().Be(1.0);
            items[2].GetNormalizedConfidence().Should().Be(0.0);
            items[3].GetNormalizedConfidence().Should().Be(0.5);
        }

        [Fact]
        public void OpenAIResponse_Should_Deserialize_Correctly()
        {
            // Arrange
            var json = @"{
                ""id"": ""chatcmpl-123"",
                ""object"": ""chat.completion"",
                ""created"": 1677652288,
                ""model"": ""gpt-4"",
                ""choices"": [{
                    ""index"": 0,
                    ""message"": {
                        ""role"": ""assistant"",
                        ""content"": ""Here are some recommendations""
                    },
                    ""finish_reason"": ""stop""
                }],
                ""usage"": {
                    ""prompt_tokens"": 100,
                    ""completion_tokens"": 50,
                    ""total_tokens"": 150
                }
            }";

            // Act
            var response = JsonSerializer.Deserialize<OpenAIResponse>(json);

            // Assert
            response.Should().NotBeNull();
            response.Id.Should().Be("chatcmpl-123");
            response.GetContent().Should().Be("Here are some recommendations");
            response.IsComplete().Should().BeTrue();
            response.Usage.TotalTokens.Should().Be(150);
        }

        [Fact]
        public void AnthropicResponse_Should_Extract_Content_Correctly()
        {
            // Arrange
            var response = new AnthropicResponse
            {
                StopReason = "end_turn",
                Content = new List<AnthropicContent>
                {
                    new AnthropicContent { Type = "text", Text = "First part" },
                    new AnthropicContent { Type = "text", Text = "Second part" }
                }
            };

            // Act
            var content = response.GetContent();
            var isComplete = response.IsComplete();

            // Assert
            content.Should().Be("First part\nSecond part");
            isComplete.Should().BeTrue();
        }

        [Fact]
        public void GeminiResponse_Should_Handle_Safety_Blocking()
        {
            // Arrange
            var blockedResponse = new GeminiResponse
            {
                PromptFeedback = new GeminiPromptFeedback
                {
                    BlockReason = "SAFETY"
                }
            };

            var normalResponse = new GeminiResponse
            {
                Candidates = new List<GeminiCandidate>
                {
                    new GeminiCandidate
                    {
                        Content = new GeminiContent
                        {
                            Parts = new List<GeminiPart>
                            {
                                new GeminiPart { Text = "Safe content" }
                            }
                        },
                        FinishReason = "STOP"
                    }
                }
            };

            // Act & Assert
            blockedResponse.IsBlocked().Should().BeTrue();
            blockedResponse.GetContent().Should().BeEmpty();

            normalResponse.IsBlocked().Should().BeFalse();
            normalResponse.GetContent().Should().Be("Safe content");
            normalResponse.IsComplete().Should().BeTrue();
        }

        [Fact]
        public void OllamaResponse_Should_Calculate_Performance_Metrics()
        {
            // Arrange
            var response = new OllamaResponse
            {
                Response = "Test response",
                EvalCount = 100,
                EvalDuration = 2_000_000_000, // 2 seconds in nanoseconds
                TotalDuration = 3_000_000_000  // 3 seconds
            };

            // Act
            var tokensPerSecond = response.GetTokensPerSecond();
            var totalTimeMs = response.GetTotalTimeMs();

            // Assert
            tokensPerSecond.Should().BeApproximately(50.0, 0.1);
            totalTimeMs.Should().Be(3000);
        }

        [Fact]
        public void OllamaResponse_Should_Support_Both_Endpoints()
        {
            // Arrange
            var chatResponse = new OllamaResponse
            {
                Message = new OllamaMessage
                {
                    Role = "assistant",
                    Content = "Chat response"
                }
            };

            var generateResponse = new OllamaResponse
            {
                Response = "Generate response"
            };

            // Act & Assert
            chatResponse.GetContent().Should().Be("Chat response");
            generateResponse.GetContent().Should().Be("Generate response");
        }

        [Theory]
        [InlineData(1950, true)]
        [InlineData(2024, true)]
        [InlineData(1899, false)]
        [InlineData(2101, false)]
        [InlineData(null, false)]
        public void RecommendationItem_Should_Validate_Year(int? year, bool expectedValid)
        {
            // Arrange
            var item = new RecommendationItem { Year = year };

            // Act & Assert
            item.HasValidYear().Should().Be(expectedValid);
        }

        [Fact]
        public void Response_Models_Should_Have_Default_Values()
        {
            // Arrange & Act
            var openAI = new OpenAIResponse();
            var anthropic = new AnthropicResponse();
            var gemini = new GeminiResponse();
            var ollama = new OllamaResponse();

            // Assert - No null reference exceptions
            openAI.Choices.Should().NotBeNull().And.BeEmpty();
            anthropic.Content.Should().NotBeNull().And.BeEmpty();
            gemini.Candidates.Should().NotBeNull().And.BeEmpty();
            ollama.Context.Should().NotBeNull().And.BeEmpty();
        }

        [Fact]
        public void AnthropicUsage_Should_Calculate_Total_Tokens()
        {
            // Arrange
            var usage = new AnthropicUsage
            {
                InputTokens = 100,
                OutputTokens = 50
            };

            // Act
            var total = usage.GetTotalTokens();

            // Assert
            total.Should().Be(150);
        }

        [Fact]
        public void Response_Deserialization_Should_Handle_Missing_Fields()
        {
            // Arrange - Minimal valid JSON
            var minimalJson = @"{""choices"":[{""message"":{""content"":""test""}}]}";

            // Act
            var response = JsonSerializer.Deserialize<OpenAIResponse>(minimalJson);

            // Assert
            response.Should().NotBeNull();
            response.GetContent().Should().Be("test");
            response.Id.Should().BeEmpty();
            response.Model.Should().BeEmpty();
            response.Usage.Should().NotBeNull();
            response.Usage.TotalTokens.Should().Be(0);
        }
    }
}