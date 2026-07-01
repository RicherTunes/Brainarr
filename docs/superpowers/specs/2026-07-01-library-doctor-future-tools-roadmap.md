# Library Doctor Future Healing and Track Management Roadmap

Status: Draft backlog
Date: 2026-07-01
Owner: RicherTunes / Brainarr

## Purpose

This backlog captures future Brainarr Library Doctor and track-management ideas beyond the committed A1/A2/A3/A4 path. It is intentionally ranked by usefulness, complexity, and ROI so each milestone can pull the next smallest high-value tool instead of jumping straight to destructive repair or AI-heavy automation.

Scores use a 1-5 scale:

- Usefulness: user impact if the feature works.
- Complexity: implementation and safety difficulty. A higher number is harder.
- ROI: expected value relative to complexity and risk.

The preferred path is deterministic first, AI only where ambiguity genuinely needs judgment, and no file-affecting action without a separate authorization, preflight, journal, backup, and rollback contract.

## Priority Bands

- Near-term hardening: A2.5 read-only features that improve evidence quality and operator workflows.
- Repair foundation: A3 dry-run, ledger, and eventually verified repair-in-place.
- Reacquire foundation: A4 dry-run and carefully authorized execution for decode-proven unrecoverable files.
- Data management: tag, release, duplicate, and consistency tools that help maintain the library without deleting data.
- AI-assisted judgment: later features where the model explains ambiguous cases but deterministic gates still decide allowed actions.

## Ranked Backlog

| Rank | Feature | Purpose | Usefulness | Complexity | ROI | Likely milestone | Safety caveat |
| ---: | --- | --- | ---: | ---: | ---: | --- | --- |
| 1 | Finding revalidation loop | Re-stat and re-read current findings so stale false alarms auto-resolve. | 5 | 2 | 5 | A2.5 | Read-only; freshness changes never authorize action. |
| 2 | Redacted evidence bundle export | Produce shareable support/debug bundles from sanitized evidence. | 5 | 2 | 5 | A2.5 | No raw paths, tag values, MBIDs, provider prompts, or exception text. |
| 3 | Triage dashboard/work queues | Filter by workflow, risk, blocked reason, freshness, and review state. | 5 | 3 | 5 | A2.5 | Review state stays separate from execution authorization. |
| 4 | Read-only probe collector | Add `ffprobe` stream/container evidence for better candidate planning. | 5 | 3 | 5 | A2.5 | Structured arguments, timeouts, stdout/stderr caps, no network inputs. |
| 5 | Fixture truth-table lab | Lock expected TagLib/probe/decode/classifier outcomes for real fixtures. | 5 | 3 | 5 | A2.5/A3 | Fixtures must prove intact audio never becomes destructive eligibility. |
| 6 | Storage/root health audit | Detect offline roots, permissions, symlinks, reparse points, low space, and path casing issues. | 5 | 2 | 5 | A2.5 | Audit only; no path rewrites, mounts, moves, or rescans. |
| 7 | Plugin-owned scan scheduler | Run budgeted recurring scans with pause/backoff and resumable cursors. | 4 | 3 | 4 | A2.5 | Timer only requests bounded scan work; no inline long-running timer scans. |
| 8 | Batch policy simulator | Show what a future batch would require: backups, free space, gates, and risk. | 4 | 3 | 4 | A2.5/A3 | Simulation cannot emit executable file or Lidarr commands. |
| 9 | Repair dry-run sandbox | Create verified temp outputs without touching originals. | 5 | 4 | 5 | A3 | Temp output only; no replace until a separate write authorization exists. |
| 10 | Operation ledger/recovery console | Inspect planned/applying/applied/failed journal states and recovery needs. | 5 | 4 | 5 | A3 | Console must not mutate without an explicit authorized transition. |
| 11 | Verified repair-in-place | Backup, repair/remux, swap, re-read, and rollback under a journal. | 5 | 5 | 4 | A3.5 | Separate authorization, same-volume proof, backup-first, post-verify. |
| 12 | Full-decode integrity sweep | Detect valid-header but truncated or undecodable files deterministically. | 4 | 4 | 4 | A4/A5 | Explicit slow mode with bounded concurrency and resumable progress. |
| 13 | Reacquire dry-run planner | Preview Lidarr search/reacquire options for unrecoverable files. | 5 | 4 | 4 | A4 | No queue push; dry-run result is evidence only. |
| 14 | Reacquire execution | Opt-in reacquire for decode-proven unrecoverable files. | 4 | 5 | 3 | A4.5 | Recycle bin, album-wide disclosure, fresh evidence, explicit opt-in. |
| 15 | Tag canonicalization dry-run | Compare weak tags to Lidarr/MusicBrainz canonical data. | 4 | 3 | 4 | C1 | AI only for ambiguous release judgment, never authorization. |
| 16 | Tag repair apply | Write approved metadata fixes with before/after diff and tag backup. | 4 | 4 | 4 | C2 | Requires canonical ID proof, backup, rollback, and explicit approval. |
| 17 | MusicBrainz release migration assistant | Identify albums attached to the wrong release and propose safer release remaps. | 4 | 4 | 4 | C1/B | Review-only until Lidarr API semantics and rollback are proven. |
| 18 | Release/album consistency audit | Detect track count, duration, edition, and release identity drift. | 4 | 4 | 4 | C1/B | AI may summarize ambiguity; deterministic gates decide state. |
| 19 | Duplicate/near-duplicate analyzer | Find duplicate tracks by fingerprint, duration, and identity hints. | 4 | 3 | 4 | C1 | Review-only; never auto-delete or replace. |
| 20 | Orphan and DB/file mismatch audit | Find unmanaged files in artist folders and missing DB records for present files. | 4 | 3 | 4 | C1 | No import/delete; produce review bundles only. |
| 21 | Rename and retag simulator | Preview tag/path normalization and Lidarr naming impacts before any write. | 4 | 3 | 4 | C1/C2 | Simulation only; no filesystem writes or Lidarr rename commands. |
| 22 | Quality/profile drift auditor | Detect files below wanted quality/profile, unexpected codecs, or lossy/lossless drift. | 3 | 3 | 3 | C1 | Report only; upgrades go through normal Lidarr policy gates. |
| 23 | Cover art and embedded artwork audit | Detect missing, huge, corrupt, or inconsistent embedded artwork. | 3 | 3 | 3 | C1/C2 | Artwork writes require tag backup and explicit opt-in. |
| 24 | Import Brain identity assistant | Adjudicate ambiguous manual import/release identity cases with evidence and confidence. | 5 | 5 | 4 | B | AI proposes only; deterministic gate verifies IDs and empty slots. |
| 25 | Tagging Brain | Suggest canonical tag fixes and release choices for weak metadata. | 4 | 5 | 3 | C | AI cannot write tags or set execution authorization. |
| 26 | Cross-arr companion adapter | Reuse evidence/triage contracts for Radarr/Sonarr when plugin hooks are unavailable. | 3 | 5 | 3 | D | API-driven mode must prove source-preserving and non-destructive semantics. |

