# Brainarr wiki home

The canonical docs now live in the repository so we avoid duplicated truth.

- Quickstart, compatibility, and upgrade notes: see the [README](../README.md).
- Full configuration, tokenization, planner, and troubleshooting guides: see [`docs/`](../docs/).
- Provider status is generated here for convenience; the source of truth is [`docs/PROVIDER_MATRIX.md`](../docs/PROVIDER_MATRIX.md).
Latest release: **v1.3.0**
Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch.

## Provider compatibility

<!-- GENERATED: scripts/sync-provider-matrix.ps1 -->
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

If you need to expand a topic, update the repo docs first, then add a short pointer here.
