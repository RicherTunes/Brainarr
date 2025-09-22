# Provider Support Matrix (v1.2.4)

Brainarr supports nine AI providers across local, cloud, and gateway scenarios. This matrix captures the defaults compiled into the plugin, the discovery mechanism the UI uses, and the latest manual verification status. For setup notes and cost guidance, see [docs/PROVIDER_GUIDE.md](PROVIDER_GUIDE.md).

> **Compatibility**
> * Brainarr 1.2.4 requires Lidarr 2.14.1.4716 or later on the nightly/plugins branch (Settings ➜ General ➜ Updates ➜ Branch = `nightly`).
> * Provider testing is manual. Verified entries below reflect smoke tests performed against Brainarr 1.2.4.

## Summary Table

| Provider | Type | Default Model | Recommended Models | Model Discovery | Tested (1.2.4) | Last Verified | Notes |
|---------|------|---------------|--------------------|-----------------|----------------|---------------|-------|
| Ollama | Local | `qwen2.5:latest` | `qwen2.5`, `llama3.2`, `mistral` | Auto-detect via `/api/tags` | Pending | — | Private, free; configure URL `http://localhost:11434`. |
| LM Studio | Local | `local-model` (placeholder) | Qwen 3, Llama 3 8B, Qwen 2.5 | Auto-detect via `/v1/models` | ✅ | 2025-09-12 | Verified with Qwen 3 via Local Server. Ensure Local Server is running before testing. |
| OpenAI | Cloud | `gpt-4o-mini` | `gpt-4o`, `gpt-4o-mini`, `gpt-3.5-turbo` | Static list (enum ➜ ID mapping) | Pending | — | Requires API key beginning with `sk-`. Higher-cost premium models available. |
| Anthropic | Cloud | `claude-3-5-haiku-latest` | `claude-3.5-sonnet`, `claude-3.5-haiku`, `claude-3-opus` | Static list (enum ➜ ID mapping) | Pending | — | Supports Thinking Mode and optional thinking budget tokens. |
| OpenRouter | Gateway | `anthropic/claude-3.5-sonnet` | `anthropic/claude-3.5-sonnet`, `openai/gpt-4o-mini`, `meta-llama/llama-3-70b`, `google/gemini-1.5-flash` | Static list (enum ➜ ID mapping) | Pending | — | One key unlocks 200+ models. Anthropic routes auto-switch to `:thinking` when enabled. |
| Perplexity | Cloud | `llama-3.1-sonar-large-128k-online` | `sonar-large`, `sonar-small`, `sonar-huge` | Static list (enum ➜ ID mapping) | ✅ | 2025-09-12 | Verified with Sonar Large. Web-enhanced responses; Pro plan includes monthly API credit. |
| DeepSeek | Cloud | `deepseek-chat` | `deepseek-chat`, `deepseek-reasoner` | Static list (enum ➜ ID mapping) | Pending | — | Ultra-low cost V3 models; supports reasoning traces. |
| Gemini | Cloud | `gemini-1.5-flash` | `gemini-1.5-flash`, `gemini-1.5-pro` | Static list (enum ➜ ID mapping) | ✅ | 2025-09-12 | Verified with Flash on free tier. Requires AI Studio key or Google Cloud project with Generative Language API enabled. |
| Groq | Cloud | `llama-3.1-70b-versatile` | `llama-3.1-70b-versatile`, `mixtral-8x7b` | Static list (enum ➜ ID mapping) | Pending | — | Focused on ultra-fast inference; generous free tier for testing. |

**Legend**

* *Auto-detect* – Brainarr queries the provider at test time to populate the model dropdown.
* *Static list* – The UI exposes enum values that map to provider-specific IDs at runtime.
* *Tested* – ✅ marks providers manually smoke-tested with Brainarr 1.2.4. Pending indicates community confirmation is welcome.

## Defaults and Sources

Default model IDs are declared in `Brainarr.Plugin/Configuration/BrainarrConstants.cs` and surfaced through the settings UI. Local providers retain their last-successful selection per provider.

* Local defaults
  * Ollama – `qwen2.5:latest`
  * LM Studio – `local-model` (placeholder resolved via Local Server)
* Cloud and gateway defaults
  * OpenAI – `gpt-4o-mini`
  * Anthropic – `claude-3-5-haiku-latest`
  * OpenRouter – `anthropic/claude-3.5-sonnet`
  * Perplexity – `llama-3.1-sonar-large-128k-online`
  * DeepSeek – `deepseek-chat`
  * Gemini – `gemini-1.5-flash`
  * Groq – `llama-3.1-70b-versatile`

## Model Overrides

* Dropdowns map enum kinds (e.g., `OpenAIModelKind`) to concrete provider IDs.
* The Advanced “Manual Model ID” field accepts explicit routes such as `anthropic/claude-3.5-sonnet:thinking` or `deepseek/deepseek-chat`.
* When Manual Model ID is set it overrides the dropdown selection; clear it to return to dropdown control.

## Thinking Mode Support

* Anthropic and OpenRouter support Brainarr’s Thinking Mode toggle. Auto/On enables extended thinking (and appends `:thinking` for Anthropic routes on OpenRouter).
* Optional “Thinking Budget Tokens” apply to direct Anthropic usage.

## Rate Limits and Timeouts

* Default rate limiter: 10 requests/minute (burst 5) per provider.
* Default HTTP timeout: 30 seconds, with safeguards up to 120 seconds for long-running local inference.
* Provider health checks observe a five-minute rolling window; repeated failures temporarily gate usage.

Tune these values under **Advanced Settings ➜ Rate Limiting & Timeouts** in the Brainarr UI.

## Contributing Verification Updates

If you validate a provider/model combination with Brainarr 1.2.4:

1. Capture the provider, model ID, and any notable settings (timeouts, iteration strategy).
2. Note your environment (OS, Docker vs bare metal, hardware specs for local models).
3. Submit a PR updating this matrix and [docs/PROVIDER_GUIDE.md](PROVIDER_GUIDE.md).
