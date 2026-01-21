# AGENTS

This file tracks persistent technical decisions and context for the Brainarr Lidarr plugin so agents and humans share the same memory when evolving CI/CD and code.

## Repository Snapshot
- Canonical repository: [RicherTunes/Brainarr](https://github.com/RicherTunes/Brainarr); default branch is `main`.
- Local checkout is a standard Git clone; `.worktrees/` and `_plugins/` directories should not exist (they are gitignored).
- Remotes: `origin` (and alias `main`) both point at GitHub. Always sync and push via `origin/main` unless the user states otherwise.
- Key manifests live in `plugin.json`, `manifest.json`, and `Brainarr.Plugin/Brainarr.Plugin.csproj`.

## Branching & Status Discipline
- Before claiming a change is committed or pushed, run `git status -sb`, `git branch -vv`, and `git remote -v`; quote the real outputs and state explicitly whether a push already happened.
- Use feature branches for PR work; keep `main` clean and fast-forwardable to `origin/main`.
- If a user needs publishing guidance, confirm remotes/credentials exist, then share concrete commands (`git add`, `git commit`, `git push origin main`).
- Do not create `.worktrees/` or `_plugins/` directories; they are gitignored and were removed from tracking.

## Local Setup & Build
- One-command bootstrap: run `./setup.ps1` (Windows/PowerShell) or `./setup.sh` (POSIX) from repo root; they clone or update Lidarr, build it, cache `LIDARR_PATH`, restore the Brainarr solution, and build the plugin. On POSIX run `chmod +x setup.sh` once before invoking it.
- `setup-lidarr.ps1` remains available for advanced scenarios (alternate branches, `-SkipBuild`); it also records the resolved Lidarr path in `ext/lidarr-path.txt` for reuse.
- Always build against real Lidarr assemblies; never introduce stubs. The setup scripts ensure `ext/Lidarr/_output/net8.0` exists and set `LIDARR_PATH` automatically.
- Only set `LIDARR_PATH` manually when Lidarr lives outside `ext/Lidarr/_output/net8.0`; otherwise let the setup tooling manage it.
- Primary entry points: `build.ps1` / `build.sh` (flags: `-Setup/--setup`, `-Test/--test`, `-Package/--package`, `-Deploy/--deploy`) once Lidarr is prepared.
- Manual builds happen inside `Brainarr.Plugin/`; run `dotnet build -c Release` only after Lidarr assemblies are present.

## Safe Cleanup & Troubleshooting
- Confirm you are in the Brainarr repo (check `Get-Location`, `git status -sb`, `git remote -v`) before running commands, especially ones copied from other projects.
- When external tooling (npm, docker, etc.) fails, capture the exact error output, summarize it for the user, and pause. Do not guess by deleting directories, removing tracked files, or installing random scripts.
- To refresh Lidarr assemblies, rerun `./setup.ps1` or `./setup.sh` instead of manually pruning `ext/`. Never delete tracked directories like `ext/Lidarr` without explicit approval.
- Avoid adding ad-hoc bootstrap scripts (e.g., `dotnet-install.sh`). Stick to repository-provided tooling; surface missing prerequisites to the user instead.

## Testing & Quality Gates
- Preferred orchestration: `test-local-ci.ps1` (flags `-SkipDownload`, `-ExcludeHeavy`, `-GenerateCoverageReport`, `-InstallReportGenerator`).
- Quick loops: `dotnet test` with `--filter Category=Unit`, `Category=Integration`, or `Category=EdgeCase` as needed.
- Coverage artifacts land in `TestResults/`; regenerate with `scripts/generate-coverage-report.ps1 -InstallTool` when sharing reports.
- Do not skip suites without explicit user approval; document any skipped tests and why.

## Documentation Map
- Overview: `README.md`.
- Build and environment details: `BUILD.md`, `BUILD_REQUIREMENTS.md`, `DEVELOPMENT.md`.
- Release planning: `CHANGELOG.md`, `docs/ROADMAP.md`.
- Additional guides live under `docs/` and `wiki-content/`; update both when touching public docs.

## Release & Versioning
- Keep `plugin.json`, `manifest.json`, README badges, and `CHANGELOG.md` aligned when bumping versions.
- Tag releases from `main` only after CI passes against real Lidarr assemblies.
- Update provider verification notes in README/docs when changing supported services.

## CI/CD Essentials
- CI always compiles against Lidarr's `plugins` branch via Docker image `ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}`; extracted assemblies live in `ext/Lidarr-docker/_output/net8.0/` and feed matrix jobs.
- Keep `LIDARR_DOCKER_VERSION` current for the plugins branch; note bumps here when they occur.
- Current default: `pr-plugins-2.14.2.4786` (updated 2025-09-24).
- Prefer Docker-based extraction everywhere; fall back to tarballs only when Docker is unavailable and versions are pinned.
- Open CI TODOs: deduplicate build vs security-scan logic. POSIX jobs now default to `shell: bash`, and a fast-fail sanity build exists.

## Agent Workflow Guardrails
- When editing this file, read it once for context, make the required change, and then respond to the user without re-opening AGENTS.md unless they explicitly ask for a review.
- If a user asks whether changes were submitted or pushed, run `git status -sb` and `git branch -vv` (plus `git remote -v` when relevant), summarize the actual output, and state plainly whether a push already happened.
- When a user needs the update on GitHub, either push it yourself (if credentials and remotes are in place) or explain why you cannot, then list the exact commands they can run (configure `origin`, checkout `main`, fast-forward/merge, and `git push origin main`).
- When reporting status, include the current branch name and its upstream so the user never has to infer where HEAD points.
