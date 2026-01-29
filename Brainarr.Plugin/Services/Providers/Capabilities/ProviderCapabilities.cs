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
        public bool SupportsJsonMode { get; init; }
        public bool SupportsSystemPrompt { get; init; }
        public int? MaxContextTokensOverride { get; init; }
        public bool RequiresMinimalFormatting { get; init; }
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
                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.ZaiGlm:
                    return new ProviderCapability
                    {
                        UsesOpenAIChatCompletions = true,
                        ResponseFormats = ResponseFormatSupport.Text | ResponseFormatSupport.JsonSchema,
                        PreferStructuredByDefault = true,
                        SupportsJsonMode = true,
                        SupportsSystemPrompt = true
                    };

                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.LMStudio:
                    return new ProviderCapability
                    {
                        UsesOpenAIChatCompletions = true,
                        ResponseFormats = ResponseFormatSupport.Text | ResponseFormatSupport.JsonSchema,
                        PreferStructuredByDefault = false,
                        SupportsJsonMode = true,
                        SupportsSystemPrompt = true
                    };

                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.Ollama:
                    return new ProviderCapability
                    {
                        UsesOpenAIChatCompletions = false,
                        ResponseFormats = ResponseFormatSupport.None,
                        PreferStructuredByDefault = false,
                        SupportsJsonMode = false,
                        SupportsSystemPrompt = true
                    };

                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.Anthropic:
                    return new ProviderCapability
                    {
                        UsesOpenAIChatCompletions = false,
                        ResponseFormats = ResponseFormatSupport.Text,
                        PreferStructuredByDefault = false,
                        SupportsJsonMode = false,
                        SupportsSystemPrompt = true,
                        RequiresMinimalFormatting = true,
                        MaxContextTokensOverride = 200000
                    };

                case NzbDrone.Core.ImportLists.Brainarr.AIProvider.Gemini:
                    return new ProviderCapability
                    {
                        UsesOpenAIChatCompletions = false,
                        ResponseFormats = ResponseFormatSupport.Text,
                        PreferStructuredByDefault = false,
                        SupportsJsonMode = false,
                        SupportsSystemPrompt = true,
                        RequiresMinimalFormatting = true
                    };

                default:
                    return new ProviderCapability
                    {
                        UsesOpenAIChatCompletions = false,
                        ResponseFormats = ResponseFormatSupport.None,
                        PreferStructuredByDefault = false,
                        SupportsJsonMode = false,
                        SupportsSystemPrompt = false
                    };
            }
        }
    }
}
