# Documentation Status – September 2025

_Last sweep: 2025-09-13 (Brainarr v1.2.4)_

## Overall Health

- **Status**: In progress (core install + provider docs up to date, registry guidance newly added)
- **Coverage**: High for user-facing setup (README, wiki), medium for advanced operations (observability, registry hosting)
- **Accuracy**: Verified against codebase as of commit for v1.2.4

## Key References

| Topic | Location |
|-------|----------|
| Quick start & install | [`README.md`](../README.md), [`docs/USER_SETUP_GUIDE.md`](USER_SETUP_GUIDE.md), [`wiki-content/Installation.md`](../wiki-content/Installation.md) |
| Provider matrix & verification | [`docs/PROVIDER_SUPPORT_MATRIX.md`](PROVIDER_SUPPORT_MATRIX.md), [`docs/PROVIDER_GUIDE.md`](PROVIDER_GUIDE.md), [`wiki-content/Cloud-Providers.md`](../wiki-content/Cloud-Providers.md) |
| Advanced tuning | [`wiki-content/Advanced-Settings.md`](../wiki-content/Advanced-Settings.md), [`docs/RECOMMENDATION_MODES.md`](RECOMMENDATION_MODES.md) |
| Troubleshooting | [`docs/TROUBLESHOOTING.md`](TROUBLESHOOTING.md), [`wiki-content/Troubleshooting.md`](../wiki-content/Troubleshooting.md) |
| Development workflows | [`docs/DEVELOPMENT.md`](../DEVELOPMENT.md), [`docs/TESTING_GUIDE.md`](TESTING_GUIDE.md), [`docs/DEPLOYMENT.md`](DEPLOYMENT.md) |

## Recent Updates

- Added documentation for the optional model registry (README, provider matrix, provider guide, wiki Provider Basics).
- Normalised provider verification status to 1.2.4 across README, docs, and wiki.
- Replaced outdated audit-style reports with concise status summaries (this file + `docs/VERIFICATION-RESULTS.md`).

## Outstanding Work

1. **Registry hosting guidance** – Document best practices for serving signed registry JSON once remote hosting is finalised.
2. **Observability walkthrough** – Expand on correlation IDs and structured logging in [`wiki-content/Observability-and-Metrics.md`](../wiki-content/Observability-and-Metrics.md).
3. **CI documentation** – Refresh [`docs/CI_CD_IMPROVEMENTS.md`](CI_CD_IMPROVEMENTS.md) with the current GitHub Actions workflows.
4. **Test coverage notes** – Summarise the unit/integration split and recommended filters in [`docs/TESTING_GUIDE.md`](TESTING_GUIDE.md).

## Maintenance Checklist

- [x] README provider status aligned with latest verification data
- [x] Wiki installation + provider pages updated for 1.2.4
- [x] Optional model registry documented in README, provider matrix, provider guide, and wiki
- [ ] Remaining providers (OpenAI, Anthropic, OpenRouter, DeepSeek, Groq, Ollama) to be verified and marked accordingly
- [ ] Update screenshots once UI changes or new settings land

## Contact

Report documentation gaps via GitHub issues or PRs. When proposing updates, include:

- The feature or behaviour observed in the codebase
- Screenshots/log snippets when relevant
- Version of Brainarr + Lidarr used during testing
