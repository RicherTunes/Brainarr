# Brainarr Wiki

Brainarr is a local-first AI import list plugin that runs great with **local providers** (Ollama, LM Studio) and can optionally use **cloud providers** (OpenAI, Anthropic, Gemini, Perplexity, Groq, DeepSeek, OpenRouter) when you enable them. Cloud usage is opt-in and governed by your settings.

## Compatibility

Requires Lidarr **2.14.2.4786+** on the **plugins/nightly** branch. In Lidarr: go to **Settings → General → Updates** and set **Branch = nightly**. If the plugin does not appear after installing, check **System → Logs** for `Brainarr: minVersion` entries and upgrade Lidarr.

## Status

- Latest release: **v1.3.0** (tagged)
- Main branch: **v1.3.0** with nightly patches in progress

### Provider verification (1.3.0)

<!-- PROVIDER_MATRIX_START -->
| Provider | Type | Status | Notes |
| --- | --- | --- | --- |
| LM Studio | Local | ✅ Verified in v1.3.0 | Best local reliability in 1.3.x |
| Gemini | Cloud | ✅ Verified in v1.3.0 | JSON-friendly responses |
| Perplexity | Cloud | ✅ Verified in v1.3.0 | Web-aware fallback |
| Ollama | Local | ✅ Verified in v1.3.0 | Run Brainarr entirely offline |
| OpenAI | Cloud | ⚠️ Experimental | JSON schema support; verify rate limits |
| Anthropic | Cloud | ⚠️ Experimental |  |
| Groq | Cloud | ⚠️ Experimental | Low-latency batches |
| DeepSeek | Cloud | ⚠️ Experimental | Budget-friendly option |
| OpenRouter | Cloud | ⚠️ Experimental | Gateway to many models |

<!-- PROVIDER_MATRIX_END -->

See the **Local Providers** and **Cloud Providers** wiki pages for smoke tests, API key scopes, and troubleshooting tips. Update provider status by editing `docs/providers.yaml` and running `pwsh ./scripts/sync-provider-matrix.ps1` so README, docs, and the wiki stay in sync.

## Quick links

- [Start Here](Start-Here) — linear onboarding checklist
- [Provider Basics](Provider-Basics) & [Provider Selector](Provider-Selector)
- [Operations Playbook](Operations)
- [Advanced Settings](Advanced-Settings)
- [Review Queue](Review-Queue)
- [Installation](Installation)
- [Troubleshooting](Troubleshooting) & [Observability & Metrics](Observability-and-Metrics)
- [Documentation Workflow](Documentation-Workflow)
