using System;
using System.Text.Json.Serialization;

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
        Groq = 8,
        ClaudeCodeSubscription = 9,
        OpenAICodexSubscription = 10,
        // Wave-4d: alternate Claude path that shells out to the `claude` CLI via common's
        // ClaudeCodeProvider. Coexists with ClaudeCodeSubscription (above) — same underlying
        // OAuth, two integration shapes:
        //   ClaudeCodeSubscription: read OAuth token from credential file, hit Anthropic REST.
        //   ClaudeCodeCli:          shell out to `claude` and let the CLI handle auth + transport.
        // No CLI install required for the subscription path; no credential parsing required for
        // the CLI path. Numeric value picked at the end of the enum so existing user settings are
        // unaffected.
        ClaudeCodeCli = 11,
        // Z.AI (Zhipu) GLM provider — OpenAI-compatible chat completions at api.z.ai.
        // First-class support for the GLM-5.x and GLM-4.x families. Added as the
        // last enum value so existing user settings are unaffected by the addition.
        ZaiGlm = 12
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
    [JsonConverter(typeof(StopSensitivityJsonConverter))]
    public enum StopSensitivity
    {
        Off = 0,
        Lenient = 1,
        Normal = 2,
        // Backwards-compat alias for legacy stored values and docs
        Balanced = Normal,
        Strict = 3,
        Aggressive = 4
    }

    // UI enum for OpenAI models (drop-down). Kept minimal; mapped to raw ids elsewhere.
    public enum OpenAIModelKind
    {
        GPT41 = 0,
        GPT41_Mini = 1,
        GPT41_Nano = 2,
        GPT4o = 3,
        GPT4o_Mini = 4,
        O4_Mini = 5
    }

    // UI enums for other cloud providers used in drop-downs
    public enum AnthropicModelKind
    {
        ClaudeSonnet4 = 0,
        Claude37_Sonnet = 1,
        Claude35_Haiku = 2,
        Claude3_Opus = 3
    }

    public enum OpenRouterModelKind
    {
        Auto = 0,
        ClaudeSonnet4 = 1,
        GPT41_Mini = 2,
        Gemini25_Flash = 3,
        Llama33_70B = 4,
        DeepSeekV3 = 5
    }

    public enum DeepSeekModelKind
    {
        DeepSeek_Chat = 0,
        DeepSeek_Reasoner = 1,
        DeepSeek_R1 = 2,
        DeepSeek_Search = 3
    }

    public enum GeminiModelKind
    {
        Gemini_25_Pro = 0,
        Gemini_25_Flash = 1,
        Gemini_25_Flash_Lite = 2,
        Gemini_20_Flash = 3,
        Gemini_15_Flash = 4,
        Gemini_15_Flash_8B = 5,
        Gemini_15_Pro = 6
    }

    public enum GroqModelKind
    {
        Llama33_70B_Versatile = 0,
        Llama33_70B_SpecDec = 1,
        DeepSeek_R1_Distill_L70B = 2,
        Llama31_8B_Instant = 3
    }

    public enum PerplexityModelKind
    {
        Sonar_Pro = 0,
        Sonar_Reasoning_Pro = 1,
        Sonar_Reasoning = 2,
        Sonar = 3
    }

    // Z.AI GLM models — verified against docs.z.ai as of May 2026.
    // Lineup as published by Zhipu Z.AI:
    //   GLM-5.1     — current flagship, 200K context, 128K max output, long-horizon agent
    //   GLM-5       — 745B MoE, released Feb 2026
    //   GLM-5-Turbo — fast/coding variant
    //   GLM-4.7     — multilingual coding gains over 4.6
    //   GLM-4.6     — 200K context, was flagship before 4.7
    //   GLM-4.5     — 355B params, agent-oriented
    //   GLM-4.5-Air — 106B params, balanced cost/quality (default)
    //   GLM-4-32B   — 32B parameter variant (glm-4-32b-0414-128k)
    // Newest first in numeric order so newer = higher value, but the default
    // points at GLM_4_5_Air (best cost/quality for brainarr's prompt sizes).
    public enum ZaiGlmModelKind
    {
        GLM_5_1 = 0,
        GLM_5 = 1,
        GLM_5_Turbo = 2,
        GLM_4_7 = 3,
        GLM_4_6 = 4,
        GLM_4_5 = 5,
        GLM_4_5_Air = 6,
        GLM_4_32B = 7
    }
}
