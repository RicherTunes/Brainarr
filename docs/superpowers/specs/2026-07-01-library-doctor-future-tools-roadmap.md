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

## Rating Corrections From Adversarial Review

The roadmap is intentionally conservative after review:

- A2 is not done until `healer/getfindings` returns treatment plans and summary counts from sanitized projected findings. A2.5 cannot start before that conformance gate passes.
- A2.5 is a set of small hardening pulls, not one large milestone. Evidence freshness, provenance, state migration, redaction, and host conformance should land before dashboards or recurring scans.
- Redacted export is split into local diagnostic export and shareable support export. Shareable export is higher complexity because it needs leak scanning and per-export salted identifiers.
- Storage/root health is split into basic audit and mutation-grade root containment proof. The latter gates A3/A4.
- Recurring scans, reacquire, tag writes, repair apply, and AI-heavy flows are down-ranked until the deterministic evidence and authorization contracts are mature.
- Whole-library decode is not a default. Targeted decode-on-finding verification has much better ROI.

## Priority Bands

- A2 completion gate: projection conformance, sanitized treatment plans, and exhaustive summary counts.
- A2.5 foundation: schema migration, provenance, TTL, read-only kill switch, fingerprint policy, redaction verification, and host/API conformance.
- A2.5 operator tools: revalidation, triage filters, local diagnostic export, read-only probes, fixture truth tables, scan-cost metrics, and basic storage/root audits.
- A3 repair foundation: synthetic mutation drill, repair strategy registry, ledger state machine, backup catalog, and repair dry-run sandbox.
- A4 reacquire foundation: targeted decode proof, blast-radius dry-run, recycle-bin proof, and approval provenance.
- C-series data management: release topology, track-slot coverage, collision analysis, tags, artwork, sidecars, queue/history, and duplicates.
- AI-assisted judgment: later features where the model explains ambiguous cases but deterministic gates still decide allowed actions.
- D-series portability: cross-arr contracts only after the Lidarr plugin path proves the contracts.

## Ranked Backlog

