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
- Safety infrastructure: provenance, host capability gates, TTL, protected scopes, and synthetic mutation drills that make later write paths fail closed.
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

## Supplemental Rated Ideas

These additions came from the A2 roadmap review. They should mostly enrich A2.5 and A3 safety infrastructure before Brainarr graduates to repair or reacquire execution.

| Feature | Parent backlog item | Purpose | Usefulness | Complexity | ROI | Likely milestone | Safety caveat |
| --- | --- | --- | ---: | ---: | ---: | --- | --- |
| Evidence provenance fingerprint | Finding revalidation loop | Record scanner version, schema version, Lidarr assembly hash, TagLib identity, ffprobe/ffmpeg version, timeout policy, and classifier version on each finding/treatment plan. | 5 | 2 | 5 | A2.5 | Provenance never authorizes action; it only proves what evidence produced a recommendation. |
| Host capability registry | Storage/root health audit | Persist versioned proof of Lidarr plugin/API capabilities by assembly hash/version. | 5 | 3 | 5 | A2.5/A3 | Any write-capable workflow must refuse execution when host capability is unproven. |
| Evidence TTL policy | Finding revalidation loop | Expire treatment-plan eligibility by workflow and evidence age, while keeping old reports visible as history. | 4 | 2 | 5 | A2.5 | Expired evidence can be displayed but cannot feed dry-run or execution gates. |
| Protected scope list | Batch policy simulator | Let operators mark roots, artists, albums, files, or fingerprints as never-mutate. | 5 | 2 | 5 | A2.5 | Future executors must fail closed when protected scope overlaps the candidate. |
| Finding lifecycle history | Triage dashboard/work queues | Track when findings appear, clear, recur, or change classification. | 5 | 3 | 5 | A2.5 | History is operator context only; recurrent findings still require fresh evidence. |
| Waiver/suppression registry | Triage dashboard/work queues | Store fingerprint-bound accepted-risk records with expiry and reason. | 5 | 2 | 5 | A2.5 | Suppression hides noise from queues but never becomes approval for mutation. |
| Decision calibration log | Triage dashboard/work queues | Let users mark findings as true positive, false positive, repaired elsewhere, ignored, or manually reacquired. | 4 | 2 | 4 | A2.5 | Use sanitized aggregate learning only; no automatic threshold changes without review. |
| Dependency/toolchain doctor | Storage/root health audit | Check ffmpeg/ffprobe availability, TagLib behavior, Lidarr assembly identity, filesystem features, and recycle-bin readiness. | 4 | 2 | 5 | A2.5 | Audit-only until specific capability gates are tested. |
| Export leak scanner | Redacted evidence bundle export | Fail redacted evidence bundle export if raw paths, prompt text, exception text, or likely private library strings remain. | 4 | 2 | 4 | A2.5 | Prefer false-positive export blocking over leaking private data. |
| Local fixture health check command | Fixture truth-table lab | Validate local TagLib/ffprobe/ffmpeg behavior against known fixtures before enabling repair dry-run. | 4 | 3 | 4 | A2.5/A3 | Local fixture failure blocks repair features and asks for environment remediation. |
| Mutation safety drill on synthetic files | Repair dry-run sandbox | Exercise journal, backup, rename, rollback, and recovery on plugin-owned dummy files outside the library. | 5 | 3 | 5 | A3 | Must never touch media paths; proves filesystem semantics before real dry-run/repair. |
| Backup/rollback readiness audit | Operation ledger/recovery console | Verify free space, same-volume backup assumptions, restore rehearsal requirements, and crash-recovery prerequisites. | 5 | 4 | 4 | A3 | Audit/dry-run only until the journal contract is implemented and tested. |
| Reacquire blast-radius resolver | Reacquire dry-run planner | Preview every album, track, file, and recycle-bin consequence Lidarr may touch for a reacquire workflow. | 5 | 4 | 4 | A4 | Exact blast radius and user approval are required but not sufficient; A4 execution still needs decode-proven unrecoverability, host capability gates, recycle-bin disclosure, policy gates, and separate authorization. |
| Cohort risk analyzer | Batch policy simulator | Group findings by extension, codec, container, root, encoder hints, import age, and source to find systemic issues. | 4 | 3 | 4 | A2.5/C1 | Cohorts guide investigation and canary planning, not batch execution. |
| Canary batch planner | Batch policy simulator | Select tiny representative batches with stop conditions, required evidence, rollback gates, and confirmation criteria. | 5 | 4 | 5 | A3/A4 | Planner never authorizes mutation; any canary execution needs a separate executor contract, fresh evidence, explicit approval, rollback proof, and per-item gates. |
| Approval provenance ledger | Operation ledger/recovery console | Record operator, timestamp, evidence hash, policy gates, and intended workflow for every future explicit opt-in. | 5 | 3 | 5 | A3/A4 | Approval expires with evidence TTL and cannot be reused across fingerprints. |
| Manual review packet builder | Triage dashboard/work queues | Build per-album or per-cohort packets with sanitized evidence, candidate workflow, missing gates, and decision options. | 4 | 3 | 4 | A2.5/C1 | Packets are advisory and must not contain executable commands. |
| Policy profile simulator presets | Batch policy simulator | Compare diagnostic-only, dry-run-allowed, repair-allowed, and reacquire-allowed policies against findings. | 4 | 3 | 4 | A2.5/A3 | Simulator explains pass/fail only and cannot queue Lidarr or filesystem actions. |
| AppData/state janitor | Operation ledger/recovery console | Manage old reports, stale findings, expired waivers, evidence bundles, and confirmed backup metadata. | 4 | 2 | 5 | A2.5/A3 | Deletes only Brainarr-owned state; never media files or Lidarr records. |
| Root migration impact analyzer | Storage/root health audit | Simulate whether findings, fingerprints, backups, and suppressions survive storage/root moves. | 4 | 3 | 4 | C1 | No filesystem moves or path rewrites. |
| Import-to-healer correlation | Orphan and DB/file mismatch audit | Correlate findings with recent imports, upgrades, renames, rescans, and failed grabs. | 4 | 3 | 4 | C1 | Correlation identifies root causes but does not remediate automatically. |
| Source/release reputation tracker | Quality/profile drift auditor | Aggregate sanitized defect rates by indexer/source/release group/import path when evidence exists. | 4 | 3 | 4 | C1 | Must avoid naming/shaming from weak evidence; use only for operator filtering and future policy simulation. |
| Queue and failed-import doctor | Orphan and DB/file mismatch audit | Read Lidarr queue/history/import failures and explain recurring stuck states or failed manual imports. | 4 | 3 | 4 | C1 | Read-only diagnostics; no automatic queue removal or retry. |
| AI review-note summarizer | Import Brain identity assistant | Summarize complex evidence packets into short human-review notes with confidence, caveats, and missing gates. | 4 | 3 | 4 | B/C1 | Inputs must be minimized/redacted; AI text is explanatory only, not evidence, and cannot set confidence gates or execution authorization. |
| AI adversarial decision checker | Import Brain identity assistant | Ask a second model to challenge ambiguous import/tag/release recommendations before human review. | 4 | 4 | 3 | B/C | Inputs must be minimized/redacted or local-provider only; checker can lower confidence or add caveats, never approve mutation. |
| Local acoustic fingerprint audit | Duplicate/near-duplicate analyzer | Use local fingerprints to detect likely duplicates, wrong tags, or identity drift without cloud lookup. | 4 | 4 | 3 | C1 | Review-only; no deletes, tag writes, or release migration from fingerprint alone. |
| Acoustic fingerprint cloud lookup | Import Brain identity assistant | Optional privacy-gated lookup for weak identity cases where local evidence is insufficient. | 3 | 5 | 2 | C/D | Deferred by default; network lookup must be explicit, cached, redacted, and review-only. |
| Cross-arr capability matrix | Cross-arr companion adapter | Define which evidence, dry-run, and execution concepts are available in Lidarr plugin mode versus Radarr/Sonarr companion mode. | 4 | 4 | 4 | D | Shared contracts must fail closed when an app lacks safe hooks. |
| Companion conformance harness | Cross-arr companion adapter | Mock Arr APIs/filesystems to verify adapter behavior before live Radarr/Sonarr integration. | 4 | 4 | 4 | D | Companion mode must prove source-preserving and non-destructive semantics before use. |

