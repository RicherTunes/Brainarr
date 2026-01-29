using System;
using System.Collections.Generic;
using NzbDrone.Core.ImportLists.Brainarr;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    internal static class ProviderModelNormalizer
    {
        public static string Normalize(AIProvider provider, string? value)
        {
            var trimmed = (value ?? string.Empty).Trim();

            return provider switch
            {
                AIProvider.OpenAI => NormalizeOpenAI(trimmed),
                AIProvider.Anthropic => NormalizeAnthropic(trimmed),
                AIProvider.Perplexity => NormalizePerplexity(trimmed),
                AIProvider.OpenRouter => NormalizeOpenRouter(trimmed),
                AIProvider.DeepSeek => NormalizeDeepSeek(trimmed),
                AIProvider.Gemini => NormalizeGemini(trimmed),
                AIProvider.Groq => NormalizeGroq(trimmed),
                AIProvider.ZaiGlm => NormalizeZaiGlm(trimmed),
                _ => trimmed
            };
        }

        private static string NormalizeOpenAI(string value)
        {
            if (string.IsNullOrEmpty(value)) return BrainarrConstants.DefaultOpenAIModel;
            if (_openAIModelAliases.TryGetValue(value, out var mapped)) return mapped;
            if (_openAIModelValues.Contains(value)) return value;

            var lower = value.ToLowerInvariant();
            if (lower == "gpt-4.1") return "GPT41";
            if (lower == "gpt-4.1-mini") return "GPT41_Mini";
            if (lower == "gpt-4.1-nano") return "GPT41_Nano";
            if (lower == "gpt-4o") return "GPT4o";
            if (lower == "gpt-4o-mini") return "GPT4o_Mini";
            if (lower == "o4-mini") return "O4_Mini";

            return value;
        }

        private static string NormalizeAnthropic(string value)
        {
            if (string.IsNullOrEmpty(value)) return BrainarrConstants.DefaultAnthropicModel;
            if (_anthropicModelAliases.TryGetValue(value, out var mapped)) return mapped;
            if (_anthropicModelValues.Contains(value)) return value;

            var condensed = value.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            if (condensed.Contains("sonnet4")) return "ClaudeSonnet4";
            if (condensed.Contains("claude37")) return "Claude37_Sonnet";
            if (condensed.Contains("haiku")) return "Claude35_Haiku";
            if (condensed.Contains("opus")) return "Claude3_Opus";

            return value;
        }

        private static string NormalizePerplexity(string value)
        {
            if (string.IsNullOrEmpty(value)) return BrainarrConstants.DefaultPerplexityModel;
            if (_perplexityModelAliases.TryGetValue(value, out var mapped)) return mapped;
            if (_perplexityModelValues.Contains(value)) return value;

            var lower = value.ToLowerInvariant();
            if (lower.Contains("sonar-pro")) return "Sonar_Pro";
            if (lower.Contains("deep-research") || lower.Contains("reasoning-pro")) return "Sonar_Reasoning_Pro";
            if (lower.Contains("reasoning")) return "Sonar_Reasoning";
            if (lower == "sonar") return "Sonar";

            return value;
        }

        private static string NormalizeOpenRouter(string value)
        {
            if (string.IsNullOrEmpty(value)) return BrainarrConstants.DefaultOpenRouterModel;
            if (_openRouterModelAliases.TryGetValue(value, out var mapped)) return mapped;
            if (_openRouterModelValues.Contains(value)) return value;

            var lower = value.ToLowerInvariant();
            if (lower.Contains("claude")) return "ClaudeSonnet4";
            if (lower.Contains("gpt-4.1")) return "GPT41_Mini";
            if (lower.Contains("gemini")) return "Gemini25_Flash";
            if (lower.Contains("llama-3.3")) return "Llama33_70B";
            if (lower.Contains("deepseek")) return "DeepSeekV3";
            if (lower.Contains("auto")) return "Auto";

            return value;
        }

        private static string NormalizeDeepSeek(string value)
        {
            if (string.IsNullOrEmpty(value)) return BrainarrConstants.DefaultDeepSeekModel;
            if (_deepSeekModelAliases.TryGetValue(value, out var mapped)) return mapped;
            if (_deepSeekModelValues.Contains(value)) return value;

            var lower = value.ToLowerInvariant();
            if (lower.Contains("reasoner")) return "DeepSeek_Reasoner";
            if (lower.Contains("search")) return "DeepSeek_Search";
            if (lower.Contains("r1")) return "DeepSeek_R1";

            return "DeepSeek_Chat";
        }

        private static string NormalizeGroq(string value)
        {
            if (string.IsNullOrEmpty(value)) return BrainarrConstants.DefaultGroqModel;
            if (_groqModelAliases.TryGetValue(value, out var mapped)) return mapped;
            if (_groqModelValues.Contains(value)) return value;

            var lower = value.ToLowerInvariant();
            if (lower.Contains("specdec")) return "Llama33_70B_SpecDec";
            if (lower.Contains("deepseek")) return "DeepSeek_R1_Distill_L70B";
            if (lower.Contains("8b")) return "Llama31_8B_Instant";

            return "Llama33_70B_Versatile";
        }

        private static string NormalizeGemini(string value)
        {
            if (string.IsNullOrEmpty(value)) return BrainarrConstants.DefaultGeminiModel;
            value = value.Trim();
            if (value.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring("models/".Length);
            }
            if (_geminiModelAliases.TryGetValue(value, out var mapped)) return mapped;
            if (_geminiModelValues.Contains(value)) return value;

            var lower = value.ToLowerInvariant();
            if (lower.StartsWith("gemini-2.5-pro")) return "Gemini_25_Pro";
            if (lower.StartsWith("gemini-2.5-flash-lite")) return "Gemini_25_Flash_Lite";
            if (lower.StartsWith("gemini-2.5-flash")) return "Gemini_25_Flash";
            if (lower.StartsWith("gemini-2.0-flash")) return "Gemini_20_Flash";
            if (lower.StartsWith("gemini-1.5-flash-8b")) return "Gemini_15_Flash_8B";
            if (lower.StartsWith("gemini-1.5-flash")) return "Gemini_15_Flash";
            if (lower.StartsWith("gemini-1.5-pro")) return "Gemini_15_Pro";
            if (lower.Contains("qwen") || lower.Contains("llama") || lower.Contains("mistral"))
            {
                return BrainarrConstants.DefaultGeminiModel;
            }

            return value;
        }

        private static readonly HashSet<string> _openAIModelValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GPT41",
            "GPT41_Mini",
            "GPT41_Nano",
            "GPT4o",
            "GPT4o_Mini",
            "O4_Mini"
        };

        private static readonly Dictionary<string, string> _openAIModelAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GPT4_Turbo"] = "GPT41",
            ["GPT35_Turbo"] = "GPT41_Mini",
            ["GPT4oMini"] = "GPT4o_Mini",
            ["gpt4o"] = "GPT4o",
            ["gpt4o_mini"] = "GPT4o_Mini"
        };

        private static readonly HashSet<string> _anthropicModelValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ClaudeSonnet4",
            "Claude37_Sonnet",
            "Claude35_Haiku",
            "Claude3_Opus"
        };

        private static readonly Dictionary<string, string> _anthropicModelAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Claude35_Sonnet"] = "ClaudeSonnet4",
            ["Claude3_Sonnet"] = "ClaudeSonnet4",
            ["Claude35_Haiku_Latest"] = "Claude35_Haiku",
            ["Claude3_Opus_Latest"] = "Claude3_Opus"
        };

        private static readonly HashSet<string> _perplexityModelValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Sonar_Pro",
            "Sonar_Reasoning_Pro",
            "Sonar_Reasoning",
            "Sonar"
        };

        private static readonly Dictionary<string, string> _perplexityModelAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sonar_Large"] = "Sonar_Pro",
            ["Sonar_Small"] = "Sonar",
            ["Llama31_70B_Instruct"] = "Sonar_Reasoning"
        };

        private static readonly HashSet<string> _openRouterModelValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Auto",
            "ClaudeSonnet4",
            "GPT41_Mini",
            "Gemini25_Flash",
            "Llama33_70B",
            "DeepSeekV3"
        };

        private static readonly Dictionary<string, string> _openRouterModelAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Claude35_Sonnet"] = "ClaudeSonnet4",
            ["GPT4o_Mini"] = "GPT41_Mini",
            ["Llama3_70B"] = "Llama33_70B",
            ["Gemini15_Flash"] = "Gemini25_Flash"
        };

        private static readonly HashSet<string> _deepSeekModelValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DeepSeek_Chat",
            "DeepSeek_Reasoner",
            "DeepSeek_R1",
            "DeepSeek_Search"
        };

        private static readonly Dictionary<string, string> _deepSeekModelAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DeepSeek_Coder"] = "DeepSeek_Search",
            ["DeepSeek_Math"] = "DeepSeek_Search",
            ["DeepSeek_V3"] = "DeepSeek_Chat"
        };

        private static readonly HashSet<string> _groqModelValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Llama33_70B_Versatile",
            "Llama33_70B_SpecDec",
            "DeepSeek_R1_Distill_L70B",
            "Llama31_8B_Instant"
        };

        private static readonly Dictionary<string, string> _groqModelAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mixtral_8x7B"] = "Llama31_8B_Instant",
            ["Llama31_70B_Versatile"] = "Llama33_70B_Versatile"
        };

        private static readonly HashSet<string> _geminiModelValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Gemini_25_Pro",
            "Gemini_25_Flash",
            "Gemini_25_Flash_Lite",
            "Gemini_20_Flash",
            "Gemini_15_Flash",
            "Gemini_15_Flash_8B",
            "Gemini_15_Pro"
        };

        private static readonly Dictionary<string, string> _geminiModelAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Gemini15_Flash"] = "Gemini_15_Flash",
            ["Gemini15_Flash_8B"] = "Gemini_15_Flash_8B",
            ["Gemini15_Pro"] = "Gemini_15_Pro",
            ["Gemini20_Flash"] = "Gemini_20_Flash",
            ["Gemini20_Flash_Exp"] = "Gemini_20_Flash",
            ["gemini-1.5-pro-latest"] = "Gemini_15_Pro",
            ["gemini-1.5-flash-latest"] = "Gemini_15_Flash",
            ["gemini-2.0-flash-exp"] = "Gemini_20_Flash",
            ["gemini-2.0-flash-lite"] = "Gemini_20_Flash",
            ["gemini-2.5-flash-latest"] = "Gemini_25_Flash",
            ["gemini-2.5-flash-lite-latest"] = "Gemini_25_Flash_Lite",
            ["gemini-2.5-pro-latest"] = "Gemini_25_Pro",
            ["gemini-2.5-pro-exp"] = "Gemini_25_Pro"
        };

        private static string NormalizeZaiGlm(string value)
        {
            if (string.IsNullOrEmpty(value)) return BrainarrConstants.DefaultZaiGlmModel;
            if (_zaiGlmModelAliases.TryGetValue(value, out var mapped)) return mapped;
            if (_zaiGlmModelValues.Contains(value)) return value;

            var lower = value.ToLowerInvariant();
            if (lower.StartsWith("glm-4.7-flash")) return "Glm47_Flash";
            if (lower.StartsWith("glm-4.7-flashx")) return "Glm47_FlashX";
            if (lower.StartsWith("glm-4.6v-flashx")) return "Glm46V_FlashX";
            if (lower.StartsWith("glm-4.5-air")) return "Glm45_Air";
            if (lower == "glm-4.5") return "Glm45";
            if (lower == "glm-4.6") return "Glm46";
            if (lower == "glm-4.7") return "Glm47";

            return value;
        }

        private static readonly HashSet<string> _zaiGlmModelValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Glm47_Flash",
            "Glm47_FlashX",
            "Glm46V_FlashX",
            "Glm45_Air",
            "Glm45",
            "Glm46",
            "Glm47"
        };

        private static readonly Dictionary<string, string> _zaiGlmModelAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["glm-4.7-flash"] = "Glm47_Flash",
            ["glm-4.7-flashx"] = "Glm47_FlashX",
            ["glm-4.6v-flashx"] = "Glm46V_FlashX",
            ["glm-4.5-air"] = "Glm45_Air",
            ["glm-4.5"] = "Glm45",
            ["glm-4.6"] = "Glm46",
            ["glm-4.7"] = "Glm47"
        };
    }
}
