# Brainarr Library Healer

Library Healer is the first read-only milestone of the broader Brainarr Library Doctor track: a conservative diagnostic layer for Lidarr-managed track files. Milestone A1 detects files where Lidarr's own tag reader reports a missing or zero duration and records evidence for review.

## A1 Scope

A1 can:
- enumerate Lidarr artists and track files;
- call Lidarr's `IAudioTagService.ReadTags(string)` on each `TrackFile.Path`;
- classify read-only evidence as `FalsePositive`, `TagReaderSymptom`, or `NeedsHumanReview` in the default A1 scan;
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

If the tag reader reports a busy state after an earlier timed-out read, A1 fails the current scan safely, preserves already completed findings from the batch, and does not fingerprint or classify the busy file.

## Next Milestones

A2 may add richer probe evidence. A3 may add lossless repair-in-place only after a separate design review, crash-recovery journal, fixture matrix, and explicit opt-in.