| Rank | Feature | Purpose | Usefulness | Complexity | ROI | Likely milestone | Safety caveat |
| ---: | --- | --- | ---: | ---: | ---: | --- | --- |
| 1 | A2 projection conformance gate | Prove `healer/getfindings` returns sanitized `treatmentPlan` objects and exhaustive summary counts. | 5 | 1 | 5 | A2 | Blocks A2.5; no dashboard/export/scheduler work until this is stable. |
| 2 | Finding revalidation loop | Re-stat and re-read current findings so stale false alarms auto-resolve. | 5 | 2 | 5 | A2.5 | Read-only; freshness changes never authorize action. |
| 3 | State schema migration/corruption harness | Dry-run upgrades of old finding, treatment-plan, waiver, and ledger schemas. | 5 | 2 | 5 | A2.5 | Corrupt or unknown state fails closed to review; no mutation. |
| 4 | Evidence provenance fingerprint | Record scanner version, schema version, Lidarr assembly hash, TagLib identity, tool versions, timeout policy, and classifier version. | 5 | 2 | 5 | A2.5 | Provenance proves evidence origin only; it never authorizes action. |
| 5 | Evidence TTL policy | Expire treatment-plan eligibility by workflow and evidence age while preserving old reports as history. | 4 | 2 | 5 | A2.5 | Expired evidence can be displayed but cannot feed dry-run or execution gates. |
| 6 | Global read-only kill switch and approval authority model | Define install-wide no-mutation mode plus future approval authority, expiry, replay protection, and operator identity. | 5 | 2 | 5 | A2.5 | The kill switch overrides every future executor. |
| 7 | Fingerprint acquisition and retention policy | Define full vs partial hash, when hashing is allowed, privacy retention, CPU budget, and invalidation. | 5 | 3 | 5 | A2.5/A3 | Future approvals must bind to fresh bytes, not only paths or track ids. |
| 8 | Lidarr state snapshot/diff inspector | Compare current TrackFile, album, quality, and root state to prior scans. | 5 | 3 | 5 | A2.5 | DB drift and root moves make old treatment plans stale. |
| 9 | Basic storage/root health audit | Detect offline roots, permissions, low space, path casing issues, and inaccessible parents. | 5 | 2 | 5 | A2.5 | Audit only; no path rewrites, mounts, moves, or rescans. |
| 10 | Mutation-grade root containment proof | Prove symlink, reparse point, hardlink, UNC/network root, case folding, same-volume, and mount identity semantics. | 5 | 4 | 5 | A3/A4 gate | Required before any media write, backup, repair, or reacquire execution. |
| 11 | Lidarr host API conformance harness | Verify plugin/API semantics, forbidden move/delete/replace paths, assembly hashes, and host capability drift. | 5 | 3 | 5 | A2.5/D | Write-capable workflows refuse execution when host capability is unproven. |
| 12 | Redaction policy verifier for all stores | Scan findings, treatment plans, exports, logs, metrics, ledgers, and AI review notes for private data leaks. | 5 | 3 | 5 | A2.5 | Prefer false-positive blocking over leaking paths, tag values, prompts, or exception text. |
| 13 | Local diagnostic evidence export | Export operator-local diagnostic bundles for debugging on the same host. | 4 | 2 | 4 | A2.5 | Local export still redacts raw paths by default; it is not a shareable support bundle. |
| 14 | Shareable support export plus leak scanner | Produce shareable sanitized bundles with leak scanning and per-export salted identifiers. | 5 | 4 | 4 | A2.5 | No global stable hashes, raw paths, tag values, MBIDs, provider prompts, or exception text. |
| 15 | Basic triage filters/API | Filter findings by workflow, risk, freshness, blocked reason, and authorization state. | 4 | 2 | 4 | A2.5 | Review state stays separate from execution authorization. |
| 16 | Full triage dashboard/work queues | Add operator queues, review state, suppression, lifecycle, protected scopes, and packet generation. | 4 | 4 | 3 | A2.5/C1 | Waits for lifecycle, suppression, and protected-scope foundations. |
| 17 | Read-only probe collector | Add `ffprobe` stream/container evidence for better candidate planning. | 5 | 4 | 4 | A2.5 | Structured arguments, timeouts, stdout/stderr caps, no network inputs. |
| 18 | Targeted decode-on-finding verifier | Run decode only where the result changes workflow eligibility or risk. | 5 | 3 | 5 | A2.5/A5 | Explicit, bounded, resumable, and never a default whole-library sweep. |
| 19 | Fixture truth-table lab | Lock expected TagLib/probe/decode/classifier outcomes for real fixtures. | 5 | 3 | 5 | A2.5/A3 | Fixtures must prove intact audio never becomes destructive eligibility. |
| 20 | Local fixture health check command | Validate local TagLib/ffprobe/ffmpeg behavior against known fixtures before enabling dry-runs. | 4 | 3 | 4 | A2.5/A3 | Local fixture failure blocks repair features and asks for remediation. |
| 21 | Metrics/cardinality and scan-cost budget | Cap metrics cardinality and track scan duration, timeouts, queue size, findings churn, and export size. | 4 | 2 | 4 | A2.5 | Metrics never include raw path, tag value, source prompt, or unbounded error labels. |
| 22 | Queue and failed-import doctor | Read Lidarr queue/history/import failures and explain recurring stuck states or failed manual imports. | 4 | 3 | 4 | C1 | Read-only diagnostics; no automatic queue removal or retry. |
| 23 | Track-slot coverage matrix | Show missing, extra, duplicate, wrong-disc, wrong-medium, alternate-edition, and unclaimed files per album/release. | 5 | 3 | 5 | C1 | Review-only; no import, delete, rename, or release migration. |
| 24 | Track collision detector | Find multiple files mapped to the same canonical track slot or one file ambiguously serving multiple slots. | 5 | 3 | 5 | C1 | Review-only; never auto-delete or replace. |
| 25 | Music release topology mapper | Model MusicBrainz release group, release, medium, disc, track, and recording relationships explicitly. | 5 | 4 | 4 | C1/B | Required before safe tag repair, release migration, or Import Brain decisions. |
| 26 | Cohort risk analyzer | Group findings by extension, codec, container, root, encoder hints, import age, and source. | 4 | 3 | 4 | A2.5/C1 | Cohorts guide investigation and canary planning, not batch execution. |
| 27 | Batch policy simulator | Show what a future batch would require: backups, free space, gates, and risk. | 3 | 3 | 3 | A2.5/A3 | Useful only after TTL, provenance, protected scopes, and capability registry exist. |
| 28 | Plugin-owned scan scheduler | Run opt-in recurring scans with pause, backoff, cost budgets, and resumable cursors. | 3 | 4 | 2 | A2.5 | Disabled by default; waits for revalidation, TTL, and reader-busy handling. |
| 29 | Repair strategy registry | Classify dry-run strategies by container, codec, symptom, and fixture evidence instead of generic remuxing. | 4 | 4 | 4 | A3 | Strategies are candidates only; no writes without the A3 authorization contract. |
| 30 | Mutation safety drill on synthetic files | Exercise journal, backup, rename, rollback, and recovery on plugin-owned dummy files outside the library. | 5 | 3 | 5 | A3 | Must never touch media paths; proves filesystem semantics before real dry-run/repair. |
| 31 | Operation ledger state machine | Implement planned/applying/applied/failed/recovery states before any write-capable operation exists. | 5 | 5 | 5 | A3 | Ledger comes before a console; every transition needs crash-injection tests. |
| 32 | Backup catalog and orphan-backup reconciler | Track backup lifecycle, stale backups, missing originals, and restore readiness. | 5 | 3 | 5 | A3 | Does not touch active media until the repair authorization contract exists. |
| 33 | Operation ledger/recovery console | Inspect journal states, failures, reconciliation needs, and recovery instructions. | 5 | 5 | 5 | A3 | Console must not mutate without an explicit authorized transition. |
| 34 | Repair dry-run sandbox | Create verified temp outputs without touching originals. | 5 | 5 | 4 | A3 | Requires synthetic mutation drill and fixture gate first; temp output only. |
| 35 | Verified repair-in-place | Backup, repair/remux, swap, re-read, and rollback under a journal. | 5 | 5 | 3 | A3.5 | Defer until dry-run, fixture, ledger, rollback, root, and host gates are proven. |
| 36 | Whole-library full-decode integrity sweep | Detect valid-header but truncated or undecodable files across the whole library. | 3 | 4 | 2 | A5 | Explicit slow mode only; targeted decode ships first. |
| 37 | Reacquire dry-run planner | Preview Lidarr search/reacquire options and scope for unrecoverable files. | 4 | 5 | 3 | A4 | No queue push; dry-run result is evidence only. |
| 38 | Reacquire blast-radius resolver | Preview every album, track, file, recycle-bin, and queue consequence Lidarr may touch. | 5 | 4 | 4 | A4 | Exact blast radius is required but not sufficient for execution. |
| 39 | Reacquire execution | Opt-in reacquire for decode-proven unrecoverable files. | 4 | 5 | 2 | A4.5 | Requires blast radius, recycle bin, host proof, approval provenance, and fresh decode evidence. |
| 40 | Tag canonicalization dry-run | Compare weak tags to Lidarr/MusicBrainz canonical data. | 4 | 3 | 4 | C1 | AI only for ambiguous release judgment, never authorization. |
| 41 | Tag repair apply | Write approved metadata fixes with before/after diff and tag backup. | 3 | 5 | 2 | C2 | Tag writes are media mutation and require backup, rollback, canonical ID proof, and explicit approval. |
| 42 | MusicBrainz release migration assistant | Identify albums attached to the wrong release and propose safer release remaps. | 4 | 4 | 4 | C1/B | Review-only until Lidarr API semantics and rollback are proven. |
| 43 | Release/album consistency audit | Detect track count, duration, edition, and release identity drift. | 4 | 4 | 4 | C1/B | AI may summarize ambiguity; deterministic gates decide state. |
| 44 | Duplicate/near-duplicate analyzer | Find duplicate tracks by fingerprint, duration, and identity hints. | 4 | 3 | 4 | C1 | Review-only; never auto-delete or replace. |
| 45 | Orphan and DB/file mismatch audit | Find unmanaged files in artist folders and missing DB records for present files. | 4 | 3 | 4 | C1 | No import/delete; produce review bundles only. |
| 46 | Import-to-healer correlation | Correlate findings with recent imports, upgrades, renames, rescans, and failed grabs. | 4 | 3 | 4 | C1 | Correlation identifies root causes but does not remediate automatically. |
| 47 | Rename and retag simulator | Preview tag/path normalization and Lidarr naming impacts before any write. | 4 | 3 | 4 | C1/C2 | Simulation only; no filesystem writes or Lidarr rename commands. |
| 48 | Quality/profile drift auditor | Detect files below wanted quality/profile, unexpected codecs, or lossy/lossless drift. | 3 | 3 | 3 | C1 | Report only; upgrades go through normal Lidarr policy gates. |
| 49 | Sidecar and companion-file audit | Review `.cue`, `.log`, `.m3u`, lyrics, `.nfo`, and sidecar artwork policy. | 3 | 3 | 3 | C1 | Review-only; no cleanup of companion files by default. |
| 50 | Cover art and embedded artwork audit | Detect missing, huge, corrupt, or inconsistent embedded artwork. | 3 | 3 | 3 | C1/C2 | Artwork writes require tag backup and explicit opt-in. |
| 51 | ReplayGain/loudness metadata audit | Detect missing or inconsistent loudness tags. | 3 | 3 | 3 | C1/C2 | Later writes use the same tag backup and rollback gates as tag repair. |
| 52 | AI review-note summarizer | Summarize complex evidence packets into short human-review notes. | 4 | 3 | 4 | B/C1 | Inputs must be minimized/redacted; AI text is explanation, not evidence. |
| 53 | Import Brain identity assistant | Adjudicate ambiguous manual import/release identity cases with evidence and confidence. | 3 | 5 | 2 | B | AI proposes only; deterministic gate verifies IDs and empty slots. |
| 54 | Tagging Brain | Suggest canonical tag fixes and release choices for weak metadata. | 3 | 5 | 2 | C | AI cannot write tags or set execution authorization. |
| 55 | AI adversarial decision checker | Ask a second model to challenge ambiguous import/tag/release recommendations. | 2 | 4 | 1 | B/C | Excluded by default; can lower confidence only, never approve mutation. |
| 56 | Cross-arr capability matrix | Define which evidence, dry-run, and execution concepts exist in Lidarr plugin mode versus Radarr/Sonarr companion mode. | 4 | 4 | 4 | D | Shared contracts must fail closed when an app lacks safe hooks. |
| 57 | Cross-arr companion adapter | Reuse evidence/triage contracts for Radarr/Sonarr when plugin hooks are unavailable. | 2 | 5 | 1 | D | Park until Lidarr proves the contracts. |
| 58 | Companion conformance harness | Mock Arr APIs/filesystems to verify adapter behavior before live Radarr/Sonarr integration. | 4 | 4 | 4 | D | Companion mode must prove source-preserving and non-destructive semantics before use. |
| 59 | Local acoustic fingerprint audit | Use local fingerprints to detect likely duplicates, wrong tags, or identity drift without cloud lookup. | 4 | 4 | 3 | C1 | Review-only; no deletes, tag writes, or release migration from fingerprint alone. |
| 60 | Acoustic fingerprint cloud lookup | Optional privacy-gated lookup for weak identity cases where local evidence is insufficient. | 2 | 5 | 1 | C/D | Excluded from defaults; explicit, cached, redacted, and review-only if ever added. |

