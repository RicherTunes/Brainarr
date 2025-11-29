# Brainarr Documentation Status

## Current documentation health

**Status**: ✅ Production ready
**Coverage**: 98%
**Accuracy**: 99%
**Last audit**: 2025-11-29

## Documentation structure

### Core documentation (repository root)

- **README.md** – Overview, compatibility promises, documentation map
- **CHANGELOG.md** – Release history and user-facing highlights
- **CLAUDE.md** – AI assistant guardrails and context sharing
- **AGENTS.md** – Process guardrails for contributors and automations
- **plugin.json / manifest.json** – Runtime metadata, minimum Lidarr version

### Technical documentation (`/docs`)

- **configuration.md** – All settings reference including advanced options, discovery modes, backfill strategies
- **planner-and-cache.md** – Deterministic planning pipeline and cache tuning
- **tokenization-and-estimates.md** – Tokenizer registry usage and drift controls
- **troubleshooting.md** – Quick FAQ plus step-by-step resolutions, including provider-specific errors
- **PROVIDER_GUIDE.md** – Provider selection guidance, model options, fallback chains
- **PROVIDER_MATRIX.md** – Generated matrix sourced from `docs/providers.yaml`
- **upgrade-notes-1.3.0.md** – Focused checklist for the 1.3.0 release
- **RELEASE_PROCESS.md** – Publish workflow, verification steps, and doc-sync tooling

### Architecture documentation (`/docs/architecture`)

- **brainarr-orchestrator-blueprint.md** – Orchestrator design and flow
- **configuration-validation-tests.md** – Validation test patterns
- **shared-library-integration.md** – Integration with lidarr.plugin.common
- **source-set-hygiene.md** – Code organization and source hygiene

### Developer & operations documentation

- **BUILD.md / build.ps1** – Environment bootstrap and automation entrypoints
- **TESTING_GUIDE.md** – Test taxonomy plus how to exercise new suites
- **CI_CD_IMPROVEMENTS.md** & **ci-stability-guide.md** – Reliability workstreams and ongoing CI tasks
- **SECURITY.md** – Security best practices, API key handling, data privacy
- **DOCUMENTATION_STATUS.md** (this file) – Canonical view of doc coverage and follow-up work

### Reference documentation

- **API_REFERENCE.md** – Complete interface, class, and error code documentation
- **METRICS_REFERENCE.md** – All metrics with tags and emitters
- **CORRELATION_TRACKING.md** – Request tracing and correlation ID system

## Recent updates (2025-11-29)

- **Consolidated FAQ into troubleshooting.md** – Single source for common questions and issues
- **Deleted redundant files** – Removed `PROVIDER_SUPPORT_MATRIX.md` (redirect only) and `RECOMMENDATIONS.md` (pointer only)
- **Updated PROVIDER_GUIDE.md** – Accurate model IDs matching current codebase (GPT41_Mini, ClaudeSonnet4, Gemini_25_Flash, etc.)
- **Expanded configuration.md** – Added documentation for Thinking Mode, Backfill Strategies, Recommendation Modes, Sampling Shape
- **Added provider-specific troubleshooting** – Sections for Gemini, OpenAI, Anthropic, OpenRouter, DeepSeek, Groq, Perplexity

## Maintenance expectations

1. **Run generators** – Always run `pwsh ./scripts/sync-provider-matrix.ps1` after editing `docs/providers.yaml`.
2. **Enforce parity** – Run `bash ./scripts/check-docs-consistency.sh` (and its PowerShell twin) before merging; CI fails if README badges, provider tables, or min-version banners drift.
3. **Single source of truth** – Update canonical files first; regenerated artefacts must be committed in the same PR.
4. **Traceability** – When docs describe behaviour covered by tests, mention the relevant test class (e.g., `TokenBudgetGuardTests`).

## Documentation metrics

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| File count (docs/) | ~45 | – | ✅ |
| Coverage | 98% | ≥90% | ✅ |
| Accuracy | 99% | ≥95% | ✅ |
| Code comments referencing docs | 65% | 80% | ⚠️ |
| API reference freshness | 100% | 100% | ✅ |
| User guides up to date | 100% | 100% | ✅ |

## Priority actions

1. Add Grafana dashboard walkthrough in `docs/examples/`.
2. Add inline XML doc comments for `CorrelationContext` helpers.
3. Review wiki content alignment with docs.

## Validation checklist

- [x] Provider matrix regenerated from `docs/providers.yaml`
- [x] README documentation map matches canonical file names
- [x] Tokenization fallback process covered in docs and metrics reference
- [x] Provider-specific troubleshooting sections added
- [x] Configuration docs match BrainarrSettings.cs
- [x] Model IDs in docs match Enums.cs
- [ ] Grafana example documented (`docs/examples/`)
- [ ] Wiki sync complete

---

This file is the authoritative status dashboard for Brainarr documentation. Update the metrics and outstanding tasks during each release cycle so contributors and automation share the same roadmap.
