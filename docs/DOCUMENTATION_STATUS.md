# Brainarr Documentation Status

## Current documentation health

**Status**: ✅ Production ready
**Coverage**: 97%
**Accuracy**: 99%
**Last audit**: 2025-10-02

## Documentation structure

### Core documentation (repository root)

- **README.md** – Overview, compatibility promises, documentation map
- **CHANGELOG.md** – Release history and user-facing highlights
- **CLAUDE.md** – AI assistant guardrails and context sharing
- **AGENTS.md** – Process guardrails for contributors and automations
- **plugin.json / manifest.json** – Runtime metadata, minimum Lidarr version

### Technical documentation (`/docs`)

- **configuration.md** – Local-first defaults, optional cloud providers, required scripts
- **planner-and-cache.md** – Deterministic planning pipeline and cache tuning
- **tokenization-and-estimates.md** – Tokenizer registry usage and drift controls
- **troubleshooting.md** – Step-by-step resolutions for the most common support tickets
- **upgrade-notes-1.3.0.md** – Focused checklist for the 1.3.0 release
- **PROVIDER_MATRIX.md** – Generated matrix sourced from `docs/providers.yaml`
- **RELEASE_PROCESS.md** – Publish workflow, verification steps, and doc-sync tooling

### Developer & operations documentation

- **BUILD.md / build.ps1** – Environment bootstrap and automation entrypoints
- **TESTING_GUIDE.md** – Test taxonomy plus how to exercise new suites
- **CI_CD_IMPROVEMENTS.md** & **ci-stability-guide.md** – Reliability workstreams and ongoing CI tasks
- **DOCUMENTATION_STATUS.md** (this file) – Canonical view of doc coverage and follow-up work

## Recent updates

- README now includes a **Documentation map** that links to the canonical guides added in 1.3.0.
- Tokenization fallback behaviour is documented in both README and `docs/tokenization-and-estimates.md`, matching the planner implementation.
- Troubleshooting guide expanded with cache TTL guidance and metric names (`prompt.plan_cache_*`, `tokenizer.fallback`).
- `scripts/sync-provider-matrix.ps1` and `scripts/check-docs-consistency.sh` documented as mandatory steps for any provider/docs change.

## Areas still in progress

1. Document `CorrelationContext` propagation and log correlation IDs in `docs/architecture/`.
2. Add a short recipe in `docs/examples/` showing planner metrics exported to Grafana (referencing the dashboards shipped in `docs/assets/`).
3. Refresh wiki content once the publish script (`scripts/publish-wiki.ps1`) lands.

## Maintenance expectations

1. **Run generators** – Always run `pwsh ./scripts/sync-provider-matrix.ps1` after editing `docs/providers.yaml`.
2. **Enforce parity** – Run `bash ./scripts/check-docs-consistency.sh` (and its PowerShell twin) before merging; CI fails if README badges, provider tables, or min-version banners drift.
3. **Single source of truth** – Update canonical files first; regenerated artefacts must be committed in the same PR.
4. **Traceability** – When docs describe behaviour covered by tests, mention the relevant test class (e.g., `TokenBudgetGuardTests`).

## Documentation metrics

| Metric | Value | Target | Status |
|--------|-------|--------|--------|
| File count | 60 | – | ✅ |
| Coverage | 97% | ≥90% | ✅ |
| Accuracy | 99% | ≥95% | ✅ |
| Code comments referencing docs | 65% | 80% | ⚠️ |
| API reference freshness | 100% | 100% | ✅ |
| User guides up to date | 100% | 100% | ✅ |

## Priority actions

1. Produce a `docs/examples/grafana-dashboard.md` walkthrough (export metrics, import shipped dashboards).
2. Add inline XML doc comments for `CorrelationContext` helpers and link back here once landed.
3. Expand `CHANGELOG.md` with the final 1.3.0 release summary.

## Validation checklist

- [x] Provider matrix regenerated from `docs/providers.yaml`
- [x] README documentation map matches canonical file names
- [x] Tokenization fallback process covered in docs and metrics reference
- [ ] Grafana example documented (`docs/examples/`)
- [ ] Correlation context behaviour documented
- [ ] CHANGELOG entry for 1.3.0 GA complete

---

This file is the authoritative status dashboard for Brainarr documentation. Update the metrics and outstanding tasks during each release cycle so contributors and automation share the same roadmap.
