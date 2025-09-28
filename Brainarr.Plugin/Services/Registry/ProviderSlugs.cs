using NzbDrone.Core.ImportLists.Brainarr;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Registry;

internal static class ProviderSlugs
{
    public static string? ToRegistrySlug(AIProvider provider)
    {
        return provider switch
        {
            AIProvider.OpenAI => "openai",
            AIProvider.Perplexity => "perplexity",
            AIProvider.Anthropic => "anthropic",
            AIProvider.OpenRouter => "openrouter",
            AIProvider.DeepSeek => "deepseek",
            AIProvider.Gemini => "gemini",
            AIProvider.Groq => "groq",
            AIProvider.Ollama => "ollama",
            AIProvider.LMStudio => "lmstudio",
            _ => null
        };
    }
}
