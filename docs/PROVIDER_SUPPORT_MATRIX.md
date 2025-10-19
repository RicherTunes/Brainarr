# Provider Support Matrix (v1.3.1)

This page now defers to the generated provider matrix so we have a single source of truth.

- **Canonical data** lives in `docs/providers.yaml`.
- **Rendered tables** appear in [README ▸ Provider status](../README.md#provider-status), `docs/PROVIDER_MATRIX.md`, and the wiki provider pages.

## Updating provider data

1. Edit `docs/providers.yaml` (status, notes, verification dates).
2. Run `pwsh ./scripts/sync-provider-matrix.ps1` from the repository root.
3. Commit the updated YAML, regenerated matrix files, and wiki exports together.

## Where to find details

- Provider selection guidance: `docs/PROVIDER_GUIDE.md` (links into the wiki articles for Local and Cloud providers).
- Verification runs and release history: `docs/VERIFICATION-RESULTS.md` and `CHANGELOG.md`.
- Compatibility requirements: see the [README compatibility notice](../README.md#provider-status).

> Historical copies of the manual matrix have been archived under `docs/archive/` should you need to reference the v1.2.x format.
