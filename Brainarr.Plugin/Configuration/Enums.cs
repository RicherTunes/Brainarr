using System;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    // Provider selection for UI and orchestration
    public enum AIProvider
    {
        Ollama = 0,
        LMStudio = 1,
        Perplexity = 2,
        OpenAI = 3,
        Anthropic = 4,
        OpenRouter = 5,
        DeepSeek = 6,
        Gemini = 7,
        Groq = 8
    }

    // Anthropic extended thinking control
    public enum ThinkingMode
    {
        Off = 0,
        Auto = 1,
        On = 2
    }

    // Discovery direction for recommendations
    public enum DiscoveryMode
    {
        Similar = 0,
        Adjacent = 1,
        Exploratory = 2
    }

    // Library sampling strategy for prompt building
    public enum SamplingStrategy
    {
        Minimal = 0,
        Balanced = 1,
        Comprehensive = 2
    }

    // Output focus
    public enum RecommendationMode
    {
        SpecificAlbums = 0,
        ArtistsOnly = 1,
        Artists = 2,
        Mixed = 3
    }

    // Iterative top-up policy
    public enum BackfillStrategy
    {
        Off = 0,
        Conservative = 1,
        Standard = 2,
        Balanced = 3,
        Aggressive = 4
    }

    // Stop sequence sensitivity for parsing/sanitization
    public enum StopSensitivity
    {
        Off = 0,
        Lenient = 1,
        Normal = 2,
        Strict = 3,
        Aggressive = 4
    }

    // UI enum for OpenAI models (drop-down). Kept minimal; mapped to raw ids elsewhere.
    public enum OpenAIModelKind
    {
        GPT4o = 0,
        GPT4o_Mini = 1,
        GPT4_Turbo = 2,
        GPT35_Turbo = 3
    }

    // UI enums for other cloud providers used in drop-downs
    public enum AnthropicModelKind
    {
        Claude35_Sonnet = 0,
        Claude35_Haiku = 1,
        Claude3_Opus = 2
    }

    public enum OpenRouterModelKind
    {
        Claude35_Sonnet = 0,
        GPT4o_Mini = 1,
        Llama3_70B = 2,
        Gemini15_Flash = 3
    }

    public enum DeepSeekModelKind
    {
        DeepSeek_Chat = 0,
        DeepSeek_Reasoner = 1
    }

    public enum GeminiModelKind
    {
        Gemini15_Flash = 0,
        Gemini15_Pro = 1
    }

    public enum GroqModelKind
    {
        Llama31_70B_Versatile = 0,
        Mixtral_8x7B = 1
    }

    public enum PerplexityModelKind
    {
        Sonar_Large_Online = 0,
        Sonar_Small_Online = 1,
        Llama31_70B_Instruct = 2
    }
}
