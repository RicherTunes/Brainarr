# Brainarr Documentation Status (Brainarr 1.2.4)

## Current Health Snapshot
- **Status**: ✅ Maintained
- **Coverage**: High-level topics covered; deep dives pending for recent refactors
- **Accuracy**: Spot-checked against Brainarr 1.2.4 on 2025-09-12
- **Last Audit**: 2025-09-12 (aligned with release 1.2.4)

## Core References
| Area | Primary Docs |
| --- | --- |
| Project overview & install | `README.md`, `docs/USER_SETUP_GUIDE.md`, `wiki-content/Installation.md` |
| Provider guidance | `docs/PROVIDER_GUIDE.md`, `docs/PROVIDER_SUPPORT_MATRIX.md`, wiki `Provider-*` pages |
| Advanced configuration | `docs/RECOMMENDATION_MODES.md`, `wiki-content/Advanced-Settings.md`, inline docs in `Brainarr.Plugin/BrainarrSettings.cs` |
| Architecture & internals | `docs/ARCHITECTURE.md`, `docs/ENHANCED_LIBRARY_ANALYSIS.md`, inline XML comments |
| Troubleshooting | `docs/TROUBLESHOOTING.md`, `wiki-content/Troubleshooting.md`, README runtime troubleshooting |

## Recent Updates (September 2025)
- ✅ Provider documentation refreshed for Brainarr 1.2.4 (verified providers, defaults, manual model override behaviour).
- ✅ README and wiki highlight nightly branch requirement (Lidarr 2.14.1.4716+).
- ✅ Support matrix cleaned up to remove stale merge markers and align with current testing status.

## Known Gaps & Follow-Ups
1. **Model Registry Documentation** – Brainarr 1.2.4 introduces the JSON-driven model registry and decorator. Add a focused how-to covering `RegistryAwareProviderFactoryDecorator`, registry cache behaviour, and override env vars.
2. **Resilience Components** – Expand docs for `RecommendationPipeline`, `RecommendationCoordinator`, and rate limiter/circuit breaker registries.
3. **Observability** – Update `wiki-content/Observability-and-Metrics.md` with current telemetry hooks (`ProviderMetricsHelper`, review queue metrics).
4. **Verification How-To** – Create a short guide (docs + wiki) explaining how to validate a provider and contribute results to the support matrix.

## Maintenance Checklist
- [x] Provider list and verification status match Brainarr 1.2.4 code paths.
- [x] Minimum Lidarr version updated to 2.14.1.4716 everywhere.
- [ ] Model registry docs published.
- [ ] Observability wiki refreshed with new metrics utilities.
- [ ] CONTRIBUTING/dev docs mention registry setup flag (`AIProviderFactory.UseExternalModelRegistry`).

## Contribution Guidelines
- Treat docs and wiki content as part of the release deliverable—update them alongside code changes.
- When adding a provider, update the support matrix, provider guide, wiki provider pages, and CHANGELOG.
- Capture verification evidence (provider, model, environment) in docs when marking providers as tested.
- Run `markdownlint` locally or via CI before submitting documentation-only changes.

*Maintained by the Brainarr documentation group. Please file issues or PRs with `docs` label for gaps or corrections.*
