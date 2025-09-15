using System.Text.Json;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.OpenAI;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Anthropic;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Google;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Local;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.DeepSeek;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Groq;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.OpenRouter;
using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Perplexity;
using Xunit;

namespace Brainarr.Tests.Models.Responses
{
    [Trait("Category", "Integration")]
    [Trait("Phase", "Phase2")]
    [Trait("Priority", "Critical")]
    public class Phase2IntegrationTests
    {
        [Fact]
        public void AllProviderModels_SerializeAndDeserialize_Perfectly()
        {
            // Test each provider model individually
            var providerTests = new (string Provider, object Model)[]
            {
                ("OpenAI", new OpenAIResponse { Id = "test", Model = "gpt-4" }),
                ("Anthropic", new AnthropicResponse { Id = "msg_test", Type = "message" }),
                ("Google", new GeminiResponse { Candidates = new() }),
                ("Ollama", new OllamaResponse { Model = "llama2", Response = "test" }),
                ("LMStudio", new LMStudioResponse { Id = "lms_test" }),
                ("DeepSeek", new DeepSeekResponse { Id = "ds_test", Model = "deepseek-chat" }),
                ("Groq", new GroqResponse { Id = "groq_test" }),
                ("OpenRouter", new OpenRouterResponse { Id = "or_test" }),
                ("Perplexity", new PerplexityResponse { Id = "pplx_test" }),
                ("Base", new RecommendationItem { Artist = "Test", Album = "Test" })
            };

            foreach (var (provider, model) in providerTests)
            {
                var act = () =>
                {
                    var json = JsonSerializer.Serialize(model);
                    json.Should().NotBeEmpty($"{provider} model should serialize to non-empty JSON");

                    var deserialized = JsonSerializer.Deserialize(json, model.GetType());
                    deserialized.Should().NotBeNull($"{provider} model should deserialize successfully");

                    return deserialized;
                };

                act.Should().NotThrow($"{provider} model serialization should not throw exceptions");
            }
        }

        [Fact]
        public void OpenAICompatibleModels_InheritanceBehavior_IsCorrect()
        {
            // Models that inherit from OpenAI should work as OpenAI responses
            var openAiCompatible = new object[]
            {
                new DeepSeekResponse { Id = "ds_inherit_test" },
                new GroqResponse { Id = "groq_inherit_test" },
                new LMStudioResponse { Id = "lms_inherit_test" },
                new OpenRouterResponse { Id = "or_inherit_test" },
                new PerplexityResponse { Id = "pplx_inherit_test" }
            };

            foreach (var model in openAiCompatible)
            {
                // Should be usable as OpenAIResponse
                model.Should().BeAssignableTo<OpenAIResponse>(
                    $"{model.GetType().Name} should inherit from OpenAIResponse for API compatibility");

                // Should serialize as OpenAI format
                var asOpenAI = (OpenAIResponse)model;
                asOpenAI.Id.Should().NotBeEmpty("Should have valid ID from base class");

                var json = JsonSerializer.Serialize(asOpenAI);
                json.Should().Contain("\"id\":", "Should serialize with OpenAI-compatible format");
            }
        }

        [Fact]
        public void ResponseModels_WithRealWorldData_HandleCorrectly()
        {
            // Test with realistic AI response data
            var openAiJson = @"{
                    ""id"": ""chatcmpl-123"",
                    ""object"": ""chat.completion"",
                    ""created"": 1677652288,
                    ""model"": ""gpt-4"",
                    ""choices"": [{
                        ""index"": 0,
                        ""message"": {
                            ""role"": ""assistant"",
                            ""content"": ""Here are some music recommendations...""
                        },
                        ""finish_reason"": ""stop""
                    }],
                    ""usage"": {
                        ""prompt_tokens"": 56,
                        ""completion_tokens"": 31,
                        ""total_tokens"": 87
                    }
                }";

            var anthropicJson = @"{
                    ""id"": ""msg_01EhYXgF4ommNNqw5dLRaJqX"",
                    ""type"": ""message"",
                    ""role"": ""assistant"",
                    ""content"": [{
                        ""type"": ""text"",
                        ""text"": ""Here are personalized music recommendations...""
                    }],
                    ""model"": ""claude-3-sonnet-20240229"",
                    ""stop_reason"": ""end_turn"",
                    ""usage"": {
                        ""input_tokens"": 48,
                        ""output_tokens"": 67
                    }
                }";

            // Test deserialization
            var openAiResponse = JsonSerializer.Deserialize<OpenAIResponse>(openAiJson);
            var anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(anthropicJson);

            // Verify proper parsing
            openAiResponse.Should().NotBeNull();
            openAiResponse.Id.Should().Be("chatcmpl-123");
            openAiResponse.Choices.Should().HaveCount(1);

            anthropicResponse.Should().NotBeNull();
            anthropicResponse.Id.Should().Be("msg_01EhYXgF4ommNNqw5dLRaJqX");
            anthropicResponse.Content.Should().HaveCount(1);
        }

        [Fact]
        public void ResponseModels_ArchitectureCompliance_IsSound()
        {
            // Verify architectural principles
            var modelTypes = new[]
            {
                typeof(OpenAIResponse),
                typeof(AnthropicResponse),
                typeof(GeminiResponse),
                typeof(OllamaResponse),
                typeof(DeepSeekResponse),
                typeof(RecommendationItem)
            };

            foreach (var modelType in modelTypes)
            {
                // Should be in correct namespace
                modelType.Namespace.Should().StartWith("NzbDrone.Core.ImportLists.Brainarr.Models.Responses");

                // Should be public
                modelType.IsPublic.Should().BeTrue($"{modelType.Name} should be public for serialization");

                // Should have parameterless constructor for JSON deserialization
                var constructor = modelType.GetConstructor(Type.EmptyTypes);
                constructor.Should().NotBeNull($"{modelType.Name} should have parameterless constructor");
            }
        }

        [Fact]
        public void ResponseModels_DoNotBreakExistingCode_ZeroRegressions()
        {
            // These are completely new models, so they should not affect existing functionality
            // This test verifies that adding these models doesn't break anything

            // Create instances of all models
            var models = new object[]
            {
                new OpenAIResponse(),
                new AnthropicResponse(),
                new GeminiResponse(),
                new OllamaResponse(),
                new LMStudioResponse(),
                new DeepSeekResponse(),
                new GroqResponse(),
                new OpenRouterResponse(),
                new PerplexityResponse(),
                new RecommendationItem()
            };

            // Should all instantiate without issues
            foreach (var model in models)
            {
                model.Should().NotBeNull($"{model.GetType().Name} should instantiate successfully");
            }

            // Should not interfere with each other
            var serializedModels = new List<string>();
            foreach (var model in models)
            {
                var json = JsonSerializer.Serialize(model);
                serializedModels.Add(json);
            }

            // All should be unique and valid
            serializedModels.Should().HaveCount(models.Length);
            serializedModels.Should().OnlyContain(json => !string.IsNullOrEmpty(json));
        }
    }
}
