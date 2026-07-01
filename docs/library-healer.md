# Brainarr Library Healer

Library Healer is the first read-only milestone of the broader Brainarr Library Doctor track: a conservative diagnostic layer for Lidarr-managed track files. Milestone A1 detects missing on-disk paths, files where Lidarr's own tag reader reports a missing or zero duration, and readable files with missing core tag metadata, then records evidence for review.

## A1 Scope

A1 can:
- enumerate Lidarr artists and track files;
- run a bounded path existence precheck before invoking Lidarr's tag reader;
- detect confirmed-missing `TrackFile.Path` entries without invoking TagLib or fingerprint reads;
- call Lidarr's `IAudioTagService.ReadTags(string)` only after the precheck confirms the path exists;
- record tag-presence evidence for title, artist, album, and MusicBrainz identifiers without storing raw tag values;
- classify read-only evidence as `FalsePositive`, `PathInconsistency`, `TagMetadataIssue`, `TagReaderSymptom`, or `NeedsHumanReview` in the default A1 scan;
- store findings under Brainarr's plugin AppData directory;
- show redacted paths by default;
- resume scans with `afterTrackFileId`;
- preserve completed findings from the current batch if a prior tag-reader timeout leaves the reader busy.

A1 cannot:
- repair files;
- import files;
- delete files;
- replace files;
- enqueue Lidarr rescans or searches;
- write tags;
- call AI providers.

## Actions

- `healer/scan`: runs one bounded read-only diagnostic batch and stores current findings. It defaults to 100 files, caps at 500 files, supports `artistId`, `afterTrackFileId`, and `maxSeconds`, and returns `truncated=true` plus `nextAfterTrackFileId` when more files remain.
- `healer/getfindings`: returns recent findings with redacted paths.
- `healer/clearfindings`: clears Brainarr-owned findings.

## Safety Model

The A1 implementation is intentionally diagnostic. It includes architecture tests that block references to Lidarr mutation APIs, command queue actions, and media file mutation operations from the healing subsystem.

The default A1 scan does not contact AI providers and does not use external media tooling. It only reads Lidarr-managed file metadata, records Brainarr-owned diagnostic evidence, and leaves remediation decisions for later milestones.

## Path Privacy

Default action output returns `basename#hash`, where `hash` is the first 12 hex characters of a SHA-256 hash of the normalized full path. This lets repeated findings correlate without exposing full local folder structure in screenshots or logs.

The persisted finding store uses the same redacted path shape for shareable evidence. Raw media paths may be read in process to open the file, but A1 should not persist them in diagnostic records.

A1 sanitizes path-like values at both the store boundary and the action boundary. This includes stale or hand-edited values that already look like `name#hash` but still contain Windows, UNC, or relative path material before the hash.

## Scan Behavior

Scans are bounded and resumable. Whole-library scans enumerate all target artists before applying the global track-file cursor so low-ID files from later artists are not skipped. Requested-artist scans may stop after one lookahead item so the action can report truncation without scanning more than the configured batch.

Confirmed missing paths are recorded as `PathInconsistency` with `FILE_MISSING` evidence, redacted path identity, and Lidarr's last known file size and modified timestamp. Inconclusive path probes, including timeouts, access denied results, invalid paths, unavailable parents, and transient storage/import churn, are recorded as `NeedsHumanReview` with `PATH_PROBE_INCONCLUSIVE` plus the specific reason. A1 does not invoke TagLib or run a full file fingerprint read for confirmed missing or inconclusive path probes.

Files with a positive TagLib duration but missing core tags are recorded as `TagMetadataIssue`. The stored metadata evidence is limited to booleans and generic missing-field names such as `title`, `album`, or `musicBrainzId`; it does not store artist names, album titles, track titles, or raw MusicBrainz values. Persistence and `healer/getfindings` output recompute missing fields from the booleans only, normalize reason codes through the fixed diagnostic vocabulary, downgrade stale metadata labels when the booleans are complete, and redact metadata-bearing error messages, so stale or malformed diagnostic records cannot return arbitrary tag values.

The action's `maxSeconds` value is enforced through the runner's bounded operation timeouts, not by canceling the scan token. If the scan budget expires while a path probe is waiting, A1 records the probe as a timeout finding for human review; an external cancellation token remains an abort signal and does not persist partial findings.

If the tag reader reports a busy state after an earlier timed-out read, A1 fails the current scan safely, preserves already completed findings from the batch, and does not fingerprint or classify the busy file.

## Next Milestones

A2 is planned as a read-only triage and evidence-planning layer. It should add a deterministic treatment plan to each finding, including the candidate workflow, confidence, risk, blocked reasons, required evidence, required policy gates, and an explicit `executionAuthorization.authorized=false` result for every treatment. A2 must not repair, retag, reacquire, rescan, delete, replace, import, or call AI providers.

A3 may add repair dry-runs and verified repair-in-place only after a separate design review, dry-run verification contract, crash-recovery journal, fixture matrix, rollback guide, and explicit opt-in. A3 must define its own execution authorization contract; it cannot inherit A2 treatment plans as permission to write.

A4 may add reacquire orchestration for genuinely unrecoverable files only after decode evidence, album-wide scope disclosure, recycle-bin configuration, and Lidarr search dry-run behavior are tested. A4 must also define a separate execution authorization contract.

The longer-term scored backlog lives in `docs/superpowers/specs/2026-07-01-library-doctor-future-tools-roadmap.md`. The next pulls should finish A2 projection conformance first, then add read-only hardening tools such as revalidation, schema migration, provenance, TTL, read-only kill switch, fingerprint policy, state diffing, storage/root health audits, host conformance, redaction verification, triage filters, probe collection, targeted decode verification, and fixture truth tables.
