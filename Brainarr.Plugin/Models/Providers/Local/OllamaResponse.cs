using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Brainarr.Plugin.Models.Providers.Local
{
    /// <summary>
    /// Ollama local AI provider response format
    /// </summary>
    public class OllamaResponse
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

        [JsonPropertyName("eval_count")]
        public int EvalCount { get; set; }

        [JsonPropertyName("eval_duration")]
        public long EvalDuration { get; set; }

        /// <summary>
        /// Calculates tokens per second for performance monitoring
        /// </summary>
        public double GetTokensPerSecond()
        {
            if (EvalDuration <= 0) return 0;
            return (double)EvalCount / (EvalDuration / 1_000_000_000.0);
        }

        /// <summary>
        /// Gets response time in milliseconds
        /// </summary>
        public long GetResponseTimeMs()
        {
            return TotalDuration / 1_000_000;
        }
    }
}