# Brainarr AI Provider Guide

Brainarr supports nine AI providers across local, cloud, and gateway modes. This guide helps you choose the right mix without duplicating the canonical reference tables.

- **Provider status, verification notes, and default models** are generated from `docs/providers.yaml` into [README ▸ Provider status](../README.md#provider-status) and `docs/PROVIDER_MATRIX.md`.
- **Deep dives and setup walkthroughs** live in the wiki: [Local Providers](https://github.com/RicherTunes/Brainarr/wiki/Local-Providers) and [Cloud Providers](https://github.com/RicherTunes/Brainarr/wiki/Cloud-Providers).
- **Troubleshooting** per provider is centralised in `docs/TROUBLESHOOTING.md` and the wiki **Observability & Metrics** page.

> Compatibility
> Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly). See the README Compatibility notice for the canonical requirement before enabling Brainarr.

## Offline mode

To stay fully offline, stick to local providers (Ollama, LM Studio), leave `BRAINARR_MODEL_REGISTRY_URL` unset, and confirm no fallback providers are enabled. The setup scripts populate every required assembly locally, so Brainarr can operate without network calls—watch the logs for unexpected HTTP requests when validating.

## Choosing your first provider

| Goal | Recommended starting point | Why |
|------|---------------------------|-----|
| 100% privacy / offline | **Ollama** or **LM Studio** | Zero-cost, no data leaves your network, great for pilots.
| Lowest cloud spend | **DeepSeek** | ~$0.14/M tokens with strong quality; pair with free Gemini for overflow.
| “It just works” managed cloud | **Gemini Flash** | Generous free tier, excellent context window, easy onboarding.
| Highest quality | **Claude 3.5 Sonnet** (via OpenRouter) | Premium reasoning. Budget a fallback such as GPT-4o.
| Fastest responses | **Groq Llama 3.2** | Ultra-low latency for interactive playlists.

For decision trees and UI screenshots, follow the wiki guides linked above.

## Suggested fallback chains

These chains balance cost, stability, and performance. Configure them in Brainarr’s provider settings using the UI labels shown in the matrix.

1. **Privacy First:** Ollama → LM Studio (ensure both run locally).
2. **Cost Effective:** DeepSeek → Gemini Flash → OpenRouter/GPT-4.1 Mini for overflow.
3. **Quality First:** Claude 3.5 Sonnet → GPT-4o → DeepSeek Chat.
4. **Exploration:** OpenRouter Auto → Gemini Flash → Groq Llama (fast retries).

Document the rationale for your environment in `docs/VERIFICATION-RESULTS.md` after testing.

## How to update provider data

1. Edit `docs/providers.yaml` with new providers, status changes, or verification dates.
2. Run `pwsh ./scripts/sync-provider-matrix.ps1` to regenerate README, docs/PROVIDER_MATRIX.md, and the wiki fragments.
3. Commit the YAML and regenerated files together (CI enforces consistency).

## Next steps & references

- **Setup in Lidarr:** follow the [README quick start](../README.md#quick-start) then continue with [`docs/USER_SETUP_GUIDE.md`](USER_SETUP_GUIDE.md).
- **Operations & monitoring:** see `docs/TROUBLESHOOTING.md` and the wiki **Observability & Metrics** page.
- **Advanced tuning:** adjust sampling and budget knobs via the Advanced Settings wiki chapter.

Keep this guide focused on decision support; if you need to add raw tables or step-by-step setup, update the wiki or `docs/providers.yaml` instead so every surface stays in sync.
