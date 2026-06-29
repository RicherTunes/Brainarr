<!-- SYNCED_WIKI_PAGE: Do not edit in the GitHub Wiki UI. This page is synced from wiki-content/ in the repository. -->
> Source of truth lives in README.md and docs/. Make changes via PRs to the repo; publish the GitHub Wiki manually until automation exists.

# Brainarr wiki home

The canonical docs now live in the repository so we avoid duplicated truth.

- Quickstart, compatibility, and upgrade notes: see the [README](../README.md).
- Full configuration, tokenization, planner, and troubleshooting guides: see [`docs/`](../docs/).
- Provider status is generated here for convenience; the source of truth is [`docs/PROVIDER_MATRIX.md`](../docs/PROVIDER_MATRIX.md).

Latest release: **v1.6.1**
Requires Lidarr 3.0.0.4855+ on the plugins/nightly branch.

## Built on Lidarr.Plugin.Common

Brainarr is built on the shared [Lidarr.Plugin.Common](https://github.com/RicherTunes/Lidarr.Plugin.Common) library (vendored at `ext/Lidarr.Plugin.Common`). For foundation topics shared across the ecosystem, consult the Common wiki:

- [Common — Home](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Home.md) — project overview, quick links, and cross-plugin conventions.
- [Common — Architecture Overview](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Architecture-Overview.md) — plugin lifecycle, module wiring, and DI patterns every plugin author should know.
- [Common — SDK and Extension Points](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/SDK-and-Extension-Points.md) — interfaces and base classes for adding providers, bridges, and services.
- [Common — Shared Helpers Catalog](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Shared-Helpers-Catalog.md) — reusable utilities (caching, health, resilience, logging, security) that Brainarr consumes.
- [Common — Versioning and Submodule Pinning](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/wiki/Versioning-and-Submodule-Pinning.md) — how Common versions are pinned, bumped, and validated across plugins.

## Install via Lidarr UI (recommended)

You can install Brainarr directly from Lidarr without downloading a ZIP:

1. Ensure Lidarr is on the plugins/nightly branch and at least version 3.0.0.4855 (Settings > General > Updates > Branch = nightly).
2. Go to Settings > Plugins.
3. Click Add Plugin.
4. Paste the repository URL: <https://github.com/RicherTunes/Brainarr>
5. Click Install, then Restart when prompted.
6. Go to Settings > Import Lists and add Brainarr.

## Provider compatibility

<!-- GENERATED: scripts/sync-provider-matrix.ps1 -->
<!-- PROVIDER_MATRIX_START -->
| Provider | Type | Status | Notes |
| --- | --- | --- | --- |
| LM Studio | Local | ✅ Verified in v1.3.1 | Best local reliability in 1.3.x |
| Gemini | Cloud | ✅ Verified in v1.3.1 | JSON-friendly responses |
| Perplexity | Cloud | ✅ Verified in v1.3.1 | Web-aware fallback |
| Ollama | Local | ✅ Verified in v1.3.1 | Run Brainarr entirely offline |
| OpenAI | Cloud | ⚠️ Experimental | JSON schema support; verify rate limits |
| Anthropic | Cloud | ⚠️ Experimental |  |
| Groq | Cloud | ⚠️ Experimental | Low-latency batches |
| DeepSeek | Cloud | ⚠️ Experimental | Budget-friendly option |
| OpenRouter | Cloud | ⚠️ Experimental | Gateway to many models |
| Z.AI GLM | Cloud | ✅ Verified in v1.6.0 | OpenAI-compatible PaaS endpoint |
| Claude Code CLI | CLI | ✅ Verified in v1.6.1 | Shells out to local `claude` CLI binary |
| Claude Code | Subscription | ✅ Verified in v1.3.2 | Uses local Claude Code CLI credentials (~/.claude/.credentials.json) |
| OpenAI Codex | Subscription | ✅ Verified in v1.3.2 | Uses local Codex CLI credentials (~/.codex/auth.json) |
| Z.AI Coding | Subscription | ✅ Verified in v1.6.0 | Anthropic-compatible Coding Plan endpoint |
<!-- PROVIDER_MATRIX_END -->

If you need to expand a topic, update the repo docs first, then add a short pointer here.

## Wiki pages

- [Start Here](Start-Here.md) — orientation and quick links for new users.
- [Installation](Installation.md) — detailed install and Docker setup.
- [First Run Guide](First-Run-Guide.md) — walk-through after your first install.
- [Provider Setup](Provider-Setup.md) — hub for per-provider configuration.
- [Cloud Providers](Cloud-Providers.md) / [Local Providers](Local-Providers.md) — provider-specific guides.
- [Advanced Settings](Advanced-Settings.md) — tuning discovery, confidence, sampling, and strictness.
- [Review Queue](Review-Queue.md) — triage and approval workflow for recommendations.
- [Timeouts & Retries](Timeouts-and-Retries.md) — timeout-aware budgets and retry behavior.
- [Troubleshooting](Troubleshooting.md) — common issues and diagnostics.

<!-- SYNCED_WIKI_PAGE: Do not edit in the GitHub Wiki UI. This page is synced from wiki-content/ in the repository. -->
> Source of truth lives in README.md and docs/. Make changes via PRs to the repo; publish the GitHub Wiki manually until automation exists.
