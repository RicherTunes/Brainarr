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

    // OpenAI models (drop-down). GPT-5 family is current flagship as of May 2026;
    // 4.1 / 4o values retained so existing user settings still resolve to a working
    // raw id. ModelIdMapper handles the translation from these canonical names to
    // the actual OpenAI API id strings.
    public enum OpenAIModelKind
    {
        GPT5 = 0,
        GPT5_Mini = 1,
        GPT5_Nano = 2,
        GPT41 = 3,
        GPT41_Mini = 4,
        GPT41_Nano = 5,
        GPT4o = 6,
        GPT4o_Mini = 7,
        O3_Pro = 8,
        O1_Preview = 9,
        O1_Mini = 10,
        O4_Mini = 11
    }

    // Anthropic Claude — May 2026 lineup. Opus 4.7 is the current flagship,
    // Sonnet 4.6 is the balanced default, Haiku 4.5 is the fast/cheap option.
    // Older entries kept for back-compat with existing settings; ModelIdMapper
    // resolves them to the most-recent equivalent raw id.
    public enum AnthropicModelKind
    {
        ClaudeOpus47 = 0,
        ClaudeSonnet46 = 1,
        ClaudeHaiku45 = 2,
        ClaudeSonnet4 = 3,
        Claude37_Sonnet = 4,
        Claude35_Haiku = 5,
        Claude3_Opus = 6
    }

    // OpenRouter — May 2026 popular routes. "Auto" lets OpenRouter pick.
    public enum OpenRouterModelKind
    {
        Auto = 0,
        ClaudeSonnet46 = 1,
        ClaudeOpus47 = 2,
        GPT5 = 3,
        GPT5_Mini = 4,
        Gemini3_Pro = 5,
        Gemini3_Flash = 6,
        Llama4_Scout = 7,
        DeepSeekV4 = 8,
        // Legacy values retained so existing user picks still resolve.
        ClaudeSonnet4 = 9,
        GPT41_Mini = 10,
        Gemini25_Flash = 11,
        Llama33_70B = 12,
        DeepSeekV3 = 13
    }

    // DeepSeek — V4 family is current as of May 2026. Old `deepseek-chat` and
    // `deepseek-reasoner` ids deprecate after 2026-07-24 per docs; kept here for
    // existing settings, mapped to V4 equivalents in ModelIdMapper when DeepSeek
    // server-side starts rejecting them.
    public enum DeepSeekModelKind
    {
        DeepSeek_V4_Pro = 0,
        DeepSeek_V4_Flash = 1,
        DeepSeek_Chat = 2,
        DeepSeek_Reasoner = 3,
        DeepSeek_R1 = 4,
        DeepSeek_Search = 5
    }

    // Google Gemini — May 2026: 3.x is current, 2.5 family still serves as a
    // stable fallback. 1.5 family kept for back-compat with old user settings.
    public enum GeminiModelKind
    {
        Gemini_3_1_Pro = 0,
        Gemini_3_Flash = 1,
        Gemini_3_1_Flash_Lite = 2,
        Gemini_25_Pro = 3,
        Gemini_25_Flash = 4,
        Gemini_25_Flash_Lite = 5,
        Gemini_20_Flash = 6,
        Gemini_15_Flash = 7,
        Gemini_15_Flash_8B = 8,
        Gemini_15_Pro = 9
    }

    // Groq Cloud — May 2026 catalog. Llama 3.3 70B Versatile remains the default
    // workhorse; OpenAI's GPT-OSS-120B and Groq Compound are recent premium
    // additions; Qwen3-32B is the strongest open-weight option.
    public enum GroqModelKind
    {
        Llama33_70B_Versatile = 0,
        Llama33_70B_SpecDec = 1,
        Llama31_8B_Instant = 2,
        OpenAi_Gpt_Oss_120B = 3,
        Groq_Compound = 4,
        Qwen3_32B = 5,
        DeepSeek_R1_Distill_L70B = 6
    }

    // Perplexity Sonar — May 2026 catalog. Deep Research is the new exhaustive
    // multi-source variant; Reasoning Pro is the premium chain-of-thought.
    public enum PerplexityModelKind
    {
        Sonar_Pro = 0,
        Sonar_Reasoning_Pro = 1,
        Sonar_Reasoning = 2,
        Sonar_Deep_Research = 3,
        Sonar = 4
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
