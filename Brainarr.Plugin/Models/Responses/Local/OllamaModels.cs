using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Brainarr.Plugin.Models.Responses.Base;

namespace Brainarr.Plugin.Models.Responses.Local
{
    /// <summary>
    /// Ollama local AI response format
    /// </summary>
    public class OllamaResponse : IProviderResponse
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; }

        [JsonPropertyName("response")]
        public string Response { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }

        [JsonPropertyName("context")]
        public List<int> Context { get; set; }

        [JsonPropertyName("total_duration")]
        public long TotalDuration { get; set; }

        [JsonPropertyName("load_duration")]
        public long LoadDuration { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public int PromptEvalCount { get; set; }

        [JsonPropertyName("prompt_eval_duration")]
        public long PromptEvalDuration { get; set; }

        [JsonPropertyName("eval_count")]
        public int EvalCount { get; set; }

        [JsonPropertyName("eval_duration")]
        public long EvalDuration { get; set; }

        public string GetContent()
        {
            return Response;
        }

        public bool IsSuccessful()
        {
            return Done && !string.IsNullOrEmpty(Response);
        }

        public int? GetTokenUsage()
        {
            return PromptEvalCount + EvalCount;
        }
    }

    /// <summary>
    /// Ollama streaming response format
    /// </summary>
    public class OllamaStreamResponse
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; }

        [JsonPropertyName("response")]
        public string Response { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }
    }

    /// <summary>
    /// Ollama model list response
    /// </summary>
    public class OllamaModelsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModel> Models { get; set; }
    }

    public class OllamaModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("modified_at")]
        public string ModifiedAt { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("digest")]
        public string Digest { get; set; }
    }

    /// <summary>
    /// Ollama request format
    /// </summary>
    public class OllamaRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("options")]
        public OllamaOptions Options { get; set; }

        [JsonPropertyName("format")]
        public string Format { get; set; }
    }

    public class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("top_k")]
        public int TopK { get; set; }

        [JsonPropertyName("top_p")]
        public double TopP { get; set; }

        [JsonPropertyName("num_predict")]
        public int NumPredict { get; set; }
    }

    /// <summary>
    /// LM Studio response format (OpenAI-compatible)
    /// </summary>
    public class LMStudioResponse : OpenAI.OpenAIResponse
    {
        // LM Studio uses OpenAI-compatible format
    }

    /// <summary>
    /// LM Studio models response
    /// </summary>
    public class LMStudioModelsResponse
    {
        [JsonPropertyName("data")]
        public List<LMStudioModel> Data { get; set; }
    }

    public class LMStudioModel
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("object")]
        public string Object { get; set; }

        [JsonPropertyName("created")]
        public long Created { get; set; }

        [JsonPropertyName("owned_by")]
        public string OwnedBy { get; set; }
    }
}