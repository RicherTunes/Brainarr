using Brainarr.Plugin.Models.Providers.OpenAI;

namespace Brainarr.Plugin.Models.Providers.Local
{
    /// <summary>
    /// LM Studio response format (OpenAI-compatible)
    /// </summary>
    public class LMStudioResponse : OpenAIResponse
    {
        // LM Studio uses OpenAI-compatible format
        // Inherits all properties and methods from OpenAIResponse
        
        /// <summary>
        /// Identifies this as a local model response
        /// </summary>
        public bool IsLocalModel => true;
    }
}