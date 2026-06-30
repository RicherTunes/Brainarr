# Release Checklist (Pre‑Release Validation)

Use this list to confirm the release is ready **before** running `pwsh ./scripts/new-release.ps1` or `bash ./scripts/new-release.sh`.

## 1. Versioning & Notes

- [ ] `CHANGELOG.md` has an "Unreleased" section ready to roll into the new version.
- [ ] `docs/providers.yaml` and the README badges already mention the target version (the tag script promotes them).
- [ ] Documentation updates reviewed (README, wiki, docs) for the release scope.

## 2. Build & Test

- [ ] `pwsh ./build.ps1 --setup --test` (or `./build.sh --setup --test`) passes locally.
- [ ] `pwsh ./scripts/verify-local.ps1` (or `bash ./scripts/verify-local.sh`) passes and matches the Gitea `CI / verify` gate.
- [ ] Any quarantined tests have been reviewed; new skips are documented with TODO links.
- [ ] CI on `main` (or the branch you plan to tag) is green in Gitea for `CI / secret-scan`, `CI / lint`, and `CI / verify`.

## 3. Manual Smoke (once per release line)

- [ ] Lidarr nightly (≥3.0.0.4855 plugins branch) with Brainarr vX.Y.Z starts cleanly and the import list appears in the UI.
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

- [ ] Run `pwsh ./scripts/new-release.ps1` or `bash ./scripts/new-release.sh` from a clean tree.
- [ ] Confirm the tag push starts the release automation and the published release page has the expected notes and assets.
- [ ] Review the generated release page before announcing.

Keep this checklist in sync with [`docs/RELEASE_PROCESS.md`](RELEASE_PROCESS.md); add new items here instead of cloning instructions elsewhere.
