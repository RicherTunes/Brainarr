using NzbDrone.Core.ImportLists.Brainarr.Models.Responses.OpenAI;

namespace NzbDrone.Core.ImportLists.Brainarr.Models.Responses.Local
{
    /// <summary>
    /// LM Studio response format - uses OpenAI-compatible API
    /// </summary>
    public class LMStudioResponse : OpenAIResponse
    {
        // LM Studio implements OpenAI-compatible API format
        // All fields inherited from OpenAIResponse
        
        /// <summary>
        /// LM Studio specific metadata that may be present
        /// </summary>
        public LMStudioMetadata Metadata { get; set; }
    }

    /// <summary>
    /// LM Studio specific metadata extensions
    /// </summary>
    public class LMStudioMetadata
    {
        public string ModelPath { get; set; } = string.Empty;
        public string ModelFamily { get; set; } = string.Empty;
        public int ContextLength { get; set; }
        public double Temperature { get; set; }
        public int MaxTokens { get; set; }
        public bool GpuAcceleration { get; set; }
    }
}