# Provider Support Matrix (v1.2.4)

Brainarr 1.2.4 continues to ship nine providers spanning local privacy-first options and premium cloud APIs. Use this matrix to confirm which combinations have been smoke-tested and how model defaults are resolved at runtime.

> **Compatibility**
> Requires Lidarr 2.14.1.4716+ on the nightly/plugins branch (Settings ➜ General ➜ Updates ➜ Branch = `nightly`).
>
> **Testing snapshot**
> Verified against Brainarr 1.2.4: **LM Studio**, **Google Gemini**, and **Perplexity**. Other providers remain available but unverified until explicit coverage is added.

## Summary Table

| Provider      | Type    | Default Model (UI)      | Discovery Source                  | Tested (1.2.4) | Last Verified | Notes |
|---------------|---------|-------------------------|-----------------------------------|----------------|---------------|-------|
| Ollama        | Local   | `qwen2.5:latest`        | Live `/api/tags` probe            | ❓ Pending      | —             | Auto-detects installed models. |
| LM Studio     | Local   | `local-model` (placeholder) | Live `/v1/models` listing      | ✅ Yes         | 2025-09-13    | Verified with Qwen 3 via Local Server. |
| OpenAI        | Cloud   | `GPT41_Mini`             | Static enum ➜ ID mapper           | ❓ Pending      | —             | Supports JSON-mode fallbacks. |
| Anthropic     | Cloud   | `ClaudeSonnet4`          | Static enum ➜ ID mapper           | ❓ Pending      | —             | Extended thinking via Advanced settings. |
| OpenRouter    | Gateway | `Auto` (route resolver)  | Static enum ➜ ID mapper + thinking | ❓ Pending      | —             | Anthropic routes auto-switch to `:thinking` when enabled. |
| Perplexity    | Cloud   | `Sonar_Pro`              | Static enum ➜ ID mapper           | ✅ Yes         | 2025-09-13    | Verified with `llama-3.1-sonar-large-128k-online`. |
| DeepSeek      | Cloud   | `DeepSeek_Chat`          | Static enum ➜ ID mapper           | ❓ Pending      | —             | Budget friendly defaults. |
| Gemini        | Cloud   | `Gemini_25_Flash`        | Static enum ➜ ID mapper           | ✅ Yes         | 2025-09-13    | Verified with AI Studio key (Flash & Pro). |
| Groq          | Cloud   | `Llama33_70B_Versatile`  | Static enum ➜ ID mapper           | ❓ Pending      | —             | Ultra-fast inference. |

Legend: **Live probes** use provider APIs to auto-detect models; **Static enum ➜ ID mapper** refers to UI enums mapped to provider-specific IDs inside the plugin.

## Defaults and Code References

- Default models are declared in [`Configuration/Constants.cs`](../Brainarr.Plugin/Configuration/Constants.cs).
- Cloud provider dropdowns map enum values to raw IDs in [`Configuration/ModelIdMapper.cs`](../Brainarr.Plugin/Configuration/ModelIdMapper.cs) and provider-specific normalizers.
- Manual overrides (`ManualModelId`) remain available for advanced routes such as OpenRouter custom paths.

## External Model Registry (Opt-In)

Brainarr 1.2.4 introduces an optional JSON-driven registry:

- Toggle with `BRAINARR_USE_EXTERNAL_MODEL_REGISTRY=true` before Lidarr launches. Optionally set `BRAINARR_MODEL_REGISTRY_URL` to point at a remote registry; otherwise the embedded [`docs/models.example.json`](models.example.json) + cached copy are used.
- Loader behaviour is defined in [`Services/Registry/ModelRegistryLoader.cs`](../Brainarr.Plugin/Services/Registry/ModelRegistryLoader.cs): it honours `ETag` headers, caches to `%TEMP%/Brainarr/ModelRegistry/registry.json`, and falls back to embedded assets if the network is unavailable.
- Provider descriptors may define `auth.env`. When present and the named environment variable exists, Brainarr will temporarily inject that value as the provider API key for validation and fetch operations.
- Registry integration is layered on top of the existing provider factory. If loading fails, the plugin quietly reverts to the built-in defaults to keep recommendation runs healthy.

## Thinking Mode and Iterative Top-Up

- Anthropic/OpenRouter “Thinking Mode” controls are surfaced in the UI. When enabled, OpenRouter Anthropic routes automatically append `:thinking`; direct Anthropic calls pass `thinking` and optional `budget_tokens` values. Implementation lives in [`BrainarrSettings`](../Brainarr.Plugin/BrainarrSettings.cs) and provider classes under `Services/Providers`.
- Iterative recommendation top-up is on by default for Ollama/LM Studio and can be toggled for cloud providers via Advanced settings (`EnableIterativeRefinement`). Behaviour is coordinated by [`IterativeRecommendationStrategy`](../Brainarr.Plugin/Services/IterativeRecommendationStrategy.cs).

## Contributing Verification Data

If you validate a provider/model combination:

1. Capture the provider, model ID, environment (OS, API tier), and any limits encountered.
2. Update this matrix (or open an issue) with a short note and date.
3. Include log excerpts where helpful—especially around health checks or rate limiting—so future readers can reproduce your setup.

This keeps Brainarr’s documentation grounded in real-world usage while signalling which combinations still need coverage.