## Supplemental Rated Ideas

These items either enrich the ranked backlog or describe subfeatures that should be planned with their parent item.

| Feature | Parent backlog item | Purpose | Usefulness | Complexity | ROI | Likely milestone | Safety caveat |
| --- | --- | --- | ---: | ---: | ---: | --- | --- |
| Protected scope list | Global read-only kill switch and approval authority model | Let operators mark roots, artists, albums, files, or fingerprints as never-mutate. | 5 | 2 | 5 | A2.5 | Future executors must fail closed when protected scope overlaps the candidate. |
| Finding lifecycle history | Full triage dashboard/work queues | Track when findings appear, clear, recur, or change classification. | 5 | 3 | 5 | A2.5 | History is operator context only; recurrent findings still require fresh evidence. |
| Waiver/suppression registry | Full triage dashboard/work queues | Store fingerprint-bound accepted-risk records with expiry and reason. | 5 | 2 | 5 | A2.5 | Suppression hides noise from queues but never becomes approval for mutation. |
| Decision calibration log | Full triage dashboard/work queues | Let users mark findings as true positive, false positive, repaired elsewhere, ignored, or manually reacquired. | 4 | 2 | 4 | A2.5 | Use sanitized aggregate learning only; no automatic threshold changes without review. |
| Dependency/toolchain doctor | Lidarr host API conformance harness | Check ffmpeg/ffprobe availability, TagLib behavior, Lidarr assembly identity, filesystem features, and recycle-bin readiness. | 4 | 2 | 5 | A2.5 | Audit-only until specific capability gates are tested. |
| Export leak scanner | Shareable support export plus leak scanner | Fail export if raw paths, prompt text, exception text, or likely private library strings remain. | 4 | 2 | 4 | A2.5 | Prefer false-positive export blocking over leaking private data. |
| Approval provenance ledger | Global read-only kill switch and approval authority model | Record operator, timestamp, evidence hash, policy gates, and intended workflow for every future explicit opt-in. | 5 | 3 | 5 | A3/A4 | Approval expires with evidence TTL and cannot be reused across fingerprints. |
| Manual review packet builder | Full triage dashboard/work queues | Build per-album or per-cohort packets with sanitized evidence, candidate workflow, missing gates, and decision options. | 4 | 3 | 4 | A2.5/C1 | Packets are advisory and must not contain executable commands. |
| Policy profile simulator presets | Batch policy simulator | Compare diagnostic-only, dry-run-allowed, repair-allowed, and reacquire-allowed policies against findings. | 4 | 3 | 4 | A2.5/A3 | Simulator explains pass/fail only and cannot queue Lidarr or filesystem actions. |
| AppData/state janitor | State schema migration/corruption harness | Manage old reports, stale findings, expired waivers, evidence bundles, and confirmed backup metadata. | 3 | 4 | 2 | A2.5/A3 | Deletes only Brainarr-owned state after retention and dry-run delete previews exist. |
| Root migration impact analyzer | Lidarr state snapshot/diff inspector | Simulate whether findings, fingerprints, backups, and suppressions survive storage/root moves. | 4 | 3 | 4 | C1 | No filesystem moves or path rewrites. |
| Source/release reputation tracker | Cohort risk analyzer | Aggregate sanitized defect rates by indexer/source/release group/import path when evidence exists. | 4 | 3 | 4 | C1 | Use only for operator filtering and future policy simulation. |
| Canary batch planner | Batch policy simulator | Select tiny representative batches with stop conditions, required evidence, rollback gates, and confirmation criteria. | 5 | 4 | 5 | A3/A4 | Planner never authorizes mutation; any canary execution needs a separate executor contract, fresh evidence, explicit approval, rollback proof, and per-item gates. |
| Root/path mapping conformance tool | Mutation-grade root containment proof | Prove Docker, UNC, symlink, reparse, case, and companion-host path semantics. | 5 | 4 | 4 | A2.5/D | No path update or move; it only blocks unsafe assumptions. |
| Evidence schema migration simulator | State schema migration/corruption harness | Load old schemas and report exactly how they would migrate or fail closed. | 4 | 3 | 4 | A2.5 | Dry-run first; migration writes require their own tested state transition. |
| Healer metrics/SLO dashboard | Metrics/cardinality and scan-cost budget | Show scan duration, timeouts, finding churn, queue size, and cardinality-safe cohorts. | 4 | 2 | 5 | A2.5 | UI displays aggregate operational health only. |