## Recommended Next Pulls

The best near-term sequence after A2 is:

1. Finding revalidation loop.
2. Evidence provenance fingerprint.
3. Evidence TTL policy.
4. Protected scope list.
5. Redacted evidence bundle export plus export leak scanner.
6. Triage dashboard/work queues.
7. Read-only probe collector.
8. Dependency/toolchain doctor.
9. Fixture truth-table lab and local fixture health check command.
10. Storage/root health audit.
11. Finding lifecycle history and waiver/suppression registry.
12. Host capability registry.
13. Policy profile simulator presets.
14. Mutation safety drill on synthetic files.

These are high-ROI because they improve trust, debuggability, and evidence quality without introducing media writes. They also reduce A3/A4 risk by proving freshness, probe behavior, fixtures, and operator workflow before repair or reacquire exists.

## Deferred Or Rejected Defaults

These ideas should not be defaults:

- Auto-delete duplicates: too destructive; keep duplicate analysis review-only unless a future deletion spec proves recycle, rollback, and operator confirmation.
- AI auto-repair: AI can explain ambiguity, but repair eligibility and execution authorization must remain deterministic.
- Always-on full decode of the whole library: useful as an explicit slow sweep, but too expensive as a default background task.
- Direct Lidarr command queue repair/reacquire from A2: violates the read-only treatment-plan contract.
- Raw support bundle export: too much privacy risk; exports must stay redacted by default.
- Auto-tag repair apply before A3 safety machinery: tag writes are media mutation and need backup, diff, rollback, freshness, canonical ID proof, and explicit approval.
- One-click fix all: too easy to confuse advisory candidates with authorization; batch execution needs item-level gates, canary rollout, and exact rollback proof.
- Cross-arr companion execution before Lidarr A1/A2/A2.5 stabilizes: portability matters, but Radarr/Sonarr API semantics must be proven separately.
- Acoustic fingerprint cloud lookup by default: privacy, cost, and false-match risk are too high; keep any future cloud lookup explicit and review-only.

## Roadmap Integration

A2.5 should be introduced as a hardening band between A2 and A3. It can ship multiple small read-only tools independently:

- revalidation;
- provenance and TTL;
- protected scopes and suppressions;
- evidence export;
- triage queues;
- probe collection;
- fixture truth-table work;
- toolchain/capability checks;
- storage/root health audit;
- policy simulation.

A3 should start only after A2.5 has enough fixture and live-read-only evidence to make repair dry-runs meaningful, and after synthetic mutation drills prove the journal/backup/rollback path outside the library. A4 should remain separate from A3 because reacquire is a Lidarr workflow with album-wide and recycle-bin risks, not a file repair workflow.
