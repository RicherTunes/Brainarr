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

- `healer/scan`: runs one bounded read-only diagnostic batch and stores current findings. It defaults to 100 files, caps at 500 files, supports `artistId`, `afterTrackFileId`, and `maxSeconds`, and returns `truncated=true` plus `nextAfterTrackFileId` when more files remain and the cursor is safe to use.
- `healer/getfindings`: returns recent findings with redacted paths, an advisory A2 treatment plan per finding, `affectedTrackCount` when a finding coalesces more than one track file, and summary counts for the returned treatment plans. It supports optional read-only triage filters: `workflow`, `risk`, `blockedReason`, and `authorized`. `workflow`, `risk`, and `blockedReason` accept comma-separated, case-insensitive values. Unknown treatment filter values are ignored; if no supplied values are recognized, that filter is not applied. `authorized` accepts `true` or `false`; malformed boolean values are ignored. Recognized filters apply before the output `limit`, and the `summary` describes only the returned filtered findings.
- `healer/getfieldcatalog`: returns static field-sensitivity metadata for the `healer/getfindings` output contract. It is read-only and does not scan files, read findings, mutate Lidarr, or contact providers.
- `healer/clearfindings`: clears Brainarr-owned findings.

## Safety Model

The A1/A2 implementation is intentionally diagnostic. It includes architecture tests that block references to Lidarr mutation APIs, command queue actions, and media file mutation operations from the healing subsystem.

The default scan and A2 triage projection do not contact AI providers and do not use external media tooling. They only read Lidarr-managed file metadata, record Brainarr-owned diagnostic evidence, and return advisory treatment plans for later milestones.

## Path Privacy

Default action output returns `basename#hash`, where `hash` is the first 12 hex characters of a SHA-256 hash of the normalized full path. This lets repeated findings correlate without exposing full local folder structure in screenshots or logs.

The persisted finding store uses the same redacted path shape for local diagnostic evidence. Raw media paths may be read in process to open the file, but A1 should not persist them in diagnostic records.

A1 sanitizes path-like values at both the store boundary and the action boundary. This includes stale or hand-edited values that already look like `name#hash` but still contain Windows, UNC, or relative path material before the hash.

The A2 action projection treats persisted findings as tainted input. Suspicious single-token metadata or command-like strings are redacted from output evidence, generic reader/probe exception messages are replaced with a fixed redaction token, and malformed stored records are surfaced consistently as `NeedsHumanReview` with `MALFORMED_FINDING_RECORD`.

## Scan Behavior

Scans are bounded and resumable. Whole-library scans enumerate all target artists before applying the global track-file cursor so low-ID files from later artists are not skipped, while retaining only the bounded low-ID lookahead needed for the current batch. If `maxSeconds` expires during whole-library candidate gathering, A1 stops before fetching more artist file lists, reports `truncated=true`, and omits `nextAfterTrackFileId` because unseen artists may still contain lower track-file IDs. Requested-artist scans may stop after one lookahead item so the action can report truncation without scanning more than the configured batch.

Confirmed missing paths are recorded as `PathInconsistency` with `FILE_MISSING` evidence, redacted path identity, and Lidarr's last known file size and modified timestamp. Inconclusive path probes, including timeouts, access denied results, invalid paths, unavailable parents, and transient storage/import churn, are recorded as `NeedsHumanReview` with `PATH_PROBE_INCONCLUSIVE` plus the specific reason. A1 does not invoke TagLib or run a full file fingerprint read for confirmed missing or inconclusive path probes.

Files with a positive TagLib duration but missing core tags are recorded as `TagMetadataIssue`. The stored metadata evidence is limited to booleans and generic missing-field names such as `title`, `album`, or `musicBrainzId`; it does not store artist names, album titles, track titles, or raw MusicBrainz values. Persistence and `healer/getfindings` output recompute missing fields from the booleans only, normalize reason codes through the fixed diagnostic vocabulary, downgrade stale metadata labels when the booleans are complete, and redact metadata-bearing error messages, so stale or malformed diagnostic records cannot return arbitrary tag values.

