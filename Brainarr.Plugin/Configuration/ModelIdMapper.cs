using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    public static class ModelIdMapper
    {
        public static string ToRawId(string provider, string label)
        {
            var p = (provider ?? string.Empty).Trim().ToLowerInvariant();

            // Z.AI endpoints reject model="default"/"" with [1210] Invalid API parameter. The
            // orchestrator passes the generic "Default" sentinel when the model dropdown is unset
            // (which can happen when Lidarr's settings modal doesn't refresh the computed model
            // list). Resolve empty / "default" to the endpoint's flagship BEFORE the generic
            // empty-passthrough below: Coding Plan → glm-5.1 (what subscribers pay for); PaaS →
            // glm-4.5-air (broadly available). Mirrors ProviderRegistry.MapZaiCodingModel.
            if (p == "zaicoding" || p == "zaiglm")
            {
                var trimmed = label?.Trim() ?? string.Empty;
                if (trimmed.Length == 0 || trimmed.Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    return p == "zaicoding" ? "glm-5.1" : "glm-4.5-air";
                }
            }

            if (string.IsNullOrWhiteSpace(label)) return label;
            var v = label.Trim();
            var lower = v.Replace('_', '-').ToLowerInvariant();
            switch (p)
            {
                case "openai":
                    return v switch
                    {
                        // GPT-5 family — current as of May 2026.
                        "GPT5" => "gpt-5",
                        "GPT5_Mini" => "gpt-5-mini",
                        "GPT5_Nano" => "gpt-5-nano",
                        "O3_Pro" => "o3-pro",
                        "O1_Preview" => "o1-preview",
                        "O1_Mini" => "o1-mini",
                        // GPT-4.1 / 4o still available; kept for back-compat.
                        "GPT41" => "gpt-4.1",
                        "GPT41_Mini" => "gpt-4.1-mini",
                        "GPT41_Nano" => "gpt-4.1-nano",
                        "GPT4o" => "gpt-4o",
                        "GPT4o_Mini" => "gpt-4o-mini",
                        "O4_Mini" => "o4-mini",
                        // Legacy mappings — fold older aliases onto current model.
                        "GPT4_Turbo" => "gpt-4.1",
                        "GPT35_Turbo" => "gpt-4.1-mini",
                        // Pass-through for users who type the raw API id directly.
                        _ when lower.StartsWith("gpt-5") => lower,
                        _ when lower == "gpt-4.1" => "gpt-4.1",
                        _ when lower == "gpt-4.1-mini" => "gpt-4.1-mini",
                        _ when lower == "gpt-4.1-nano" => "gpt-4.1-nano",
                        _ when lower == "o4-mini" => "o4-mini",
                        _ when lower == "o3-pro" => "o3-pro",
                        _ when lower == "o1-preview" => "o1-preview",
                        _ when lower == "o1-mini" => "o1-mini",
                        _ when lower == "gpt-4o-mini" => "gpt-4o-mini",
                        _ when lower == "gpt-4o" => "gpt-4o",
                        _ => v
                    };
                case "perplexity":
                    if (lower is "sonar-pro" or "sonar-pro-128k" or "sonar-pro-online") return "sonar-pro";
                    if (lower == "sonar-reasoning-pro") return "sonar-reasoning-pro";
                    if (lower == "sonar-deep-research") return "sonar-deep-research";
                    if (lower == "sonar-reasoning") return "sonar-reasoning";
                    if (lower == "sonar") return "sonar";
                    if (lower.StartsWith("llama-3.1-sonar-large")) return "llama-3.1-sonar-large-128k-online";
                    if (lower.StartsWith("llama-3.1-sonar-small")) return "llama-3.1-sonar-small-128k-online";
                    return v switch
                    {
                        "Sonar_Pro" => "sonar-pro",
                        "Sonar_Reasoning_Pro" => "sonar-reasoning-pro",
                        "Sonar_Reasoning" => "sonar-reasoning",
                        "Sonar_Deep_Research" => "sonar-deep-research",
                        "Sonar" => "sonar",
                        "Sonar_Large" => "llama-3.1-sonar-large-128k-online",
                        "Sonar_Small" => "llama-3.1-sonar-small-128k-online",
                        "Llama31_70B_Instruct" => "llama-3.1-70b-instruct",
                        _ => v
                    };
                case "anthropic":
                    return v switch
                    {
                        // May 2026 lineup — flagship Opus 4.7, balanced Sonnet 4.6, fast Haiku 4.5.
                        "ClaudeOpus47" => "claude-opus-4-7",
                        "ClaudeSonnet46" => "claude-sonnet-4-6",
                        "ClaudeHaiku45" => "claude-haiku-4-5-20251001",
                        // Earlier-gen models still served by Anthropic; kept for back-compat.
                        "ClaudeSonnet4" => "claude-sonnet-4-20250514",
                        "Claude37_Sonnet" => "claude-3-7-sonnet-20250219",
                        "Claude35_Haiku" => "claude-3-5-haiku-20241022",
                        "Claude3_Opus" => "claude-3-opus-latest",
                        "Claude35_Sonnet" => "claude-3-5-sonnet-latest",
                        // Pass-through for users who type the raw id directly.
                        _ when lower == "claude-opus-4-7" => "claude-opus-4-7",
                        _ when lower == "claude-sonnet-4-6" => "claude-sonnet-4-6",
                        _ when lower == "claude-haiku-4-5" => "claude-haiku-4-5-20251001",
                        _ when lower == "claude-sonnet-4" => "claude-sonnet-4-20250514",
                        _ when lower == "claude-3-5-sonnet" => "claude-3-5-sonnet-latest",
                        _ when lower == "claude-3-opus" => "claude-3-opus-latest",

                        _ => v
                    };
                case "openrouter":
                    return v switch
                    {
                        "Auto" => "openrouter/auto",
                        // May 2026 popular routes.
                        "ClaudeSonnet46" => "anthropic/claude-sonnet-4-6",
                        "ClaudeOpus47" => "anthropic/claude-opus-4-7",
                        "GPT5" => "openai/gpt-5",
                        "GPT5_Mini" => "openai/gpt-5-mini",
                        "Gemini3_Pro" => "google/gemini-3.1-pro-preview",
                        "Gemini3_Flash" => "google/gemini-3-flash-preview",
                        "Llama4_Scout" => "meta-llama/llama-4-scout",
                        "DeepSeekV4" => "deepseek/deepseek-v4-pro",
                        // Legacy values still routable via OpenRouter.
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
                        // V4 family — current as of May 2026.
                        "DeepSeek_V4_Pro" => "deepseek-v4-pro",
                        "DeepSeek_V4_Flash" => "deepseek-v4-flash",
                        // Legacy ids still served by DeepSeek (deprecate after 2026-07-24).
                        "DeepSeek_Chat" => "deepseek-chat",
                        "DeepSeek_Reasoner" => "deepseek-reasoner",
                        "DeepSeek_R1" => "deepseek-r1",
                        "DeepSeek_Search" => "deepseek-search",
                        _ when lower == "deepseek-v4-pro" => "deepseek-v4-pro",
                        _ when lower == "deepseek-v4-flash" => "deepseek-v4-flash",
                        _ when lower == "deepseek-v3" => "deepseek-chat",
                        _ => v
                    };
                case "gemini":
                    if (string.IsNullOrWhiteSpace(v)) return BrainarrConstants.DefaultGeminiModel;

                    var trimmed = v.Trim();
                    if (trimmed.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                    {
                        trimmed = trimmed.Substring("models/".Length);
                    }

                    if (trimmed.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
                    {
                        return trimmed;
                    }

                    var geminiLower = trimmed.Replace('_', '-').ToLowerInvariant();

                    return trimmed switch
                    {
                        // 3.x — current as of May 2026.
                        "Gemini_3_1_Pro" => "gemini-3.1-pro-preview",
                        "Gemini_3_Flash" => "gemini-3-flash-preview",
                        "Gemini_3_1_Flash_Lite" => "gemini-3.1-flash-lite",
                        // 2.5 family still stable.
                        "Gemini_25_Pro" => "gemini-2.5-pro",
                        "Gemini_25_Flash" => "gemini-2.5-flash",
                        "Gemini_25_Flash_Lite" => "gemini-2.5-flash-lite",
                        "Gemini_20_Flash" => "gemini-2.0-flash",
                        // 1.5 retained for back-compat.
                        "Gemini_15_Flash" => "gemini-1.5-flash",
                        "Gemini_15_Flash_8B" => "gemini-1.5-flash-8b",
                        "Gemini_15_Pro" => "gemini-1.5-pro",
                        "Gemini15_Flash" => "gemini-1.5-flash",
                        "Gemini15_Flash_8B" => "gemini-1.5-flash-8b",
                        "Gemini15_Pro" => "gemini-1.5-pro",
                        "Gemini_20_Flash_Exp" => "gemini-2.0-flash-exp",
                        "Gemini20_Flash" => "gemini-2.0-flash",
                        _ when geminiLower.StartsWith("gemini-3.1-pro") => "gemini-3.1-pro-preview",
                        _ when geminiLower.StartsWith("gemini-3-flash") => "gemini-3-flash-preview",
                        _ when geminiLower.StartsWith("gemini-3.1-flash-lite") => "gemini-3.1-flash-lite",
                        _ when geminiLower.StartsWith("gemini-2.5-pro") => "gemini-2.5-pro",
                        _ when geminiLower.StartsWith("gemini-2.5-flash-lite") => "gemini-2.5-flash-lite",
                        _ when geminiLower.StartsWith("gemini-2.5-flash") => "gemini-2.5-flash",
                        _ => trimmed
                    };
                case "groq":
                    return v switch
                    {
                        // May 2026 catalog.
                        "Llama33_70B_Versatile" => "llama-3.3-70b-versatile",
                        "Llama33_70B_SpecDec" => "llama-3.3-70b-specdec",
                        "Llama31_8B_Instant" => "llama-3.1-8b-instant",
                        "OpenAi_Gpt_Oss_120B" => "openai/gpt-oss-120b",
                        "Groq_Compound" => "groq/compound",
                        "Qwen3_32B" => "qwen3-32b-preview",
                        "DeepSeek_R1_Distill_L70B" => "deepseek-r1-distill-llama-70b",
                        // Legacy / passthrough.
                        "Llama31_70B_Versatile" => "llama-3.1-70b-versatile",
                        "Mixtral_8x7B" => "mixtral-8x7b-32768",
                        _ => v
                    };
                case "zaiglm":
                case "zaicoding":
                    // Z.AI / Zhipu GLM canonical-id mapping. Catalog sourced from
                    // z.ai/manage-apikey/rate-limits. Same mapping applies to both the
                    // PaaS endpoint (ZaiGlm provider, OpenAI wire format) and the Coding
                    // Plan endpoint (ZaiCoding provider, Anthropic wire format) — Z.AI
                    // accepts identical model_id strings on both paths; what differs is
                    // which models a given account / plan is *entitled* to serve.
                    //
                    // The empty-input default differs by provider so we never silently
                    // pick a model the user's tier may not cover: PaaS users see
                    // glm-4.5-air (broadly available, cheap); Coding Plan users see
                    // glm-5.1 (their flagship — what they're paying for).
                    return v switch
                    {
                        "GLM_5_1" => "glm-5.1",
                        "GLM_5" => "glm-5",
                        "GLM_5_Turbo" => "glm-5-turbo",
                        "GLM_4_7" => "glm-4.7",
                        "GLM_4_7_Flash" => "glm-4.7-flash",
                        "GLM_4_7_FlashX" => "glm-4.7-flashx",
                        "GLM_4_6" => "glm-4.6",
                        "GLM_4_5" => "glm-4.5",
                        "GLM_4_5_Air" => "glm-4.5-air",
                        "GLM_4_5_AirX" => "glm-4.5-airx",
                        "GLM_4_5_Flash" => "glm-4.5-flash",
                        "GLM_4_Plus" => "glm-4-plus",
                        "GLM_4_32B" => "glm-4-32b-0414-128k",
                        // accept already-raw ids unchanged so users can paste them
                        _ when lower.StartsWith("glm-") => lower,
                        _ => v
                    };
                default:
                    return v;
            }
        }
    }
}