## Recommended Next Pulls

The best near-term sequence after A2 is:

1. Finding revalidation loop.
2. Redacted evidence bundle export.
3. Triage dashboard/work queues.
4. Read-only probe collector.
5. Fixture truth-table lab.
6. Storage/root health audit.

These are high-ROI because they improve trust, debuggability, and evidence quality without introducing media writes. They also reduce A3/A4 risk by proving freshness, probe behavior, fixtures, and operator workflow before repair or reacquire exists.

## Deferred Or Rejected Defaults

These ideas should not be defaults:

- Auto-delete duplicates: too destructive; keep duplicate analysis review-only unless a future deletion spec proves recycle, rollback, and operator confirmation.
- AI auto-repair: AI can explain ambiguity, but repair eligibility and execution authorization must remain deterministic.
- Always-on full decode of the whole library: useful as an explicit slow sweep, but too expensive as a default background task.
- Direct Lidarr command queue repair/reacquire from A2: violates the read-only treatment-plan contract.
- Raw support bundle export: too much privacy risk; exports must stay redacted by default.

## Roadmap Integration

A2.5 should be introduced as a hardening band between A2 and A3. It can ship multiple small read-only tools independently:

- revalidation;
- evidence export;
- triage queues;
- probe collection;
- fixture truth-table work;
- storage/root health audit;
- policy simulation.

A3 should start only after A2.5 has enough fixture and live-read-only evidence to make repair dry-runs meaningful. A4 should remain separate from A3 because reacquire is a Lidarr workflow with album-wide and recycle-bin risks, not a file repair workflow.
