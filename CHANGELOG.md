# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [1.2.4] - 2025-09-12

- feat(registry): add JSON-driven provider/model registry schema and example assets for zero-rebuild model onboarding.
- feat(registry): introduce registry-aware provider factory decorator behind a feature flag with environment API key support.
- feat(registry): add ETag-aware loader with cache/embedded fallback and validation tests plus dedicated CI workflow.
- chore: bump plugin manifest versions to 1.2.4.

## [1.2.3] - 2025-09-11

- CI: Update actions to `actions/setup-node@v5`, `actions/setup-python@v6`, and `lycheeverse/lychee-action@v2`.
- CI: Stabilize link checking by fixing TOML patterns and scoping checks to `README.md` and `docs/` only.
- Docs/Repo: Normalize EOLs and tidy config.
- Docs: Add provider testing checklist (tasks/provider-testing.md).
- Docs: Mark LM Studio, Gemini, and Perplexity as tested for 1.2.3; update support matrix, guide “Last Verified”, README Provider Status, and release notes.

No runtime code changes in this release.

## [Unreleased]

Planned: minor improvements post 1.2.3

## [1.2.2] - 2025-09-08

- fix(settings): tolerate legacy Top‑Up Stop Sensitivity values
  - Added a custom JSON converter to map historical/synonym values (e.g., "balanced", "medium") to current enum members (maps to "normal").
  - Resolves deserialization error: could not convert to StopSensitivity at $.topUpStopSensitivity.
  - Backwards‑compatible; no database migration required.

## [1.2.1] - Timeout + provider scope + docs

- See commit history for details.

[Unreleased]: https://github.com/RicherTunes/Brainarr/compare/v1.2.4...HEAD
[1.2.4]: https://github.com/RicherTunes/Brainarr/compare/v1.2.3...v1.2.4
[1.2.3]: https://github.com/RicherTunes/Brainarr/compare/v1.2.2...v1.2.3
[1.2.2]: https://github.com/RicherTunes/Brainarr/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/RicherTunes/Brainarr/compare/v1.2.0...v1.2.1