## Recommended Next Pulls

The best near-term sequence is intentionally smaller than the full A2.5 band:

1. Finish A2 projection conformance: treatment plans and summary counts in `healer/getfindings`.
2. Finding revalidation loop.
3. State schema migration/corruption harness.
4. Evidence provenance fingerprint.
5. Evidence TTL policy.
6. Global read-only kill switch and approval authority model.
7. Fingerprint acquisition and retention policy.
8. Lidarr state snapshot/diff inspector.
9. Basic storage/root health audit.
10. Lidarr host API conformance harness.
11. Redaction policy verifier for all stores.
12. Local diagnostic evidence export.
13. Basic triage filters/API.
14. Read-only probe collector.
15. Targeted decode-on-finding verifier.
16. Fixture truth-table lab and local fixture health check command.
17. Metrics/cardinality and scan-cost budget.
18. Mutation-grade root containment proof.
19. Mutation safety drill on synthetic files.
20. Operation ledger state machine.

This sequence improves trust, debuggability, and evidence quality without introducing media writes. It also reduces A3/A4 risk by proving freshness, state migration, probe behavior, fixture behavior, redaction, root containment, and host semantics before repair or reacquire exists.

## Deferred Or Rejected Defaults

These ideas should not be defaults:

- Auto-delete duplicates: too destructive; keep duplicate analysis review-only unless a future deletion spec proves recycle, rollback, and operator confirmation.
- AI auto-repair: AI can explain ambiguity, but repair eligibility and execution authorization must remain deterministic.
- AI adversarial decision checker by default: cost and privacy surface exceed value if deterministic gates are sound.
- Always-on full decode of the whole library: useful as an explicit slow sweep, but too expensive as a default background task.
- Default recurring scans: recurring scans can amplify TagLib busy states, disk churn, stale evidence, and privacy logs; keep scheduler opt-in and late.
- Direct Lidarr command queue repair/reacquire from A2: violates the read-only treatment-plan contract.
- Raw support bundle export: too much privacy risk; exports must stay redacted and leak-scanned by default.
- Auto-tag repair apply before A3 safety machinery: tag writes are media mutation and need backup, diff, rollback, freshness, canonical ID proof, and explicit approval.
- One-click fix all: too easy to confuse advisory candidates with authorization; batch execution needs item-level gates, canary rollout, and exact rollback proof.
- Reacquire execution before blast-radius proof: album-wide replacement behavior, recycle-bin state, and host semantics must be proven first.
- Cross-arr companion execution before Lidarr A1/A2/A2.5 stabilizes: portability matters, but Radarr/Sonarr API semantics must be proven separately.
- Acoustic fingerprint cloud lookup by default: privacy, cost, and false-match risk are too high; keep any future cloud lookup explicit and review-only.

