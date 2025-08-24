using System.Collections.Generic;
using Xunit;
using Brainarr.Plugin.Models.Providers.OpenAI;

namespace Brainarr.Tests.Models.Providers.OpenAI
{
    public class OpenAIResponseTests
    {
        [Fact]
        public void HasValidContent_ReturnsTrueWhenContentPresent()
        {
            var response = new OpenAIResponse
            {
                Choices = new List<OpenAIChoice>
                {
                    new OpenAIChoice
                    {
                        Message = new OpenAIMessage
                        {
                            Content = "Valid content"
                        }
                    }
                }
            };

            Assert.True(response.HasValidContent());
        }

        [Fact]
        public void HasValidContent_ReturnsFalseWhenNoChoices()
        {
            var response = new OpenAIResponse
            {
                Choices = new List<OpenAIChoice>()
            };

            Assert.False(response.HasValidContent());
        }

        [Fact]
        public void HasValidContent_ReturnsFalseWhenContentEmpty()
        {
            var response = new OpenAIResponse
            {
                Choices = new List<OpenAIChoice>
                {
                    new OpenAIChoice
                    {
                        Message = new OpenAIMessage
                        {
                            Content = ""
                        }
                    }
                }
            };

            Assert.False(response.HasValidContent());
        }

        [Fact]
        public void GetContent_ReturnsFirstChoiceContent()
        {
            const string expectedContent = "Test recommendation";
            var response = new OpenAIResponse
            {
                Choices = new List<OpenAIChoice>
                {
                    new OpenAIChoice
                    {
                        Message = new OpenAIMessage
                        {
                            Content = expectedContent
                        }
                    },
                    new OpenAIChoice
                    {
                        Message = new OpenAIMessage
                        {
                            Content = "Second choice"
                        }
                    }
                }
            };

            Assert.Equal(expectedContent, response.GetContent());
        }

        [Fact]
        public void GetContent_ReturnsNullWhenNoChoices()
        {
            var response = new OpenAIResponse();
            
            Assert.Null(response.GetContent());
        }

        [Fact]
        public void OpenAIChoice_IsComplete_ChecksFinishReason()
        {
            var completeChoice = new OpenAIChoice { FinishReason = "stop" };
            var lengthChoice = new OpenAIChoice { FinishReason = "length" };
            var errorChoice = new OpenAIChoice { FinishReason = "content_filter" };

            Assert.True(completeChoice.IsComplete());
            Assert.True(lengthChoice.IsComplete());
            Assert.False(errorChoice.IsComplete());
        }

        [Fact]
        public void OpenAIMessage_FactoryMethods_CreateCorrectRoles()
        {
            var system = OpenAIMessage.System("System prompt");
            var user = OpenAIMessage.User("User message");
            var assistant = OpenAIMessage.Assistant("Assistant response");

            Assert.Equal("system", system.Role);
            Assert.Equal("System prompt", system.Content);
            Assert.Equal("user", user.Role);
            Assert.Equal("User message", user.Content);
            Assert.Equal("assistant", assistant.Role);
            Assert.Equal("Assistant response", assistant.Content);
        }

        [Theory]
        [InlineData("gpt-4", 100, 50, 0.0045)]
        [InlineData("gpt-3.5-turbo", 100, 50, 0.0003)]
        [InlineData("gpt-4-turbo", 100, 50, 0.0045)]
        public void OpenAIUsage_EstimatedCost_CalculatesCorrectly(
            string model, int promptTokens, int completionTokens, decimal expectedCost)
        {
            var usage = new OpenAIUsage
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                TotalTokens = promptTokens + completionTokens
            };

            var cost = usage.EstimatedCost(model);
            
            Assert.Equal(expectedCost, cost, 4);
        }
    }
}