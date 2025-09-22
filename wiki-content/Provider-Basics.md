# Provider Basics

> Compatibility
> Requires Lidarr 2.14.1.4716+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).

This page helps you pick a provider, understand configuration URLs, and set API keys.

## Choosing a Provider

Brainarr supports local (private) and cloud providers. A quick guide:

- Local (Private): Ollama, LM Studio — zero cost, data stays on your machine
- Gateway: OpenRouter — one key to access 200+ models
- Budget: DeepSeek, Gemini — low cost or free tiers
- Fast: Groq — very fast response times
- Premium: OpenAI, Anthropic — best quality models

Tips:

- Start local if privacy matters, or Gemini (free) for a no-cost cloud start.
- OpenRouter is great for trying many models with a single key.
- DeepSeek V3 provides strong quality at low cost.

## Configuration URL

Only used for local providers:

- Ollama URL: `http://localhost:11434`
- LM Studio URL: `http://localhost:1234`

For cloud providers (OpenAI, Anthropic, Perplexity, OpenRouter, DeepSeek, Gemini, Groq), the Configuration URL shows “N/A – API Key based provider.”

## API Keys

Cloud providers require API keys. Enter them in settings after selecting the provider:

- OpenAI: <https://platform.openai.com/api-keys>
- Anthropic: <https://console.anthropic.com>
- OpenRouter: <https://openrouter.ai/keys>
- Perplexity: <https://perplexity.ai/settings/api>
- DeepSeek: <https://platform.deepseek.com>
- Gemini: <https://aistudio.google.com/apikey>
- Groq: <https://console.groq.com/keys>

Security tips:

- Never share API keys in screenshots or logs.
- Rotate keys if accidentally exposed.

Tip: Perplexity Pro subscribers receive $5/month in API credits that can be used with the API key in Brainarr.

## Optional: External Model Registry

- Set `BRAINARR_USE_EXTERNAL_MODEL_REGISTRY=true` before Lidarr starts to let Brainarr hydrate provider defaults (endpoints, recommended models, timeout policies) from a JSON document.
- Provide `BRAINARR_MODEL_REGISTRY_URL` if you host the registry yourself; otherwise Brainarr falls back to the bundled `docs/models.example.json` and caches the last successful download under the system temp directory.
- Registry entries can specify `auth.env` values. When present, Brainarr temporarily reads the named environment variable (for example `OPENAI_API_KEY`) so the **Test** button works without storing the key in the UI.