The action's `maxSeconds` value is enforced through candidate-gathering budget checks and the runner's bounded path-probe timeouts, not by canceling the scan token. Synchronous Lidarr host calls that enumerate files or read tags cannot be preempted mid-call, so the budget is checked between those calls. If the scan budget expires while a path probe is waiting, A1 records the probe as a timeout finding for human review; an external cancellation token remains an abort signal and does not persist partial findings.

If the tag reader reports a busy state after an earlier timed-out read, A1 fails the current scan safely, preserves already completed findings from the batch, and does not fingerprint or classify the busy file.

If a storage root is offline or otherwise fails the root-level availability check, the scan coalesces the root into a single `NeedsHumanReview` finding with `STORAGE_ROOT_OFFLINE` rather than emitting one finding per missing track file. That finding reports `affectedTrackCount` so operators can see the blast radius without flooding the review queue.

## A2 Scope

A2 adds a read-only treatment plan to each finding returned by `healer/getfindings`. The plan includes `candidateWorkflow`, confidence, risk, freshness, fixed blocked reasons, required evidence, required policy gates, rationale codes, and `executionAuthorization.authorized=false`. The response `summary` counts workflows, risks, workflow-by-risk, authorization state, and all non-zero blocked reasons for the returned findings. Persisted freshness values are allowlisted; missing, malformed, stale, or hand-edited values fail closed to `unknown` and require human review.

A2 does not repair, retag, reacquire, rescan, delete, replace, import, run `ffmpeg`, or call AI providers. Its treatment plans are advisory evidence planning only; future mutating milestones must define separate authorization, preflight, journal, backup, and rollback contracts.

## A2.5 Scope

A2.5 begins with read-only triage filtering on `healer/getfindings`. The first filter slice lets operators narrow returned findings by treatment workflow, risk, blocked reason, and authorization state without changing stored findings, Lidarr state, media files, treatment-plan authorization, or scan behavior.

The first evidence contract golden pack snapshots the sanitized `healer/getfindings` projection and the treatment vocabulary. It covers representative repair candidates, tag-repair candidates, privacy redaction, summary counts, and malformed finding fail-closed behavior so later UI, export, AI review, and execution planning work must make any reinterpretation of A2 advisory fields visible during contract review.

The first field-sensitivity catalog is available through `healer/getfieldcatalog`. It is static, read-only metadata for the `healer/getfindings` response and does not scan files, read findings, mutate Lidarr, or contact providers. Each known output field is annotated with a sensitivity class plus local diagnostic export, shareable support export, and AI prompt booleans. Non-public fields such as redacted paths, stable path hashes, Lidarr-local identifiers, file size/duration/timestamps, and diagnostic error tokens are blocked from shareable support export and AI prompts by default.

## Next Milestones

A2.5 should continue hardening the read-only evidence layer before any repair dry-run work. The highest-ROI pulls are revalidation, schema migration, provenance, TTL, read-only kill switch, fingerprint policy, Lidarr state diffing, recurrent storage/root health conformance, host conformance, redaction verification, field-sensitivity expansion for future exports and AI packets, classifier replay benches, filter expansion for freshness/review lifecycle, probe collection, targeted decode verification, fixture truth tables, risk-prioritized review queues, Lidarr configuration conformance, ownership-boundary mapping, and edition/variant protected scopes.

A3 may add repair dry-runs and verified repair-in-place only after a separate design review, dry-run verification contract, crash-recovery journal, fixture matrix, rollback guide, and explicit opt-in. A3 must define its own execution authorization contract; it cannot inherit A2 treatment plans as permission to write.

A4 may add reacquire orchestration for genuinely unrecoverable files only after decode evidence, album-wide scope disclosure, recycle-bin configuration, and Lidarr search dry-run behavior are tested. A4 must also define a separate execution authorization contract.

The longer-term scored backlog lives in `docs/superpowers/specs/2026-07-01-library-doctor-future-tools-roadmap.md`.
