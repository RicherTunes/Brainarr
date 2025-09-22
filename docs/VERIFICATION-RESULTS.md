# Brainarr Verification Results (v1.2.4)

_Last reviewed: 2025-09-13_

## Executive Summary

- **Target environment**: Lidarr 2.14.1.4716+ (nightly/plugins branch) with .NET 6 runtime. The plugin manifest enforces this minimum in [`plugin.json`](../plugin.json).
- **Providers**: Nine providers implemented via [`AIProvider`](../Brainarr.Plugin/Configuration/Enums.cs) with concrete classes under `Services/Providers/`. Local providers (Ollama, LM Studio) use live model discovery; cloud providers rely on mapped IDs with optional manual overrides.
- **New in 1.2.4**: Optional JSON-backed model registry (`BRAINARR_USE_EXTERNAL_MODEL_REGISTRY=true`) with cache + ETag support (`ModelRegistryLoader`) and environment-driven credential injection (`RegistryAwareProviderFactoryDecorator`). Embedded fallbacks live under `docs/models.example.json` + `docs/models.schema.json`.
- **Verified providers**: Smoke-tested LM Studio (local), Google Gemini (cloud), and Perplexity (cloud) during release 1.2.4. Other providers remain available but pending verification.

## Architecture Highlights

| Area | Evidence |
|------|----------|
| Import list integration | [`BrainarrImportList`](../Brainarr.Plugin/BrainarrImportList.cs) extends `ImportListBase<BrainarrSettings>` and wires orchestration, caching, health monitoring, and registry support. |
| Provider orchestration | [`AIProviderFactory`](../Brainarr.Plugin/Services/Core/AIProviderFactory.cs) + [`ProviderRegistry`](../Brainarr.Plugin/Services/Core/ProviderRegistry.cs) create providers and optionally hydrate from the external registry defined in [`ModelRegistry`](../Brainarr.Plugin/Services/Registry/ModelRegistry.cs). |
| Recommendation pipeline | [`BrainarrOrchestrator`](../Brainarr.Plugin/Services/Core/BrainarrOrchestrator.cs) coordinates library analysis, prompt building, iterative top-up, validation, and caching. |
| Library analysis | [`LibraryAnalyzer`](../Brainarr.Plugin/Services/Core/LibraryAnalyzer.cs) builds genre/era profiles and sampling strategies. |
| Caching & resilience | [`RecommendationCache`](../Brainarr.Plugin/Services/RecommendationCache.cs) plus [`RetryPolicy`](../Brainarr.Plugin/Services/RetryPolicy.cs) and [`ProviderHealthMonitor`](../Brainarr.Plugin/Services/ProviderHealth.cs) manage resilience. |

## Settings & UI

- [`BrainarrSettings`](../Brainarr.Plugin/BrainarrSettings.cs) exposes provider selection, model dropdowns, manual overrides, iterative refinement, safety gates, thinking mode, and caching knobs.
- Validation is handled by [`BrainarrSettingsValidator`](../Brainarr.Plugin/Configuration/BrainarrSettingsValidator.cs) and provider-specific validators.
- Model detection for local providers is implemented in [`ModelDetectionService`](../Brainarr.Plugin/Services/ModelDetectionService.cs) with caching to minimise repeated probes.

## Testing & Tooling Overview

- Unit and integration tests live under [`Brainarr.Tests`](../Brainarr.Tests/). Categories include `Configuration`, `Services/Core`, `Services/Providers`, and `Integration`.
- Test orchestration scripts: `test-local-ci.ps1` / `.sh` and `scripts/generate-coverage-report.ps1` (see [docs/TESTING_GUIDE.md](TESTING_GUIDE.md)).
- 1.2.4 functional smoke tests focused on LM Studio, Gemini (AI Studio key), and Perplexity Sonar models. Remaining providers require additional manual validation before being marked verified.

## Deployment Notes

- Recommended installation is via the Lidarr plugin gallery (Settings ➜ Plugins ➜ Add Plugin ➜ GitHub URL). Manual installs should copy `Lidarr.Plugin.Brainarr.dll`, `plugin.json`, and the `docs/models*.json` assets into `RicherTunes/Brainarr` under the Lidarr plugins directory (see [docs/DEPLOYMENT.md](DEPLOYMENT.md)).
- When enabling the external registry, ensure the environment variables are set for the Lidarr process/service. Cached registries live under `%TEMP%/Brainarr/ModelRegistry/` (or the platform equivalent).

## Outstanding Items

- Expand smoke testing to cover OpenAI, Anthropic, OpenRouter, DeepSeek, Groq, and Ollama during the 1.2.x cycle.
- Document registry hosting best practices (signing, integrity metadata) once the remote schema is finalised.
- Continue tightening documentation around correlation IDs and observability (see [wiki/Observability-and-Metrics](../wiki-content/Observability-and-Metrics.md)).

## Contact & Issue Reporting

For bugs, configuration issues, or verification updates, open an issue at <https://github.com/RicherTunes/Brainarr/issues>. Include Lidarr logs around plugin startup, provider health checks, and the output of the **Test** button for faster triage.
