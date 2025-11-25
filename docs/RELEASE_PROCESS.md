# Release Process Overview

Brainarr releases are automated through scripts and GitHub Actions. This page collects the entry points so you can follow the single source of truth instead of re-copying every command.

## Canonical entry points

- `./.github/scripts/tag-release.sh <version>` — primary path. Creates the tag, bumps manifests, runs the full build/test/package workflow, and publishes the GitHub release.
- `./.github/scripts/quick-release.sh` — interactive helper that calls the tag script after letting you pick the version bump.
- `gh workflow run release.yml -f version=vX.Y.Z` — manual trigger when you need to retry the pipeline or run from automation.

Each script writes detailed progress to the console; see `scripts/README-release.md` in the `.github/scripts/` folder for implementation notes. (If that README is missing or stale, treat the scripts themselves as the authoritative logic.)

## Release checklist

Validation steps (tests, manual smoke, provider verification) now live in [`docs/RELEASE_CHECKLIST.md`](RELEASE_CHECKLIST.md). Complete that checklist before tagging.

## Changelog and metadata

- Update [`CHANGELOG.md`](../CHANGELOG.md) using the keep-a-changelog format.
- Keep `plugin.json`, `manifest.json`, README badges, and wiki release notes in sync; the tag script handles this automatically if the changelog is ready.
- For manual adjustments after a release, amend the tag or open the release draft via GitHub and edit the generated notes.

## When to choose each path

| Scenario | Recommended path |
|----------|------------------|
| Standard release day | `./.github/scripts/tag-release.sh <version>` |
| Need to preview bump options or pre-release suffixes | `./.github/scripts/quick-release.sh` |
| Re-run a failed publish without touching local tree | `gh workflow run release.yml -f version=vX.Y.Z` |
| Emergency manual hotfix | Follow the checklist, then use the tag script with the new version |

## What the automation performs

1. Restores Lidarr dependencies via `./setup.ps1`/`./setup.sh` if needed.
2. Builds and tests the plugin (same as `pwsh ./build.ps1 --setup --test --package`).
3. Packages the release ZIP and computes checksums.
4. Updates manifests/badges and pushes the tag.
5. Publishes the GitHub release with notes derived from `CHANGELOG.md`.

Refer back here instead of copying snippets into other docs—keeping these pointers aligned means we only edit one place when the tooling changes.
