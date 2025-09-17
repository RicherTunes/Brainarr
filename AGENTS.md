# AGENTS

This file tracks persistent technical decisions and context for the Brainarr Lidarr plugin so agents and humans share the same memory when evolving CI/CD and code.

## Repository Snapshot
- Canonical repository: [RicherTunes/Brainarr](https://github.com/RicherTunes/Brainarr); default branch is `main`.
- Local checkout uses Git worktrees inside `.worktrees/`; leave them intact when cleaning the repo.
- Remotes: `origin` (and alias `main`) both point at GitHub. Always sync and push via `origin/main` unless the user states otherwise.
- Key manifests live in `plugin.json`, `manifest.json`, and `Brainarr.Plugin/Brainarr.Plugin.csproj`.

## Branching & Status Discipline
- Before claiming a change is committed or pushed, run `git status -sb`, `git branch -vv`, and `git remote -v`; quote the real outputs and state explicitly whether a push already happened.
- Use feature branches for PR work; keep `main` clean and fast-forwardable to `origin/main`.
- If a user needs publishing guidance, confirm remotes/credentials exist, then share concrete commands (`git add`, `git commit`, `git push origin main`).
- Do not delete `.worktrees/main`; it underpins local and CI workflows.

## Local Setup & Build
- Always build against real Lidarr assemblies; never introduce stubs. Run `setup-lidarr.ps1` (Windows) or `setup-lidarr.sh` (POSIX) to fetch and build the Lidarr `plugins` branch into `ext/Lidarr`.
- Only set `LIDARR_PATH` when Lidarr lives outside `ext/Lidarr/_output/net6.0`; the build scripts auto-discover the default path.
- Primary entry points: `build.ps1` / `build.sh` (flags: `-Setup/--setup`, `-Test/--test`, `-Package/--package`, `-Deploy/--deploy`).
- Manual builds happen inside `Brainarr.Plugin/`; run `dotnet build -c Release` only after Lidarr assemblies are present.

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
- CI always compiles against Lidarr's `plugins` branch via Docker image `ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}`; extracted assemblies live in `ext/Lidarr-docker/_output/net6.0/` and feed matrix jobs.
- Keep `LIDARR_DOCKER_VERSION` current for the plugins branch; note bumps here when they occur.
- Prefer Docker-based extraction everywhere; fall back to tarballs only when Docker is unavailable and versions are pinned.
- Open CI TODOs: enforce `shell: bash` on POSIX GitHub Actions legs, deduplicate build vs security-scan logic, and add a fast-fail sanity build that checks assemblies are present.

## Agent Workflow Guardrails
- When editing this file, read it once for context, make the required change, and then respond to the user without re-opening AGENTS.md unless they explicitly ask for a review.
- If a user asks whether changes were submitted or pushed, run `git status -sb` and `git branch -vv` (plus `git remote -v` when relevant), summarize the actual output, and state plainly whether a push already happened.
- When a user needs the update on GitHub, either push it yourself (if credentials and remotes are in place) or explain why you cannot, then list the exact commands they can run (configure `origin`, checkout `main`, fast-forward/merge, and `git push origin main`).
- When reporting status, include the current branch name and its upstream so the user never has to infer where HEAD points.
