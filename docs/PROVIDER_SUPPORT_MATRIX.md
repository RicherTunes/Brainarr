# Provider Support Matrix (v1.2.4)

This matrix summarizes provider characteristics, default models, and current testing status based on the codebase. For setup and tips, see docs/PROVIDER_GUIDE.md.

> Compatibility
> Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).
>
> Testing Status
> For v1.2.4, LM Studio (local), Gemini (cloud), and Perplexity (cloud) are verified. LM Studio with Qwen 3 was exercised at ~40–50k tokens (shared across GPU + CPU) on an NVIDIA RTX 3090. Other providers remain pending explicit verification.

## Summary Table

| Provider | Type | Default Model (UI ▸ raw ID) | Recommended Models | Model Discovery | Tested (1.2.4) | Last Verified | Notes |
|---------|------|-----------------------------|--------------------|-----------------|----------------|---------------|-------|
| Ollama | Local | `qwen2.5:latest` | `qwen2.5:latest`, `llama3.2`, `mistral` | Auto-detect via `/api/tags` | Pending verification | — | Private, free; set URL `http://localhost:11434` |
| LM Studio | Local | `local-model` ▸ auto-selected | Qwen 3 (tested), Llama 3 8B, Qwen 2.5 | Auto-detect via `/v1/models` | ✅ Tested | 2025-09-13 | Verified: Qwen 3 at ~40-50k tokens (shared GPU + CPU) on RTX 3090; start the Local Server |
| OpenAI | Cloud | `GPT41_Mini` ▸ `gpt-4.1-mini` | `GPT41`, `GPT4o`, `GPT4o_Mini` | Static list (ID mapping) | Pending verification | — | Cost-effective default |
| Anthropic | Cloud | `ClaudeSonnet4` ▸ `claude-sonnet-4-20250514` | `Claude37_Sonnet`, `Claude35_Haiku`, `Claude3_Opus` | Static list (ID mapping) | Pending verification | — | Thinking Mode supported |
| OpenRouter | Gateway | `Auto` ▸ `openrouter/auto` | `ClaudeSonnet4`, `GPT41_Mini`, `Gemini25_Flash`, `Llama33_70B` | Static list (ID mapping) | Pending verification | — | One key, many models; `:thinking` auto for Anthropic |
| Perplexity | Cloud | `Sonar_Pro` ▸ `sonar-pro` | `Sonar_Reasoning_Pro`, `Sonar_Reasoning`, `Sonar` | Static list (ID mapping) | ✅ Tested | 2025-09-13 | Web-enabled Sonar models; Perplexity Pro includes $5/month API credit |
| DeepSeek | Cloud | `DeepSeek_Chat` ▸ `deepseek-chat` | `DeepSeek_Chat`, `DeepSeek_Reasoner`, `DeepSeek_R1` | Static list (ID mapping) | Pending verification | — | Budget-friendly DeepSeek V3 |
| Gemini | Cloud | `Gemini_25_Flash` ▸ `gemini-2.5-flash` | `Gemini_25_Flash`, `Gemini_25_Pro`, `Gemini_20_Flash` | Static list (ID mapping) | ✅ Tested | 2025-09-13 | Free tier available; verified on AI Studio key |
| Groq | Cloud | `Llama33_70B_Versatile` ▸ `llama-3.3-70b-versatile` | `Llama31_8B_Instant`, `Llama33_70B_SpecDec`, `DeepSeek_R1_Distill_L70B` | Static list (ID mapping) | Pending verification | — | Very fast inference |

Legend:

- Model Discovery “Auto‑detect” uses live endpoints for local providers.
- “Static list (ID mapping)” uses UI enums mapped to provider IDs at runtime.

## Defaults and Sources (Code)

- Local defaults:
  - Ollama: `qwen2.5:latest`
  - LM Studio: `local-model` (placeholder; real selection comes from Local Server)
- Cloud/gateway defaults (UI label ▸ raw ID):
  - OpenAI: `GPT41_Mini` ▸ `gpt-4.1-mini`
  - Anthropic: `ClaudeSonnet4` ▸ `claude-sonnet-4-20250514`
  - OpenRouter: `Auto` ▸ `openrouter/auto` (test route fallback ▸ `gpt-4.1-mini`)
  - Perplexity: `Sonar_Pro` ▸ `sonar-pro`
  - DeepSeek: `DeepSeek_Chat` ▸ `deepseek-chat`
  - Gemini: `Gemini_25_Flash` ▸ `gemini-2.5-flash`
  - Groq: `Llama33_70B_Versatile` ▸ `llama-3.3-70b-versatile`

Defaults reference: Brainarr.Plugin/Configuration/Constants.cs

## Model IDs and Overrides

- UI dropdowns map enum kinds (e.g., `OpenAIModelKind`) to provider‑specific IDs at runtime.
- Advanced override: `ManualModelId` lets you enter an exact model route (e.g., `openrouter/anthropic/claude-3.5-sonnet:thinking`). When set, it overrides the dropdown.

## Thinking Mode (Anthropic/OpenRouter)

- Off (default): never request extended thinking.
- Auto/On: enables Anthropic “thinking”; for OpenRouter + Anthropic, `:thinking` route is used automatically.
- Optional budget tokens can be set for direct Anthropic requests.

## Rate Limits and Timeouts (Defaults)

- Rate limit (per provider): 10 req/min, burst 5
- Timeouts: 30s request default; 120s maximum
- Health check window: 5 minutes; unhealthy threshold ~50% failures

These are safe defaults; tune in Advanced Settings.

## Contributing Testing Results

Current LM Studio, Gemini, and Perplexity are tested for 1.2.4. Other providers are pending verification. If you confirm a provider configuration works in your environment:

- Open a PR updating this matrix with “Tested” and briefly note the model and any relevant limits.
- Include your environment: OS, network, and provider quotas where relevant.
