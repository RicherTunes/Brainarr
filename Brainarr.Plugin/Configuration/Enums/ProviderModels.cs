namespace NzbDrone.Core.ImportLists.Brainarr.Configuration.Enums
{
    /// <summary>
    /// Available models for each AI provider
    /// </summary>
    
    public enum PerplexityModel
    {
        SonarMedium = 0,    // llama-3-sonar-medium-32k-online (balanced)
        SonarSmall = 1,     // llama-3-sonar-small-32k-online (faster)
        SonarLarge = 2      // llama-3-sonar-large-32k-online (best quality)
    }

    public enum OpenAIModel
    {
        GPT4oMini = 0,      // gpt-4o-mini (cost-effective, recommended)
        GPT35Turbo = 1,     // gpt-3.5-turbo (fast, cheap)
        GPT4o = 2,          // gpt-4o (high quality)
        GPT4Turbo = 3       // gpt-4-turbo (latest GPT-4)
    }

    public enum AnthropicModel
    {
        Claude3Haiku = 0,   // claude-3-haiku (fastest, cheapest)
        Claude3Sonnet = 1,  // claude-3-sonnet (balanced)
        Claude3Opus = 2     // claude-3-opus (most capable)
    }

    public enum OpenRouterModel
    {
        Auto = 0,                   // openrouter/auto (best for prompt)
        Llama370B = 1,             // meta-llama/llama-3-70b (open, capable)
        Mixtral8x22B = 2,          // mistralai/mixtral-8x22b (efficient)
        Qwen272B = 3,              // qwen/qwen-2-72b (multilingual)
        CommandRPlus = 4,          // cohere/command-r-plus (great for structured)
        ClaudeHaiku = 5,           // anthropic/claude-3-haiku (fast Claude)
        ClaudeSonnet = 6,          // anthropic/claude-3.5-sonnet (balanced Claude)
        GeminiPro15 = 7,           // google/gemini-pro-1.5 (long context)
        GPT4oMini = 8,             // openai/gpt-4o-mini (cost-effective GPT)
        GPT4o = 9,                 // openai/gpt-4o (latest GPT-4)
        DeepseekChat = 10,         // deepseek/deepseek-chat (very cheap)
        SonarMedium = 11,          // perplexity/llama-3-sonar-medium (web-aware)
        MistralLarge = 12,         // mistralai/mistral-large (Mistral flagship)
        Yi34BChat = 13,            // 01-ai/yi-34b-chat (efficient bilingual)
        PhiMedium = 14,            // microsoft/phi-3-medium (compact)
        NousHermes = 15,           // nousresearch/nous-hermes-2-mixtral (uncensored)
        MythoMax = 16,             // gryphe/mythomax-l2-13b (creative)
        Cinematika = 17,           // openrouter/cinematika-7b (storytelling)
        Dolphin = 18,              // cognitivecomputations/dolphin-mixtral (helpful)
        WizardLM = 19              // microsoft/wizardlm-2-8x22b (reasoning)
    }

    public enum DeepSeekModel
    {
        DeepSeekChat = 0,  // deepseek-chat (general purpose)
        DeepSeekCoder = 1  // deepseek-coder (code-focused)
    }

    public enum GeminiModel
    {
        Gemini15Flash = 0,  // gemini-1.5-flash (fast, efficient)
        Gemini15Pro = 1,    // gemini-1.5-pro (balanced)
        Gemini10Pro = 2     // gemini-1.0-pro (legacy)
    }

    public enum GroqModel
    {
        Llama370B = 0,      // llama3-70b-8192 (large, capable)
        Llama38B = 1,       // llama3-8b-8192 (fast, efficient)
        Mixtral8x7B = 2,    // mixtral-8x7b-32768 (balanced)
        Gemma7B = 3         // gemma-7b-it (compact)
    }

    public enum SamplingStrategy
    {
        Standard = 0,      // Temperature 0.8
        Creative = 1,      // Temperature 1.0
        Precise = 2        // Temperature 0.5
    }

    public enum RecommendationMode
    {
        Albums = 0,        // Album recommendations (default)
        Artists = 1        // Artist recommendations for comprehensive import
    }
}