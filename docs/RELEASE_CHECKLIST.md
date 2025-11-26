# Release Checklist (Pre‑Release Validation)

Use this list to confirm the release is ready **before** running `./.github/scripts/tag-release.sh <version>`.

## 1. Versioning & Notes

- [ ] `CHANGELOG.md` has an "Unreleased" section ready to roll into the new version.
- [ ] `docs/providers.yaml` and the README badges already mention the target version (the tag script promotes them).
- [ ] Documentation updates reviewed (README, wiki, docs) for the release scope.

## 2. Build & Test

- [ ] `pwsh ./build.ps1 --setup --test` (or `./build.sh --setup --test`) passes locally.
- [ ] Any quarantined tests have been reviewed; new skips are documented with TODO links.
- [ ] CI on `feat/roadmap-1.3.0` is green.

## 3. Manual Smoke (once per release line)

- [ ] Lidarr nightly (≥2.14.2.4786) with Brainarr vX.Y.Z starts cleanly and the import list appears in the UI.
- [ ] Primary provider path (local or cloud) returns a recommendation batch end-to-end.
- [ ] Failover path triggers by intentionally breaking the primary (e.g., stop Ollama service, revoke cloud key) and recovers.

## 4. Sanity of Observability & Security

- [ ] Prompt logs show the expected headroom and token guard fields.
- [ ] Structured logging redaction verified (API keys not present).
- [ ] Metrics export contains the `plan_cache` and `prompt` series introduced in 1.3.x.

## 5. Packaging Snapshot (optional if tag script succeeds locally)

- [ ] `pwsh ./build.ps1 --package` (or `./build.sh --package`) produces `Brainarr-<version>.net8.0.zip` + checksums.
- [ ] Inspect ZIP contents: `Lidarr.Plugin.Brainarr.dll` + `plugin.json` only.

## 6. Tag & Publish

- [ ] Run `./.github/scripts/tag-release.sh <version>`.
- [ ] Verify the workflow run completes in GitHub Actions.
- [ ] Review the generated GitHub release draft (notes, assets, version numbers) before announcing.

Keep this checklist in sync with [`docs/RELEASE_PROCESS.md`](RELEASE_PROCESS.md); add new items here instead of cloning instructions elsewhere.
