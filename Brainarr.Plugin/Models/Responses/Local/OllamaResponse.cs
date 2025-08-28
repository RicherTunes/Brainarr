using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Local
{
    /// <summary>
    /// Ollama response format for local AI model inference
    /// </summary>
    public class OllamaResponse
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = string.Empty;

        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public OllamaMessage Message { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }

        [JsonPropertyName("done_reason")]
        public string DoneReason { get; set; } = string.Empty;

        [JsonPropertyName("context")]
        public List<int> Context { get; set; } = new List<int>();

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

        /// <summary>
        /// Gets the response content (supports both chat and generate endpoints)
        /// </summary>
        public string GetContent()
        {
            // Chat endpoint uses Message.Content
            if (Message != null && !string.IsNullOrEmpty(Message.Content))
                return Message.Content;

            // Generate endpoint uses Response field
            return Response ?? string.Empty;
        }

        /// <summary>
        /// Calculates tokens per second for performance metrics
        /// </summary>
        public double GetTokensPerSecond()
        {
            if (EvalDuration <= 0)
                return 0;

            // Duration is in nanoseconds
            var seconds = EvalDuration / 1_000_000_000.0;
            return EvalCount / seconds;
        }

        /// <summary>
        /// Gets total processing time in milliseconds
        /// </summary>
        public long GetTotalTimeMs()
        {
            return TotalDuration / 1_000_000; // Convert from nanoseconds
        }
    }

    public class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// Response for listing available models
    /// </summary>
    public class OllamaModelsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModelInfo> Models { get; set; } = new List<OllamaModelInfo>();
    }

    public class OllamaModelInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("modified_at")]
        public string ModifiedAt { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("digest")]
        public string Digest { get; set; } = string.Empty;

        [JsonPropertyName("details")]
        public OllamaModelDetails Details { get; set; }
    }

    public class OllamaModelDetails
    {
        [JsonPropertyName("parent_model")]
        public string ParentModel { get; set; } = string.Empty;

        [JsonPropertyName("format")]
        public string Format { get; set; } = string.Empty;

        [JsonPropertyName("family")]
        public string Family { get; set; } = string.Empty;

        [JsonPropertyName("families")]
        public List<string> Families { get; set; } = new List<string>();

        [JsonPropertyName("parameter_size")]
        public string ParameterSize { get; set; } = string.Empty;

        [JsonPropertyName("quantization_level")]
        public string QuantizationLevel { get; set; } = string.Empty;
    }
}