## Roadmap Integration

A2 should finish the projection contract before the roadmap pulls any A2.5 work. A2.5 should then ship as many small read-only tools, not as one broad release:

- revalidation;
- schema migration and corruption handling;
- provenance and TTL;
- read-only kill switch, protected scopes, and approval authority model;
- fingerprint policy;
- Lidarr state snapshots and diffing;
- storage/root health audit;
- host/API conformance;
- redaction verification;
- local diagnostic export;
- triage filters;
- probe collection;
- targeted decode verification;
- fixture truth-table work;
- metrics/cardinality and scan-cost budgets.

A3 should start only after A2.5 has enough fixture and live-read-only evidence to make repair dry-runs meaningful, after mutation-grade root containment is proven, and after synthetic mutation drills prove the journal/backup/rollback path outside the library. A4 should remain separate from A3 because reacquire is a Lidarr workflow with album-wide and recycle-bin risks, not a file repair workflow.

The C-series data-management tools should be developed mostly read-only first. Track-slot coverage, release topology, queue/history diagnostics, duplicates, sidecars, artwork, ReplayGain, and tag canonicalization can deliver operator value before any write path exists.

AI-assisted tools should remain late and advisory. They may summarize evidence or challenge ambiguous identity/release decisions, but model output is never evidence, never execution authorization, and never a source of filesystem paths or Lidarr commands.
