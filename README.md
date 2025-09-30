# Brainarr - AI-Powered Music Discovery for Lidarr

<p align="left">
  <img src="docs/assets/brainarr-logo.png" alt="Brainarr logo" width="500" height="333">
 </p>

[![License](https://img.shields.io/github/license/RicherTunes/Brainarr)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-6.0%2B-blue)](https://dotnet.microsoft.com/download)
[![Lidarr](https://img.shields.io/badge/Lidarr-Plugin-green)](https://lidarr.audio/)
[![Version](https://img.shields.io/badge/version-1.3.0-brightgreen)](plugin.json)
[![Latest Release](https://img.shields.io/badge/latest_release-1.3.0-brightgreen)](https://github.com/RicherTunes/Brainarr/releases/tag/v1.3.0)
[![Changelog](https://img.shields.io/badge/changelog-link-blue)](CHANGELOG.md)
[![Docs Lint](https://github.com/RicherTunes/Brainarr/actions/workflows/docs-lint.yml/badge.svg)](https://github.com/RicherTunes/Brainarr/actions/workflows/docs-lint.yml)
[![pre-commit](https://github.com/RicherTunes/Brainarr/actions/workflows/pre-commit.yml/badge.svg)](https://github.com/RicherTunes/Brainarr/actions/workflows/pre-commit.yml)

Local-first AI Music Discovery for Lidarr — Brainarr is a privacy-focused import list plugin that runs great with **local providers** (Ollama, LM Studio) and can **optionally** use **cloud providers** (OpenAI, Anthropic, Gemini, Perplexity, Groq, DeepSeek, OpenRouter) for scale. Cloud usage is opt-in and governed by your settings.

## Why Brainarr?

- **Local-first recommendations** that work entirely offline (Ollama/LM Studio) with optional cloud scale-ups.
- **Deterministic planning & caching** so repeated runs produce stable, comparable prompts.
- **Safe defaults** for sampling, safety gates, and failover that you can tune without touching code.
- **Observability built-in** (metrics, logs, dashboards) to keep provider health and token budgets in check.
- **Ready-to-ship docs & workflows** for operations, troubleshooting, and documentation maintenance.

> *Local-first design.* Brainarr runs great with **local providers** (Ollama, LM Studio). You can optionally enable **cloud providers** (OpenAI, Anthropic, Gemini, Perplexity, Groq, DeepSeek, OpenRouter) with automatic failover and health monitoring when you need extra scale.
>
> Compatibility Notice
> Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch. In Lidarr: Settings > General > Updates > set Branch = nightly. If you run an older Lidarr, upgrade first — otherwise the plugin will not load.
>
> The plugin fails closed on unsupported Lidarr versions. If Brainarr does not appear after install, check **System → Logs** for `Brainarr: minVersion` messages and confirm Lidarr is tracking the `nightly` branch.
>
## Provider status

- Latest release: **v1.3.0** (tagged)
- Main branch: **v1.3.0** with nightly patches in progress

The matrix below is generated from `docs/providers.yaml` (run `pwsh ./scripts/sync-provider-matrix.ps1` after edits). For additional setup tips, see the "Local Providers" and "Cloud Providers" wiki pages.

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

## What's new in 1.3.0

- Deterministic prompt planning/rendering thanks to new tie-breakers, centralized `StableHash`, and a hard headroom guard in `LibraryAwarePromptBuilder`.
- Advanced sampling configuration via `sampling_shape` (defaults + validation) so you can tune ratios without touching code.
- Provider docs now flow from `docs/providers.yaml` using `scripts/sync-provider-matrix.ps1`, keeping README, docs, and wiki in sync.
- Documentation refresh across README, Advanced Settings, and the wiki for the 1.3.0 release.

> For the full list see [CHANGELOG.md](CHANGELOG.md).

## Feature highlights

- Local-first recommendations with optional failover to nine verified cloud providers.
- Deterministic planning, caching, and headroom guards keep prompts stable and within model limits.
- Built-in observability (metrics + structured logs) so you can monitor provider health and token usage.

## Quick install summary

| Platform | Steps |
| --- | --- |
| Lidarr (UI) | Settings → Plugins → Add → paste `https://github.com/RicherTunes/Brainarr`; restart Lidarr. |
| Docker | Bind-mount the plugin folder (`/config/plugins/RicherTunes/Brainarr/`) or use the UI; ensure container is on nightly tag. |
| Windows | Copy release ZIP contents to `C\\ProgramData\\Lidarr\\plugins\\RicherTunes\\Brainarr\` and restart. |
| Linux | Copy release ZIP contents to `/var/lib/lidarr/plugins/RicherTunes/Brainarr/` (or the path from the Installation wiki) and restart. |

## Sample configurations

- **Local-only**: Primary provider = Ollama (`http://localhost:11434`, model `qwen2.5:latest`), Iterative Refinement = On, Safety Gates = defaults.
- **Budget cloud failover**: Primary = DeepSeek (`deepseek-chat`), Fallback providers = Gemini Flash → OpenRouter `gpt-4.1-mini`, Guarantee Exact Target = On, Adaptive throttling = On (CloudCap 2).
- **Premium quality**: Primary = Claude 3.5 Sonnet (via OpenRouter), Fallback = GPT-4o → DeepSeek Chat, Safety Gates tightened (Minimum Confidence ≥ 0.65, Require MBIDs = true).

## Quick start

1. Confirm Lidarr 2.14.2.4786+ and clone the plugin (see [docs/USER_SETUP_GUIDE.md](docs/USER_SETUP_GUIDE.md)).
2. Run `./setup.sh` or `./setup.ps1` to fetch Lidarr assemblies and restore the solution.
3. Configure one provider in the Brainarr UI, then click **Test** to verify the connection.
4. For day-to-day workflows (manual fetch, automatic schedules, review queue) follow the **Operations** chapter in [docs/USER_SETUP_GUIDE.md](docs/USER_SETUP_GUIDE.md).

## Documentation map

- **Start here**: [wiki Start Here](https://github.com/RicherTunes/Brainarr/wiki/Start-Here) stitches together installation → provider selection → first run.

- **Setup & operations**: [docs/USER_SETUP_GUIDE.md](docs/USER_SETUP_GUIDE.md) (mirrors the "First-Run" wiki section) and the [Operations Playbook](https://github.com/RicherTunes/Brainarr/wiki/Operations).

- **Providers**: [docs/PROVIDER_GUIDE.md](docs/PROVIDER_GUIDE.md) + the wiki [Provider Selector](https://github.com/RicherTunes/Brainarr/wiki/Provider-Selector), [Local Providers](https://github.com/RicherTunes/Brainarr/wiki/Local-Providers), and [Cloud Providers](https://github.com/RicherTunes/Brainarr/wiki/Cloud-Providers).

- **Advanced configuration**: Wiki [Advanced Settings](https://github.com/RicherTunes/Brainarr/wiki/Advanced-Settings) aggregates tuning guidance (sampling shape, safety gates, token budgets). Pair it with [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) and the [Observability & Metrics](https://github.com/RicherTunes/Brainarr/wiki/Observability-and-Metrics) appendix for dashboards.

- **Troubleshooting & observability**: [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md), [docs/FAQ.md](docs/FAQ.md), and the wiki **Troubleshooting** and **Observability & Metrics** pages.

- **Architecture & roadmap**: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and [docs/ROADMAP.md](docs/ROADMAP.md) for component-level detail and planning.

- **Docs governance**: [docs/DOCS_STRATEGY.md](docs/DOCS_STRATEGY.md) explains canonical sources and required scripts; use the [Documentation Workflow](https://github.com/RicherTunes/Brainarr/wiki/Documentation-Workflow) when editing docs/wiki content.

## Provider operations

- Prefer running everything locally (Ollama, LM Studio). For cloud usage review API key scopes, data handling, and rate limits in [docs/PROVIDER_GUIDE.md](docs/PROVIDER_GUIDE.md).
- Keep the provider matrix in sync by editing `docs/providers.yaml`; the sync script rewrites README, docs, and wiki tables.
- Cost, speed, and verification notes live in the guide + wiki. Avoid duplicating per-provider notes elsewhere.

## Observability & troubleshooting

- Enable debug logging only while investigating; the exact flags live in [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md).
- Metrics names (prompt tokens, cache hit rates, etc.) are documented in wiki **Observability & Metrics**—treat that as the single source of truth.
- If recommendations fail, check provider health dashboards first, then review the review-queue guidance in the wiki.

## Development

- Build and test instructions live in [BUILD.md](BUILD.md) and [docs/USER_SETUP_GUIDE.md](docs/USER_SETUP_GUIDE.md#development-mode). Use those instead of ad-hoc snippets.
- Architectural details, extension points, and component ownership are catalogued in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).
- Contribution workflow: see [CONTRIBUTING.md](CONTRIBUTING.md) for branching, linting, and PR checklist.

## Upgrade & rollback checklist

1. Back up your existing plugin directory (or note the currently installed version).
2. Install the new release via the Lidarr UI or copy the ZIP contents over the existing `plugins/RicherTunes/Brainarr/` folder.
3. Run through the [Operations Playbook](https://github.com/RicherTunes/Brainarr/wiki/Operations) smoke tests (Test button, Manual fetch, Review Queue).
4. If anything regresses, restore the previous ZIP or reinstall the earlier tag.

## Known limitations

- Requires Lidarr nightly builds (2.14.2.4786+); older releases are unsupported.
- Provider health is dependent on your API keys/quotas—configure at least one fallback.
- Brainarr currently targets album/artist recommendations only (no track-level mode).

## Support & community

- File issues or feature requests on GitHub.
- Join GitHub Discussions (when enabled) or the Arr community channels for Q&A.
- Review the [FAQ](docs/FAQ.md) and doc map before opening a ticket.

## Project status

**Latest Release**: 1.3.0 (`v1.3.0` tag)

**Main Branch Version**: 1.3.0 (matches latest release; PRs now target 1.3.x maintenance).

For planned work see [docs/ROADMAP.md](docs/ROADMAP.md) and the GitHub project board.

## License

Brainarr is distributed under the [MIT License](LICENSE).

## Acknowledgments

Thanks to the provider teams, Lidarr project, and everyone filing issues and PRs.
