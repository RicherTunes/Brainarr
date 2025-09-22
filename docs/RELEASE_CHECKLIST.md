# Release Checklist (Pre‑Release Validation)

This checklist covers validation steps to perform before tagging and publishing a new Brainarr release.

## 1. Versioning & Metadata

- [ ] Confirm `plugin.json` version (remains 1.2.4 until tests complete)
- [ ] Ensure `CHANGELOG.md` Unreleased section accurately lists changes
- [ ] Verify docs links in `plugin.json` (website, supportUri, changelogUri) are valid

## 2. Build & Tests

- [ ] `dotnet restore && dotnet build -c Release`
- [ ] `dotnet test -c Release` (expect all pass; some long tests are skipped by design)
- [ ] Confirm no temp files or large artifacts are tracked by Git (see .gitignore)

## 3. Lidarr Integration (Manual Smoke Tests)

- [ ] Confirm Lidarr version: 2.14.1.4716+ and branch = nightly (plugins)
- [ ] Install plugin via GitHub URL or manual copy of `Lidarr.Plugin.Brainarr.dll` + `plugin.json`
- [ ] Restart Lidarr; verify “Loaded plugin: Brainarr” in logs
- [ ] Check Settings > Import Lists > Add > Brainarr visible

## 4. Provider Sanity (Local)

- [ ] Ollama configured; Get 5–10 recs. Verify:
  - [ ] JSON parsing succeeds (no empty arrays)
  - [ ] Sanitized fields (no tags, no injection patterns)
  - [ ] Iterative Top‑Up fills to target when duplicates occur
- [ ] LM Studio basic flow, similar checks as Ollama

## 5. Provider Sanity (Cloud)

- [ ] OpenRouter: basic recs return; structured outputs parsed when available
- [ ] OpenAI/Gemini/Anthropic: minimal smoke (single run) per provider if configured
- [ ] Non‑responsive provider handling: verify failover to next provider

## 6. Rate Limiting & Stability

- [ ] Burst/spacing visually consistent in logs for configured rates
- [ ] Throttling messages appear when expected (debug level)
- [ ] No long stalls or starvation when multiple providers are used

## 7. MusicBrainz Validation

- [ ] Artist validation returns expected true/false for known artists
- [ ] Artist+Album search returns matches; no 429 spam (user‑agent present)
- [ ] MBID resolution enriching sample recs works with caching

## 8. Security & Sanitization

- [ ] Recommendations: malicious Artist/Album inputs are rejected
- [ ] Reason/Genre sanitized (no HTML/script remains)
- [ ] Strict vs relaxed JSON parsing: relaxed only for provider payloads

## 9. Packaging (Dry‑Run)

- [ ] `dotnet publish -c Release -o dist/`
- [ ] Ensure `dist/` contains `Lidarr.Plugin.Brainarr.dll` and `plugin.json`
- [ ] Exclude unneeded files from release assets

## 10. Tag & Release (Do not execute until sign‑off)

- [ ] Update `plugin.json` version to next (e.g., 1.2.4)
- [ ] Move Unreleased notes to `## [1.2.4] - YYYY-MM-DD` in CHANGELOG
- [ ] `git tag -a v1.2.4 -m "..." && git push origin v1.2.4`
- [ ] Create GitHub Release, attach `dist/` artifacts, paste CHANGELOG notes

Notes:

- Keep debug logging enabled when doing smoke tests to observe rate limiting and sanitization behavior.
- Prefer local providers for thorough validation if cloud keys are limited.
