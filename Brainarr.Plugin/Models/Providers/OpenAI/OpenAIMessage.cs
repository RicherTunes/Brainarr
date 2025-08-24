using System.Text.Json.Serialization;

namespace Brainarr.Plugin.Models.Providers.OpenAI
{
    /// <summary>
    /// Represents a message in the OpenAI chat format
    /// </summary>
    public class OpenAIMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }

        /// <summary>
        /// Creates a system message
        /// </summary>
        public static OpenAIMessage System(string content) => 
            new() { Role = "system", Content = content };

        /// <summary>
        /// Creates a user message
        /// </summary>
        public static OpenAIMessage User(string content) => 
            new() { Role = "user", Content = content };

        /// <summary>
        /// Creates an assistant message
        /// </summary>
        public static OpenAIMessage Assistant(string content) => 
            new() { Role = "assistant", Content = content };
    }
}