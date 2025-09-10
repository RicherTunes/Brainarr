using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Capabilities
{
    [Flags]
    public enum ResponseFormatSupport
    {
        None = 0,
        Text = 1 << 0,
        JsonSchema = 1 << 1,
        JsonObject = 1 << 2
    }

    public sealed class ProviderCapability
    {
        public ResponseFormatSupport ResponseFormats { get; init; }
        public bool UsesOpenAIChatCompletions { get; init; }
        public bool PreferStructuredByDefault { get; init; }
    }

    public static class ProviderCapabilities
    {
        public static ProviderCapability Get(NzbDrone.Core.ImportLists.Brainarr.AIProvider provider)
        {
            switch (provider)
            {
                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.OpenAI:
                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.OpenRouter:
                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.Groq:
                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.DeepSeek:
                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.Perplexity:
                    return new ProviderCapability
                    {
                        UsesOpenAIChatCompletions = true,
                        ResponseFormats = ResponseFormatSupport.Text | ResponseFormatSupport.JsonSchema,
                        PreferStructuredByDefault = true
                    };

                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.LMStudio:
                    return new ProviderCapability
                    {
                        UsesOpenAIChatCompletions = true,
                        // LM Studio’s OpenAI-compatible server expects text or json_schema (json_object unsupported)
                        ResponseFormats = ResponseFormatSupport.Text | ResponseFormatSupport.JsonSchema,
                        PreferStructuredByDefault = false // be conservative for widest compatibility
                    };

                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.Ollama:
                    return new ProviderCapability
                    {
                        UsesOpenAIChatCompletions = false,
                        ResponseFormats = ResponseFormatSupport.None,
                        PreferStructuredByDefault = false
                    };

                // Anthropic/Gemini use different endpoints; builder not applicable
                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.Anthropic:
                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.Gemini:
                default:
                    return new ProviderCapability
                    {
                        UsesOpenAIChatCompletions = false,
                        ResponseFormats = ResponseFormatSupport.None,
                        PreferStructuredByDefault = false
                    };
            }
        }
    }
}
