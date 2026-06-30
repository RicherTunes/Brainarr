# Release Process Overview

Brainarr releases are driven by local scripts and the Gitea-primary CI gate. This page collects the entry points so you can follow the single source of truth instead of re-copying every command.

## Canonical entry points

- `pwsh ./scripts/verify-local.ps1` or `bash ./scripts/verify-local.sh` — primary pre-release gate. This mirrors Gitea `CI / verify` and runs the full local build/test/package path.
- `pwsh ./scripts/new-release.ps1` or `bash ./scripts/new-release.sh` — interactive release helper. It checks prerequisites, runs tests, creates the tag, pushes it, and lets the repository's release automation take over.
- `gh run list --event push --limit 3` — optional way to inspect the tag-triggered automation after the push lands.

Each script writes detailed progress to the console; see the script files themselves in `scripts/` for implementation notes.

## Release checklist

Validation steps (tests, manual smoke, provider verification) now live in [`docs/RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md). Complete that checklist before tagging.

## Changelog and metadata

- Update [`CHANGELOG.md`](../CHANGELOG.md) using the keep-a-changelog format.
- Keep `plugin.json`, `manifest.json`, README badges, and wiki release notes in sync; the tag script handles this automatically if the changelog is ready.
- For manual adjustments after a release, amend the tag or open the release draft via GitHub and edit the generated notes.

## When to choose each path

| Scenario | Recommended path |
|----------|------------------|
| Standard release day | `pwsh ./scripts/new-release.ps1` or `bash ./scripts/new-release.sh` |
| Need to validate the package before tagging | `pwsh ./scripts/verify-local.ps1` or `bash ./scripts/verify-local.sh` |
| Re-run or inspect the tag-triggered automation | `gh run list --event push --limit 3` |
| Emergency manual hotfix | Follow the checklist, then run the release helper from a clean tree |

## What the automation performs

1. Restores Lidarr dependencies via `./setup.ps1`/`./setup.sh` if needed.
2. Builds and tests the plugin (same as `pwsh ./build.ps1 --setup --test --package`).
3. Packages the release ZIP and computes checksums.
4. Updates manifests/badges and pushes the tag.
5. Publishes the GitHub release with notes derived from `CHANGELOG.md`.

Refer back here instead of copying snippets into other docs—keeping these pointers aligned means we only edit one place when the tooling changes.
