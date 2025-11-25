# Documentation & Wiki Governance

Brainarr’s documentation follows a **single source of truth** model so README, docs/, and the wiki stay aligned even as multiple contributors iterate. Use this page as the contract for how we update docs, run lint checks, and publish wiki content.

## Canonical sources

| Domain | Source of truth | Generated surfaces |
|--------|-----------------|--------------------|
| Provider metadata (status, notes, defaults) | `docs/providers.yaml` | README provider matrix, `docs/PROVIDER_MATRIX.md`, wiki provider tables |
| Release & verification notes | `CHANGELOG.md` + `docs/VERIFICATION-RESULTS.md` | README “What’s new”, wiki Home status block |
| Advanced settings defaults | `Brainarr.Plugin/BrainarrSettings.cs` + unit tests | Wiki “Advanced Settings” hub, UI tooltips |
| Operational workflows | `docs/USER_SETUP_GUIDE.md`, wiki Operations/First Run pages | README quick start, troubleshooting links |

Whenever you change a canonical file, regenerate the dependent surfaces (see Scripts below) and commit both together.

## Required scripts & checks

- `pwsh ./build.ps1 --docs` — runs markdown lint, whitespace checks, README/wiki consistency checks. Must pass before merging.
- `pwsh ./scripts/sync-provider-matrix.ps1` — refreshes provider matrix outputs after editing `docs/providers.yaml`.
- `bash ./scripts/auto-upload-wiki.sh` — push the rendered `wiki-content/` into the GitHub wiki locally (mirrors the CI job in `.github/workflows/wiki-update.yml`).

Add these commands to your local workflow or set up a git alias to run them before committing documentation changes.

## Editing guidelines

1. **Update the source, not the copy.** If you spot a mismatch, fix the canonical file (e.g., `docs/providers.yaml`, `BrainarrSettings.cs`) and regenerate instead of patching the derived output.
2. **Keep doc styles focused.** README → high-level intro + doc map; docs/ → technical references; wiki → user-facing playbooks. Point across surfaces rather than duplicating text.
3. **Record rationale.** When you tune advanced settings or provider defaults, note the reason in `docs/VERIFICATION-RESULTS.md` so future releases can review the history.
4. **Link to tests/code.** If a doc describes behaviour covered by tests (token guard, plan cache), name the test class or method (e.g., `TokenBudgetGuardTests`) so contributors know where to validate.

## Review checklist for doc PRs

- [ ] Did you edit the canonical source and rerun any required generators?
- [ ] Does `pwsh ./build.ps1 --docs` pass?
- [ ] Are new links stable (avoid `[[wiki syntax]]`, prefer Markdown links)?
- [ ] Did you update the doc map or this governance page if the structure changed?

Keeping this contract updated ensures contributors—and any future automation—know how to keep README, docs/, and the wiki in sync.
