using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    public static class ModelIdMapper
    {
        public static string ToRawId(string provider, string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return label;
            var p = (provider ?? string.Empty).Trim().ToLowerInvariant();
            var v = label.Trim();
            var lower = v.Replace('_', '-').ToLowerInvariant();
            switch (p)
            {
                case "openai":
                    return v switch
                    {
                        "GPT41" => "gpt-4.1",
                        "GPT41_Mini" => "gpt-4.1-mini",
                        "GPT41_Nano" => "gpt-4.1-nano",
                        "GPT4o" => "gpt-4o",
                        "GPT4o_Mini" => "gpt-4o-mini",
                        "O4_Mini" => "o4-mini",
                        "GPT4_Turbo" => "gpt-4.1",
                        "GPT35_Turbo" => "gpt-4.1-mini",
                        _ when lower == "gpt-4.1" => "gpt-4.1",
                        _ when lower == "gpt-4.1-mini" => "gpt-4.1-mini",
                        _ when lower == "gpt-4.1-nano" => "gpt-4.1-nano",
                        _ when lower == "o4-mini" => "o4-mini",
                        _ when lower == "gpt-4o-mini" => "gpt-4o-mini",
                        _ when lower == "gpt-4o" => "gpt-4o",
                        _ => v
                    };
                case "perplexity":
                    if (lower is "sonar-pro" or "sonar-pro-128k" or "sonar-pro-online") return "sonar-pro";
                    if (lower is "sonar-reasoning-pro" or "sonar-deep-research") return "sonar-reasoning-pro";
                    if (lower == "sonar-reasoning") return "sonar-reasoning";
                    if (lower == "sonar") return "sonar";
                    if (lower.StartsWith("llama-3.1-sonar-large")) return "llama-3.1-sonar-large-128k-online";
                    if (lower.StartsWith("llama-3.1-sonar-small")) return "llama-3.1-sonar-small-128k-online";
                    return v switch
                    {
                        "Sonar_Pro" => "sonar-pro",
                        "Sonar_Reasoning_Pro" => "sonar-reasoning-pro",
                        "Sonar_Reasoning" => "sonar-reasoning",
                        "Sonar" => "sonar",
                        "Sonar_Large" => "llama-3.1-sonar-large-128k-online",
                        "Sonar_Small" => "llama-3.1-sonar-small-128k-online",
                        "Llama31_70B_Instruct" => "llama-3.1-70b-instruct",
                        _ => v
                    };
                case "anthropic":
                    return v switch
                    {
                        "ClaudeSonnet4" => "claude-sonnet-4-20250514",
                        "Claude37_Sonnet" => "claude-3-7-sonnet-20250219",
                        "Claude35_Haiku" => "claude-3-5-haiku-20241022",
                        "Claude3_Opus" => "claude-3-opus-latest",
                        "Claude35_Sonnet" => "claude-3-5-sonnet-latest",
                        _ when lower == "claude-sonnet-4" => "claude-sonnet-4-20250514",
                        _ when lower == "claude-3-5-sonnet" => "claude-3-5-sonnet-latest",
                        _ when lower == "claude-3-opus" => "claude-3-opus-latest",

                        _ => v
                    };
                case "openrouter":
                    return v switch
                    {
                        "Auto" => "openrouter/auto",
                        "ClaudeSonnet4" => "anthropic/claude-sonnet-4-20250514",
                        "GPT41_Mini" => "openai/gpt-4.1-mini",
                        "Gemini25_Flash" => "google/gemini-2.5-flash",
                        "Llama33_70B" => "meta-llama/llama-3.3-70b-versatile",
                        "DeepSeekV3" => "deepseek/deepseek-chat",
                        "Claude35_Sonnet" => "anthropic/claude-3.5-sonnet",
                        "GPT4o_Mini" => "openai/gpt-4o-mini",
                        _ => v
                    };
                case "deepseek":
                    return v switch
                    {
                        "DeepSeek_Chat" => "deepseek-chat",
                        "DeepSeek_Reasoner" => "deepseek-reasoner",
                        "DeepSeek_R1" => "deepseek-r1",
                        "DeepSeek_Search" => "deepseek-search",
                        _ when lower == "deepseek-v3" => "deepseek-chat",
                        _ => v
                    };
                case "gemini":
                    return v switch
                    {
                        "Gemini_25_Pro" => "gemini-2.5-pro-latest",
                        "Gemini_25_Flash" => "gemini-2.5-flash",
                        "Gemini_25_Flash_Lite" => "gemini-2.5-flash-lite",
                        "Gemini_20_Flash" => "gemini-2.0-flash",
                        "Gemini_15_Flash" => "gemini-1.5-flash",
                        "Gemini_15_Flash_8B" => "gemini-1.5-flash-8b",
                        "Gemini_15_Pro" => "gemini-1.5-pro",
                        "Gemini15_Flash" => "gemini-1.5-flash",
                        "Gemini15_Flash_8B" => "gemini-1.5-flash-8b",
                        "Gemini15_Pro" => "gemini-1.5-pro",
                        "Gemini_20_Flash_Exp" => "gemini-2.0-flash-exp",
                        "Gemini20_Flash" => "gemini-2.0-flash",
                        _ when lower.StartsWith("gemini-2.5-pro") => "gemini-2.5-pro-latest",
                        _ when lower.StartsWith("gemini-2.5-flash-lite") => "gemini-2.5-flash-lite",
                        _ when lower.StartsWith("gemini-2.5-flash") => "gemini-2.5-flash",
                        _ => v
                    };
                case "groq":
                    return v switch
                    {
                        "Llama33_70B_Versatile" => "llama-3.3-70b-versatile",
                        "Llama33_70B_SpecDec" => "llama-3.3-70b-specdec",
                        "DeepSeek_R1_Distill_L70B" => "deepseek-r1-distill-llama-70b",
                        "Llama31_8B_Instant" => "llama-3.1-8b-instant",
                        "Llama31_70B_Versatile" => "llama-3.1-70b-versatile",
                        "Mixtral_8x7B" => "mixtral-8x7b-32768",
                        _ => v
                    };
                default:
                    return v;
            }
        }
    }
}
