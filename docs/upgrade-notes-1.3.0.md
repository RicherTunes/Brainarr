# Upgrade notes · 1.3.0

## What changed

- Planner and renderer were split so prompts stay deterministic even when you toggle formatting options. The planner now logs its build salt (`ConfigVersion`) whenever cache settings change.
- Plan cache hardening: five-minute sliding TTL, fingerprint-aware invalidation, and explicit metrics (`prompt.plan_cache_*`) so you can monitor hit rates.
- Tokenizer visibility: `ModelTokenizerRegistry` raises a one-time warning and emits `tokenizer.fallback` metrics when a model lacks a dedicated tokenizer.
- Observability: Grafana dashboards in `docs/assets/` were refreshed to use the new metric names (`prompt.plan_cache_*`, `tokenizer.fallback`) so your charts stay accurate after the upgrade.
- Documentation guardrails now gate releases: provider matrix comes from `docs/providers.yaml`, docs consistency runs in CI, and README links out to focused deep dives instead of duplicating content.
- Troubleshooting and configuration docs were reorganised to keep single sources of truth inside the repo and minimise wiki drift.

## What to do after upgrade

1. Restart Lidarr so the updated plugin assemblies load cleanly.
2. Run Brainarr manually once (`Settings → Import Lists → Brainarr → Run Now`) to warm the new plan cache.
3. Watch `System → Logs` for any `tokenizer.fallback` warnings; add tokenizers if a critical model falls back to the estimator.
4. Review [docs/tokenization-and-estimates.md](./tokenization-and-estimates.md) and [docs/planner-and-cache.md](./planner-and-cache.md) so operators understand the new metrics.
5. Re-run the documentation guardrails if you maintain forks: `pwsh ./scripts/sync-provider-matrix.ps1`, `pwsh ./scripts/check-docs-consistency.ps1` (or `bash ./scripts/check-docs-consistency.sh`), then `pre-commit run --all-files`.

## Backward compatibility

- Local-first defaults are unchanged: Ollama stays the default provider with iterative refinement enabled.
- Cloud providers remain opt-in and keep the same API key fields; there are no new required settings.
- Existing cache settings are normalised to the new min/max bounds automatically.
- The docs and wiki now link into a shared set of `.md` files; bookmarks should update to the repo locations listed in the README.

## Related reading

- [README](../README.md)
- [Configuration guide](./configuration.md)
- [Troubleshooting](./troubleshooting.md)
- [Planner & cache](./planner-and-cache.md)
- [Tokenization & estimates](./tokenization-and-estimates.md)
