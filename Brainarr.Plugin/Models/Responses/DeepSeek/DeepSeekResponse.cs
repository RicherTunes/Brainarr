using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.OpenAI;

namespace NzbDrone.Core.ImportLists.Brainarr.Models.Responses.DeepSeek
{
    /// <summary>
    /// DeepSeek response format - uses OpenAI-compatible API
    /// </summary>
    public class DeepSeekResponse : OpenAIResponse
    {
        // DeepSeek implements OpenAI-compatible API format
        // All base fields inherited from OpenAIResponse

        /// <summary>
        /// DeepSeek specific reasoning tokens for DeepSeek-R1 model
        /// </summary>
        public DeepSeekReasoningMetadata ReasoningMetadata { get; set; }
    }

    /// <summary>
    /// DeepSeek R1 reasoning model specific metadata
    /// </summary>
    public class DeepSeekReasoningMetadata
    {
        public int ReasoningTokens { get; set; }
        public string ReasoningProcess { get; set; } = string.Empty;
        public double ReasoningConfidence { get; set; }
        public int ThinkingSteps { get; set; }
    }
}
