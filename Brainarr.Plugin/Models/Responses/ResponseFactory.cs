using System;
using System.Text.Json;
using Brainarr.Plugin.Models.Responses.Base;
using Brainarr.Plugin.Models.Responses.OpenAI;
using Brainarr.Plugin.Models.Responses.Anthropic;
using Brainarr.Plugin.Models.Responses.Gemini;
using Brainarr.Plugin.Models.Responses.Local;
using Brainarr.Plugin.Models.Responses.Groq;
using Brainarr.Plugin.Configuration;

namespace Brainarr.Plugin.Models.Responses
{
    /// <summary>
    /// Factory class for creating provider-specific response objects
    /// </summary>
    public static class ResponseFactory
    {
        /// <summary>
        /// Parse response based on provider type
        /// </summary>
        public static IProviderResponse ParseResponse(AIProvider provider, string jsonResponse)
        {
            if (string.IsNullOrWhiteSpace(jsonResponse))
                return null;

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            try
            {
                return provider switch
                {
                    AIProvider.OpenAI => JsonSerializer.Deserialize<OpenAIResponse>(jsonResponse, options),
                    AIProvider.Anthropic => JsonSerializer.Deserialize<AnthropicResponse>(jsonResponse, options),
                    AIProvider.Gemini => JsonSerializer.Deserialize<GeminiResponse>(jsonResponse, options),
                    AIProvider.Ollama => JsonSerializer.Deserialize<OllamaResponse>(jsonResponse, options),
                    AIProvider.LMStudio => JsonSerializer.Deserialize<LMStudioResponse>(jsonResponse, options),
                    AIProvider.Groq => JsonSerializer.Deserialize<GroqResponse>(jsonResponse, options),
                    AIProvider.DeepSeek => JsonSerializer.Deserialize<OpenAIResponse>(jsonResponse, options), // DeepSeek uses OpenAI format
                    AIProvider.Perplexity => JsonSerializer.Deserialize<OpenAIResponse>(jsonResponse, options), // Perplexity uses OpenAI format
                    AIProvider.OpenRouter => JsonSerializer.Deserialize<OpenAIResponse>(jsonResponse, options), // OpenRouter uses OpenAI format
                    _ => throw new NotSupportedException($"Provider {provider} is not supported")
                };
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to parse {provider} response", ex);
            }
        }

        /// <summary>
        /// Create request object based on provider type
        /// </summary>
        public static object CreateRequest(AIProvider provider, string prompt, string model, double temperature = 0.7, int maxTokens = 1000)
        {
            return provider switch
            {
                AIProvider.OpenAI or AIProvider.DeepSeek or AIProvider.Perplexity or AIProvider.OpenRouter => 
                    new OpenAIRequest
                    {
                        Model = model,
                        Messages = new() { new OpenAIMessage { Role = "user", Content = prompt } },
                        Temperature = temperature,
                        MaxTokens = maxTokens,
                        ResponseFormat = new OpenAIRequest.ResponseFormat { Type = "json_object" }
                    },
                    
                AIProvider.Anthropic => 
                    new AnthropicRequest
                    {
                        Model = model,
                        Messages = new() { new AnthropicMessage { Role = "user", Content = prompt } },
                        Temperature = temperature,
                        MaxTokens = maxTokens
                    },
                    
                AIProvider.Gemini => 
                    new GeminiRequest
                    {
                        Contents = new() { new GeminiContent 
                        { 
                            Role = "user", 
                            Parts = new() { new GeminiPart { Text = prompt } } 
                        }},
                        GenerationConfig = new GeminiGenerationConfig
                        {
                            Temperature = temperature,
                            MaxOutputTokens = maxTokens,
                            TopK = 40,
                            TopP = 0.95
                        }
                    },
                    
                AIProvider.Ollama => 
                    new OllamaRequest
                    {
                        Model = model,
                        Prompt = prompt,
                        Stream = false,
                        Format = "json",
                        Options = new OllamaOptions
                        {
                            Temperature = temperature,
                            NumPredict = maxTokens
                        }
                    },
                    
                AIProvider.LMStudio => 
                    new OpenAIRequest // LM Studio uses OpenAI format
                    {
                        Model = model,
                        Messages = new() { new OpenAIMessage { Role = "user", Content = prompt } },
                        Temperature = temperature,
                        MaxTokens = maxTokens
                    },
                    
                AIProvider.Groq => 
                    new GroqRequest
                    {
                        Model = model,
                        Messages = new() { new GroqMessage { Role = "user", Content = prompt } },
                        Temperature = temperature,
                        MaxTokens = maxTokens,
                        ResponseFormat = new GroqRequest.ResponseFormat { Type = "json_object" }
                    },
                    
                _ => throw new NotSupportedException($"Provider {provider} is not supported")
            };
        }

        /// <summary>
        /// Check if provider uses OpenAI-compatible format
        /// </summary>
        public static bool IsOpenAICompatible(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.OpenAI or 
                AIProvider.LMStudio or 
                AIProvider.DeepSeek or 
                AIProvider.Perplexity or 
                AIProvider.OpenRouter or
                AIProvider.Groq => true,
                _ => false
            };
        }
    }
}