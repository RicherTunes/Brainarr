# Brainarr Wiki

## Compatibility

Requires Lidarr **2.14.2.4786+** on the **plugins/nightly** branch. In Lidarr: go to **Settings → General → Updates** and set **Branch = nightly**. If the plugin does not appear after installing, check **System → Logs** for `Brainarr: minVersion` entries and upgrade Lidarr.

## Status

- Latest release: **v1.2.8** (tagged)
- Main branch: **v1.2.8** with nightly patches in progress

### Provider verification (1.2.8)

<!-- PROVIDER_MATRIX_START -->
| Provider | Type | Status | Notes |
| --- | --- | --- | --- |
| LM Studio | Local | ✅ Verified in v1.2.8 | Best local reliability in 1.2.8 |
| Gemini | Cloud | ✅ Verified in v1.2.8 | JSON-friendly responses |
| Perplexity | Cloud | ✅ Verified in v1.2.8 | |
| Ollama | Local | 🔄 Pending re-verification for the 1.2.8 cycle | Re-verify during the 1.2.8 patch cycle |
| OpenAI | Cloud | ⚠️ Experimental | JSON schema support; verify rate limits |
| Anthropic | Cloud | ⚠️ Experimental | |
| Groq | Cloud | ⚠️ Experimental | |
| DeepSeek | Cloud | ⚠️ Experimental | Budget-friendly option |
| OpenRouter | Cloud | ⚠️ Experimental | Gateway to many models |
<!-- PROVIDER_MATRIX_END -->

See the **Local Providers** and **Cloud Providers** wiki pages for smoke tests, API key scopes, and troubleshooting tips. Provider status changes must update this table and `docs/PROVIDER_MATRIX.md`.

## Quick links

- Provider Basics: ./Provider-Basics.md
- Advanced Settings: ./Advanced-Settings.md
- Review Queue: ./Review-Queue.md
- Troubleshooting: ./Troubleshooting.md
- Installation: ./Installation.md
- Observability & Metrics (Preview): ./Observability-and-Metrics.md
