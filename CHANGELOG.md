# Changelog

All notable changes to this project will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [Unreleased]

Planned: minor improvements post 1.2.2

- Repair: MusicBrainzService was corrupted and caused compile errors; rebuilt with TTL caches, proper rate limiting, and safe JSON parsing
- Change: RateLimiter standardized to leaky‑bucket minimum spacing for more predictable throttling across providers
- Security: Introduced `SecureJsonSerializer.ParseDocumentRelaxed` for provider outputs; strict mode retains heuristic checks for known attack strings; preserved size/depth protections in both modes
- Security: Sanitize‑first pipeline for recommendations; reject records with malicious artist/album in raw input; keep safe sanitized non‑critical fields (reason/genre)
- Tests: Stabilized timing‑sensitive tests (Windows/CI); normalized line endings and Unicode escaping for golden JSON; added CI‑friendly thresholds
- Docs: Expanded SECURITY.md (strict vs relaxed JSON, sanitization flow); refreshed PR analysis report
- Repo Hygiene: Removed temp/debug artifacts and added .gitignore rules for tmp/backup/test logs/OS files

### Notes

- Plugin version remains `1.2.1` in `plugin.json` until release is cut and validated.
- See `docs/RELEASE_CHECKLIST.md` for validation steps prior to tagging.

## [1.2.2] - 2025-09-08

- fix(settings): tolerate legacy Top‑Up Stop Sensitivity values
  - Added a custom JSON converter to map historical/synonym values (e.g., "balanced", "medium") to current enum members (maps to "normal").
  - Resolves deserialization error: could not convert to StopSensitivity at $.topUpStopSensitivity.
  - Backwards‑compatible; no database migration required.

## [1.2.1] - Timeout + provider scope + docs

- See commit history for details.

[Unreleased]: https://github.com/RicherTunes/Brainarr/compare/main...HEAD
[1.2.2]: https://github.com/RicherTunes/Brainarr/compare/v1.2.1...v1.2.2
[1.2.1]: https://github.com/RicherTunes/Brainarr/compare/v1.2.0...v1.2.1
