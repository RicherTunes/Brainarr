using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    public static class ModelIdMapper
    {
        public static string ToRawId(string provider, string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return label;
            var p = (provider ?? string.Empty).Trim().ToLowerInvariant();
            switch (p)
            {
                case "openai":
                    return label switch
                    {
                        "GPT4o_Mini" => "gpt-4o-mini",
                        "GPT4o" => "gpt-4o",
                        "GPT4_Turbo" => "gpt-4-turbo",
                        "GPT35_Turbo" => "gpt-3.5-turbo",
                        _ => label
                    };
                case "perplexity":
                    return label switch
                    {
                        "Sonar_Large" => "llama-3.1-sonar-large-128k-online",
                        "Sonar_Small" => "llama-3.1-sonar-small-128k-online",
                        "Llama31_70B_Instruct" => "llama-3.1-70b-instruct",
                        _ => label
                    };
                case "anthropic":
                    return label switch
                    {
                        "Claude35_Haiku" => "claude-3-5-haiku-latest",
                        "Claude35_Sonnet" => "claude-3-5-sonnet-latest",
                        "Claude3_Opus" => "claude-3-opus-latest",
                        _ => label
                    };
                case "openrouter":
                    return label switch
                    {
                        "Claude35_Sonnet" => "anthropic/claude-3.5-sonnet",
                        "GPT4o_Mini" => "openai/gpt-4o-mini",
                        "Llama3_70B" => "meta-llama/llama-3.1-70b-instruct",
                        "Gemini15_Flash" => "google/gemini-1.5-flash",
                        _ => label
                    };
                case "deepseek":
                    return label switch
                    {
                        "DeepSeek_Chat" => "deepseek-chat",
                        "DeepSeek_Reasoner" => "deepseek-reasoner",
                        _ => label
                    };
                case "gemini":
                    return label switch
                    {
                        "Gemini_15_Flash" => "gemini-1.5-flash",
                        "Gemini_15_Pro" => "gemini-1.5-pro",
                        _ => label
                    };
                case "groq":
                    return label switch
                    {
                        "Llama31_70B_Versatile" => "llama-3.1-70b-versatile",
                        "Mixtral_8x7B" => "mixtral-8x7b-32768",
                        _ => label
                    };
                default:
                    return label;
            }
        }
    }
}

