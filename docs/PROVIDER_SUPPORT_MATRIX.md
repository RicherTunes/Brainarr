# Provider Support Matrix (v1.2.2)

This matrix summarizes provider characteristics, default models, and current testing status based on the codebase. For setup and tips, see docs/PROVIDER_GUIDE.md.

> Compatibility
> Requires Lidarr 2.14.1.4716+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).
>
> Testing Status
> For v1.2.2, only LM Studio with Qwen 3 has been verified, at ~40–50k tokens (shared across GPU + CPU) on an NVIDIA RTX 3090. All other providers are pending verification.

## Summary Table

| Provider | Type | Default Model | Recommended Models | Model Discovery | Tested (1.2.2) | Last Verified | Notes |
|---------|------|---------------|--------------------|-----------------|----------------|---------------|-------|
| Ollama | Local | `qwen2.5:latest` | `qwen2.5`, `llama3.2`, `mistral` | Auto‑detect via `/api/tags` | Pending verification | — | Private, free; set URL `http://localhost:11434` |
| LM Studio | Local | `local-model` (placeholder) | Qwen 3 (tested), Llama 3 8B, Qwen 2.5 | Auto-detect via `/v1/models` | Tested | 2025-09-06 | Verified: Qwen 3 at ~40-50k tokens (shared GPU + CPU) on RTX 3090; load model in LM Studio > Local Server |
| OpenAI | Cloud | `gpt-4o-mini` | `gpt-4o`, `gpt-4o-mini`, `gpt-3.5-turbo` | Static list (ID mapping) | Pending verification | — | Cost‑effective default |
| Anthropic | Cloud | `claude-3-5-haiku-latest` | `claude-3.5-sonnet`, `claude-3.5-haiku`, `claude-3-opus` | Static list (ID mapping) | Pending verification | — | Thinking Mode supported |
| OpenRouter | Gateway | `anthropic/claude-3.5-sonnet` | `anthropic/claude-3.5-sonnet`, `openai/gpt-4o-mini`, `meta-llama/llama-3-70b`, `google/gemini-1.5-flash` | Static list (ID mapping) | Pending verification | — | One key, many models; `:thinking` auto for Anthropic |
| Perplexity | Cloud | `llama-3.1-sonar-large-128k-online` | `sonar-large`, `sonar-small`, `sonar-huge` | Static list (ID mapping) | Pending verification | — | Web‑enabled Sonar models |
| DeepSeek | Cloud | `deepseek-chat` | `deepseek-chat`, `deepseek-reasoner` | Static list (ID mapping) | Pending verification | — | Budget‑friendly (V3) |
| Gemini | Cloud | `gemini-1.5-flash` | `gemini-1.5-flash`, `gemini-1.5-pro` | Static list (ID mapping) | Pending verification | — | Free tier available |
| Groq | Cloud | `llama-3.1-70b-versatile` | `llama-3.1-70b-versatile`, `mixtral-8x7b` | Static list (ID mapping) | Pending verification | — | Very fast inference |

Legend:

- Model Discovery “Auto‑detect” uses live endpoints for local providers.
- “Static list (ID mapping)” uses UI enums mapped to provider IDs at runtime.

## Defaults and Sources (Code)

- Local defaults:
  - Ollama: `qwen2.5:latest`
  - LM Studio: `local-model` (placeholder; real selection comes from Local Server)
- Cloud/gateway defaults:
  - OpenAI: `gpt-4o-mini`
  - Anthropic: `claude-3-5-haiku-latest`
  - OpenRouter: `anthropic/claude-3.5-sonnet` (test route fallback: `gpt-4o-mini`)
  - Perplexity: `llama-3.1-sonar-large-128k-online`
  - DeepSeek: `deepseek-chat`
  - Gemini: `gemini-1.5-flash`
  - Groq: `llama-3.1-70b-versatile`

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

Current LM Studio is tested for 1.2.2. Other providers are pending verification. If you confirm a provider configuration works in your environment:

- Open a PR updating this matrix with “Tested” and briefly note the model and any relevant limits.
- Include your environment: OS, network, and provider quotas where relevant.
