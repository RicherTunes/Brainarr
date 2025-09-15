# AGENTS

This file tracks persistent technical decisions and context for the Brainarr plugin repo, so agents and humans share the same memory when evolving CI/CD and code.

## CI Decision: Compile Against Lidarr "plugins" Branch
- Rationale: The plugin must compile against the exact APIs and assembly shape exposed by Lidarr’s `plugins` branch. Using a generic release tarball can drift (APIs, assembly versions), causing mismatches and flaky builds.
- Implementation: In CI we extract required assemblies from the Docker image tag dedicated to the plugins branch, referenced by `LIDARR_DOCKER_VERSION`.
  - Image: `ghcr.io/hotio/lidarr:${LIDARR_DOCKER_VERSION}`
  - Assemblies copied from `/app/bin` into `ext/Lidarr-docker/_output/net6.0/` and published as an artifact for matrix jobs.
- Fallback: Prefer Docker-based extraction everywhere. If a non-Linux runner lacks Docker, run a tar.gz fallback only if strictly necessary and pin versions to avoid drift.

## Conventions
- Keep `LIDARR_DOCKER_VERSION` current for the `plugins` branch.
- Do not fetch arbitrary Lidarr releases for CI unless intentionally testing cross-version compatibility.
- Matrix jobs consume the prepared assemblies artifact instead of re-fetching.

## Next Steps
- Enforce `shell: bash` on POSIX-style scripts across all OS matrix legs.
- Reduce duplication between CI and security-scan jobs.
- Wire a quick sanity build that fails fast if assemblies are missing.
