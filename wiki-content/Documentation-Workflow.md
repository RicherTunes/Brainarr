# Documentation Workflow

> Follow this checklist whenever you touch README/docs/wiki content so everything stays generated from the same inputs.

## 1. Identify the source of truth

| Task | Edit this | Regenerate |
|------|-----------|------------|
| Provider status, notes, defaults | `docs/providers.yaml` | `pwsh ./scripts/sync-provider-matrix.ps1` |
| Release notes & verification | `CHANGELOG.md`, `docs/VERIFICATION-RESULTS.md` | (manual) |
| Advanced defaults | `Brainarr.Plugin/BrainarrSettings.cs`, planner/renderer tests | (manual doc updates) |
| Operations guidance | `docs/USER_SETUP_GUIDE.md`, `wiki-content/Operations.md` | (manual) |

Refer back to [docs/DOCS_STRATEGY.md](../docs/DOCS_STRATEGY.md) for the canonical mapping.

## 2. Local workflow

1. Make changes to the canonical file(s).
2. If `docs/providers.yaml` changed, run:

   ```pwsh
   pwsh ./scripts/sync-provider-matrix.ps1
   ```

   Commit the updated README/doc/wiki fragments alongside the YAML.
3. Run the docs lint:

   ```pwsh
   pwsh ./build.ps1 -Docs
   ```

   Fix any reported `[[link]]` syntax or other issues before committing.
4. (Optional) Build everything for a final confidence check:

   ```pwsh
   pwsh ./build.ps1 --docs
   ```

## 3. Wiki publication (manual for now)

1. After merging into the release branch, copy files from `wiki-content/` into the GitHub wiki clone (`git clone git@github.com:RicherTunes/Brainarr.wiki.git`).
2. Commit and push to the wiki repository.
3. Future automation target: `pwsh ./scripts/publish-wiki.ps1` (tracked in `docs/DOCS_STRATEGY.md`).

## 4. Pull requests & review

- Include in the PR body which canonical file you changed and confirm `pwsh ./build.ps1 -Docs` passed.
- Mention any updates to `docs/providers.yaml`, `CHANGELOG.md`, or `docs/VERIFICATION-RESULTS.md` explicitly for reviewers.
- If the change affects operational behaviour (new flags, provider defaults), update the [Operations Playbook](Operations.md) and `docs/VERIFICATION-RESULTS.md` in the same PR.

## 5. Common pitfalls

- Editing generated provider tables by hand (they revert on the next sync).
- Leaving `[[wiki-style]]` links in markdown—doc lint now fails these.
- Forgetting to update the README doc map or [docs/DOCS_STRATEGY.md](../docs/DOCS_STRATEGY.md) when adding a new guide.

Keeping this workflow handy ensures every contributor—human or agent—follows the same process to maintain documentation quality.
