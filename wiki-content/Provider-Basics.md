# Provider Basics

> Compatibility
> Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).

Brainarr’s provider catalog lives in `docs/providers.yaml`; run `pwsh ./scripts/sync-provider-matrix.ps1` after edits so the README, docs, and this wiki stay in sync. For the latest verification status and defaults, check the generated matrix in [docs/PROVIDER_MATRIX.md](../docs/PROVIDER_MATRIX.md) or the README provider table.

## How to choose a provider

Use these quick rules of thumb, then dive into the dedicated guides for step-by-step setup.

- **Local & private** (Ollama, LM Studio): zero cost, data never leaves your network. See [Local Providers](Local-Providers).
- **Gateway access** (OpenRouter): one key unlocks many models; great for experimentation. See [Cloud Providers](Cloud-Providers#openrouter).
- **Budget cloud** (DeepSeek, Gemini): generous free tiers or very low pricing; ideal for high volume.
- **Premium reasoning** (OpenAI, Anthropic): best raw quality; plan for higher costs.
- **Ultra fast** (Groq): sub-second responses for interactive workflows.

## Connection settings

Only local providers require URLs:

- **Ollama**: `http://localhost:11434`
- **LM Studio**: `http://localhost:1234`

Cloud providers rely on API keys and ignore the URL field.

## API keys & portals

Create keys with the provider dashboards below and store them securely (Credential Manager, Keychain, libsecret, etc.). Never paste keys in screenshots or issue reports.

- OpenAI — <https://platform.openai.com/api-keys>
- Anthropic — <https://console.anthropic.com>
- OpenRouter — <https://openrouter.ai/keys>
- Perplexity — <https://perplexity.ai/settings/api>
- DeepSeek — <https://platform.deepseek.com>
- Google Gemini — <https://aistudio.google.com/apikey>
- Groq — <https://console.groq.com/keys>

Tip: Perplexity Pro subscribers receive $5/month in API credits that work with the API key above.

## Next steps

- Follow the detailed walkthroughs in [Provider Setup](Provider-Setup) for each provider.
- After configuring a primary provider, run the [First Run Guide](First-Run-Guide) to validate recommendations.
- For advanced tuning (fallbacks, sampling, safety gates), read [Advanced Settings](Advanced-Settings) and the README compatibility notice.
