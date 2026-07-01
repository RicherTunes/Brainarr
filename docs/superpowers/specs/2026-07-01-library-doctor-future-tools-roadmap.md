# Library Doctor Future Healing and Track Management Roadmap

Status: Draft backlog
Date: 2026-07-01
Owner: RicherTunes / Brainarr

Current A2.5 read-only slices landed after A2 projection conformance: triage filters for `workflow`, `risk`, `blockedReason`, and `authorized`; golden snapshots for the sanitized `healer/getfindings` projection and treatment vocabulary; and a static `healer/getfieldcatalog` field-sensitivity catalog for export and AI-prompt policy gating.

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
| 15 | Basic triage filters/API | Filter findings by workflow, risk, blocked reason, and authorization state; freshness and review-lifecycle filters remain future expansion. | 4 | 2 | 4 | A2.5 | Review state stays separate from execution authorization. |
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
| Tool availability quick check | Lidarr host API conformance harness | Check whether required tools and host services are discoverable before deeper conformance tests run. | 4 | 2 | 5 | A2.5 | Availability is not capability proof; filesystem, recycle-bin, and mutation semantics stay in A3/A4 gates. |
| Export leak-scanner fixture corpus | Shareable support export plus leak scanner | Maintain fixtures for raw paths, prompt fragments, exception text, MusicBrainz identifiers, and private library strings. | 4 | 3 | 4 | A2.5 | Fixtures strengthen deterministic leak scanning; they do not certify an export by themselves. |
| Approval receipt model | Global read-only kill switch and approval authority model | Record operator, timestamp, evidence hash, policy gates, intended workflow, expiry, and replay protections for future explicit opt-ins. | 5 | 4 | 5 | A3/A4 | Approval expires with evidence TTL and cannot be reused across fingerprints or workflows. |
| Manual review packet builder | Full triage dashboard/work queues | Build per-album or per-cohort packets with sanitized evidence, candidate workflow, missing gates, and decision options. | 4 | 3 | 4 | A2.5/C1 | Packets are advisory and must not contain executable commands. |
| Policy profile simulator presets | Batch policy simulator | Compare diagnostic-only, dry-run-allowed, repair-allowed, and reacquire-allowed policies against findings. | 4 | 3 | 4 | A2.5/A3 | Simulator explains pass/fail only and cannot queue Lidarr or filesystem actions. |
| State retention/quarantine auditor | State schema migration/corruption harness | Report stale reports, expired waivers, orphaned evidence bundles, and backup metadata that may need retention action. | 3 | 3 | 3 | A2.5/A3 | A2.5 is report/quarantine-preview only; deletion requires retention policy, journaled state transition, and restore proof. |
| Root migration impact analyzer | Lidarr state snapshot/diff inspector | Simulate whether findings, fingerprints, backups, and suppressions survive storage/root moves. | 4 | 3 | 4 | C1 | No filesystem moves or path rewrites. |
| Source/release defect trend report | Cohort risk analyzer | Aggregate local opt-in defect trends by source, release group, import age, and queue/history context when sanitized evidence exists. | 3 | 4 | 3 | C1 | Local-only and advisory; no automatic quality changes, source penalties, blocklists, deletions, or upgrades. |
| Canary batch planner | Batch policy simulator | Select tiny representative batches with stop conditions, required evidence, rollback gates, and confirmation criteria. | 5 | 4 | 4 | A3/A4 | Planner never authorizes mutation; delete/reacquire canaries require blast-radius proof, rollback contracts, fresh evidence, explicit approval, and per-item gates. |
| Root/path mapping conformance tool | Mutation-grade root containment proof | Prove Docker, UNC, symlink, reparse, case, and companion-host path semantics. | 5 | 4 | 4 | A2.5/D | No path update or move; it only blocks unsafe assumptions. |
| Legacy-state fixture corpus | State schema migration/corruption harness | Keep old, malformed, and partial state files as fixtures proving migrations and fail-closed behavior. | 4 | 2 | 4 | A2.5 | Fixtures cover dry-run migration expectations; migration writes require their own tested state transition. |
| Healer scan-cost report | Metrics/cardinality and scan-cost budget | Show scan duration, timeouts, finding churn, queue size, and cardinality-safe cohorts. | 4 | 2 | 4 | A2.5 | Report displays aggregate operational health only and must not hide high-risk findings. |

## New Rated Additions

These ideas came from the follow-up healer/QoL brainstorm. They should be folded into parent milestones only after the existing A2/A2.5 contracts remain stable; none of them grants permission to mutate media or Lidarr state.

| Feature | Purpose | Usefulness | Complexity | ROI | Likely milestone | Safety caveat |
| --- | --- | ---: | ---: | ---: | --- | --- |
| Container/codec mismatch signature detector | Flag files whose extension/container/codec combination is suspicious, such as FLAC audio inside MP4/M4A, before deciding whether a repair strategy is even plausible. | 5 | 2 | 5 | A2.5/A3 | Read-only signature evidence; mismatch alone never authorizes remux, rename, or reacquire. |
| FLAC STREAMINFO MD5 verifier | Verify lossless FLAC stream integrity cheaply where available and use it as a repair/dry-run gate. | 5 | 3 | 5 | A2.5/A3 | Integrity evidence must be bound to the current fingerprint and tool provenance. |
| Recycle-bin and backup readiness doctor | Audit Lidarr recycle-bin settings, backup target health, free space, same-volume constraints, and restore feasibility before A3/A4 execution exists. | 5 | 2 | 5 | A2.5/A3/A4 | Audit-only; a ready backup target is necessary but not sufficient for mutation. |
| Known-bad signature library | Build deterministic rules for recurring defect patterns by extension, container, codec, encoder hints, import source, and TagLib/probe/decode outcome. | 5 | 3 | 5 | A2.5/C1 | Signatures can raise suspicion and choose required evidence, not select destructive workflows by themselves. |
| Classifier replay/regression bench | Replay historical findings through new classifiers, treatment-plan rules, and policy changes before release. | 5 | 2 | 5 | A2.5/C | Read-only release gate; changed labels/plans require review before migration or rollout. |
| Root outage storm coalescer | Collapse thousands of missing-file findings into one root/NAS/offline-storage incident when evidence points to a shared outage. | 5 | 2 | 5 | A2.5 | Coalescing reduces noise only; it must not hide per-file findings or authorize repair. |
| Evidence contract golden pack | Freeze treatment-plan DTOs, vocabularies, summaries, redaction behavior, and fail-closed cases as compatibility fixtures. | 5 | 2 | 5 | A2.5 | Contract drift blocks release until migration or versioning is explicit. |
| Field sensitivity annotations | Mark each evidence, export, log, metric, AI-note, and UI field as public, local, private, or secret. | 5 | 3 | 5 | A2.5/B/C | Mechanical leak checks use annotations; missing annotations fail closed. |
| Deterministic track-slot identity index | Build a normalized index of album, release, medium, disc, track, recording IDs, durations, and current file occupancy. | 5 | 3 | 5 | C1/B | Powers coverage and Import Brain evidence but cannot claim, delete, or replace files by itself. |
| Adaptive large-library sampler | Use stratified read-only sampling by root, codec, import age, source, and prior findings before expensive sweeps. | 4 | 2 | 5 | A2.5/A5 | Sampling prioritizes investigation but cannot certify the full library healthy. |
| Evidence change explainer | Explain why a finding changed between scans: moved file, mtime/fingerprint change, label normalization, added probe evidence, or TTL expiry. | 4 | 2 | 4 | A2.5/C1 | Explanation is derived from stored facts and never becomes action approval. |
| Content identity graph across moves | Track path hash, TrackFile ID, size, mtime, and optional content fingerprint over time so stale approvals cannot follow the wrong file. | 5 | 3 | 4 | C1/A3/A4 | Fingerprints are privacy-bearing and need salt, retention, and stale-plan invalidation rules. |
| Root capability atlas | Map filesystem traits such as hardlinks, same-volume proof, mtime precision, case behavior, reparse points, and cloud placeholders. | 5 | 4 | 4 | A2.5/A3 | Read-only atlas data gates future writes but cannot authorize them. |
| Toolchain reproducibility runner | Run TagLib, ffprobe, ffmpeg, fixture, and host-assembly checks against pinned versions. | 4 | 3 | 4 | A2.5/A3 | Drift blocks higher-confidence evidence until fixtures are refreshed or the toolchain is pinned. |
| Synthetic fixture/corruption generator | Generate truncated, bad-header, missing-tag, odd-container, and hostile-filename fixtures for deterministic tests. | 4 | 3 | 4 | A2.5/A3 | Generated fixtures avoid private media and must be clearly separated from user library paths. |
| Album-level finding packet aggregator | Group per-track findings into album, root, or cohort packets so operators review causes instead of isolated symptoms. | 4 | 2 | 4 | A2.5/C1 | Packets preserve item-level evidence and cannot batch-authorize actions. |
| Human review calibration log | Capture sanitized outcomes such as true positive, false positive, externally repaired, ignored, or manually reacquired. | 4 | 3 | 4 | A2.5/B/C | Outcomes inform versioned threshold reviews only; no silent automatic threshold changes. |
| Track survivorship review planner | For duplicate or collision cases, rank keep-candidates by quality, completeness, tags, duration, provenance, and slot fit. | 4 | 3 | 4 | C1 | Review-only; no deletion, replacement, or move from survivorship ranking. |
| Dependency allow/block policy | Known-bad TagLib, ffmpeg, ffprobe, or Lidarr assembly versions fail closed; known-good versions unlock higher-confidence evidence. | 4 | 2 | 4 | A2.5/A3 | Allow/block status affects evidence confidence only, not execution authorization. |
| Dummy restore rehearsal | Prove backup restore and ledger reconciliation on plugin-owned dummy files before any real media repair path exists. | 5 | 3 | 4 | A3 | Dummy paths only; live media remains untouched until restore proof and authorization contracts exist. |
| Source-of-truth conflict detector | Compare Lidarr DB state, file tags, folder/file names, MusicBrainz topology, and optional probe evidence to identify which source disagrees. | 5 | 4 | 5 | C1/B | Conflicts become review packets; AI may summarize ambiguity but deterministic IDs/gates decide action eligibility. |
| Import rejection explainer | Translate Lidarr manual-import and failed-import reasons into actionable, evidence-linked next steps. | 5 | 3 | 5 | C1/B | Explanation only; no queue retry, import, delete, or replace without a separate workflow. |
| Track split/merge anomaly detector | Detect likely one-file-many-tracks, many-files-one-track, hidden-track, pregap, or incorrectly split album cases. | 4 | 4 | 3 | C1 | Review-only; topology mismatches are too ambiguous for automatic repair. |
| Sample-rate and bit-depth drift audit | Report files that drift from release/profile expectations, including unexpected downsampling or inconsistent album-level technical traits. | 4 | 3 | 4 | C1 | Report-only; upgrades and replacements remain Lidarr policy decisions. |
| Lossy transcode suspicion audit | Identify cases that look like lossy-to-lossless transcodes using codec history hints, spectrogram-capable optional tooling, or trusted provenance gaps. | 3 | 5 | 2 | C1/A5 | Expensive and probabilistic; never delete or reacquire from this signal alone. |
| Silence, clipping, and audio-content anomaly sampler | Run bounded optional audio-content checks for all-silence files, severe clipping, tiny decoded audio, or broken channel layouts. | 4 | 4 | 3 | A5 | Explicit slow mode only; content heuristics require human review and fresh decode evidence. |
| Tag round-trip compatibility verifier | Before any tag-write milestone, prove that TagLib, Lidarr, and common players can read proposed tag changes consistently. | 4 | 4 | 4 | C2 | Uses synthetic or copied fixtures first; no live media tag writes until backup/rollback gates exist. |
| MusicBrainz evidence cache and rate-limit planner | Cache canonical release/release-group/recording topology with provenance, expiry, and request budgets for later Import Brain/tag tools. | 4 | 3 | 4 | C1/B | Cached canonical data expires; stale IDs can explain but cannot authorize. |
| Privacy budget and AI prompt preflight | Before any model call, show exactly what evidence fields would be sent, verify redaction, estimate tokens/cost, and require policy approval. | 5 | 3 | 5 | B/C | AI is off by default for healer execution; preflight can block or redact but cannot approve mutation. |
| AI confidence calibration dataset | Store sanitized operator outcomes for AI-assisted suggestions so thresholds can be evaluated instead of guessed. | 4 | 4 | 3 | B/C | Aggregates only; no raw tags/paths/prompts, and calibration never silently changes execution gates. |
| AI evidence contradiction checker | Ask a model to find contradictions in already-redacted evidence packets, such as year/runtime/tag/release mismatches, before human review. | 4 | 3 | 4 | B/C1 | The model can lower confidence or request more evidence; it cannot raise authorization. |
| Evidence packet review-note drafter | Draft short review notes from sanitized structured evidence with citations to finding IDs. | 4 | 2 | 4 | B/C1 | Output is explanation, never evidence, approval, or policy. |
| Missing-evidence recommender | Suggest which allowed read-only evidence would reduce ambiguity for a blocked treatment plan. | 4 | 3 | 4 | B/C1 | Suggestions come from an allowlist of probes/questions and contain no commands. |
| Policy decision explainer | Translate deterministic gate pass/fail reasons into operator-readable summaries. | 3 | 2 | 4 | A3/B/C | Deterministic gates compute decisions; AI only explains blocked reasons. |
| Canary/batch adversarial reviewer | Challenge proposed canary or batch packets and identify missing gates or unsafe assumptions. | 4 | 4 | 3 | A3/A4/B | Can lower confidence or request review only; cannot approve mutation or relax item gates. |
| Semantic privacy risk reviewer | Review already-redacted exports, AI notes, and support bundles for subtle privacy leaks after deterministic scanners run. | 4 | 4 | 3 | B/C | AI can block or escalate suspected leaks but cannot certify privacy safety. |
| Cohort/root-cause narrative assistant | Summarize cohort evidence into hypotheses about likely causes, such as source, root outage, encoder, or import path. | 3 | 3 | 3 | B/C1 | Hypotheses require evidence references and cannot change thresholds or remediation policy. |
| AI output grounding checker | Validate AI-generated notes for unsupported claims, leaked identifiers, command-like language, or missing evidence citations. | 5 | 4 | 4 | B/C | Deterministic schemas still enforce allowed fields and fail closed. |
| Human-readable remediation runbook generator | Generate a short operator runbook per cohort or album: what was found, what evidence is missing, what the safest next manual step is. | 4 | 2 | 4 | A2.5/C1 | Runbooks contain no executable commands and no raw private values. |
| Health score and trendline | Summarize library health trends by read-only cohorts, finding churn, recurrence, stale evidence, and remediation outcomes. | 3 | 2 | 4 | A2.5/C1 | Score is navigation aid only; it must not hide high-risk findings or become a pass/fail gate. |
| Maintenance window and resource governor | Let operators constrain scan/probe/decode work by time window, disk pressure, CPU budget, and Lidarr activity state. | 4 | 3 | 4 | A2.5/A5 | Governs read-only work first; write-capable operations need stricter per-item approval gates. |
| Mutation chaos drill harness | Inject crashes, access-denied errors, disk-full states, lock contention, and partial-renames against synthetic files to prove ledger recovery. | 5 | 4 | 5 | A3 | Synthetic/plugin-owned paths only; must pass before any repair-in-place implementation touches media. |
| Support-safe fixture minimizer | Produce synthetic or heavily minimized repro fixtures that preserve structural failure signals without exposing private audio or tags. | 3 | 5 | 2 | A2.5/A3 | Default should be metadata-only or synthetic; never export user audio by accident. |
| Provider/source defect feedback loop | Aggregate sanitized defect patterns by source, release group, import age, and queue/history context to help users tune acquisition policy. | 4 | 4 | 3 | C1 | Advisory only; no automatic blocklists, quality changes, or source penalties from healer alone. |
| Policy-as-code simulator | Express future operator policies as versioned, testable rules and simulate them against findings before enabling any dry-run or execution path. | 4 | 4 | 4 | A3/C | Simulation cannot mutate; policy rules must be covered by fixtures and adversarial review. |
| Append-only library fact store | Persist scan facts, Lidarr snapshots, fingerprints, gate outcomes, and operator decisions as historical evidence instead of only current state. | 5 | 4 | 5 | A2.5/C | Facts are evidence only; compaction and retention must preserve auditability and privacy. |
| Stable logical track lineage graph | Track identity across renames, rescans, release remaps, reimports, repairs, and replacements. | 5 | 4 | 5 | C1/A3/A4 | Lineage explains history but never grants deletion, replacement, or import authority. |
| Incremental dirty-set scanner | Scan changed files, albums, or roots from Lidarr events, stored fingerprints, and filesystem hints instead of rescanning everything. | 5 | 4 | 4 | A2.5/C1 | Missed events must fall back to bounded revalidation; dirty-set membership cannot authorize action. |
| Reference-data version pinning | Record the MusicBrainz/Lidarr metadata snapshot used for a decision so ambiguous results remain reproducible. | 4 | 3 | 4 | C1/B | Stale reference data can explain old plans but blocks mutation gates until refreshed. |
| Deterministic policy DSL | Represent repair, reacquire, tag, and import gates as versioned declarative rules with static validation. | 5 | 5 | 4 | A3/C/B | AI may suggest policies, but only reviewed deterministic rules can run. |
| Machine-checkable gate proof bundle | Export a compact pass/fail proof for every required gate behind a candidate action. | 5 | 3 | 5 | A3/A4/C | Proof bundles must be redacted and must not include executable commands. |
| AI-to-rule distillation workbench | Let AI mine recurring ambiguous cases and propose deterministic predicates for humans to turn into tests and policy rules. | 4 | 4 | 4 | B/C | Proposed rules require human approval, fixtures, and adversarial review before adoption. |
| Root/library sharding coordinator | Partition very large libraries by root, artist cohort, storage tier, or risk class with independent budgets and backpressure. | 4 | 3 | 4 | A2.5/C1 | Shards must not create inconsistent authorization or partial batch approval. |
| Cross-tool contention detector | Detect concurrent Lidarr, manual, backup, tagger, or filesystem edits during diagnosis, dry-run, or future execution windows. | 5 | 3 | 5 | A2.5/A3/A4 | Any contention invalidates stale plans and fails closed to review. |
| Format lifecycle planner | Produce a read-only plan for obsolete codecs, risky containers, archival policy, sample-rate drift, and future migration pressure. | 3 | 3 | 3 | C1 | No transcoding, replacement, or quality upgrade action belongs in this planner. |

## Additional Track and Data Management Ideas

These ideas are useful once the evidence layer can explain library state consistently. They should stay read-only first, even when their eventual value is repair, reacquire, or import automation.

| Feature | Purpose | Usefulness | Complexity | ROI | Likely milestone | Safety caveat |
| --- | --- | ---: | ---: | ---: | --- | --- |
| Library inventory snapshot and diff export | Produce a bounded local manifest of artists, albums, TrackFiles, sizes, mtimes, tags-present booleans, evidence versions, and redacted path identities, then diff snapshots over time. | 5 | 3 | 4 | A2.5/C1 | Export remains local by default; stable redacted identities are privacy-bearing and need retention limits, salts, and leak scanning before sharing. |
| Manual repair intake verifier | After a user manually fixes, replaces, or reacquires a file, verify that the finding closed, the track slot is still correct, and no new symptom appeared. | 5 | 3 | 5 | C1/A3/A4 | Requires fresh fingerprint, TrackFile, track-slot, and contention-invalidated evidence; it records outcomes only and never grants permission for future automatic fixes. |
| Empty-slot local candidate finder | Find unmanaged on-disk files that may satisfy monitored empty track slots using deterministic IDs, normalized names, durations, and release topology. | 5 | 4 | 4 | C1/B | Review-only; no import, move, rename, delete, or replacement without the later Import Brain gates. |
| Playlist and collection impact analyzer | Identify playlists, `.m3u` files, external collection exports, and player indexes that reference files affected by a planned repair, rename, release remap, or reacquire. | 4 | 3 | 4 | C1/A3/A4 | Impact analysis blocks risky plans but cannot update playlists or external apps by itself. |
| Backup-manifest checksum auditor | Compare Brainarr/Lidarr-visible files to an operator-provided backup manifest or checksum inventory to detect unprotected, changed, or manifest-mismatched tracks. | 5 | 3 | 5 | C1/A3 | A manifest proves only manifest agreement; restore confidence also needs manifest provenance, freshness, hash privacy, and restore-drill status. |
| Downstream visibility smoke verifier | Optionally check whether Navidrome, Plex, Jellyfin, or another player can see representative repaired/reacquired tracks after Lidarr reconciliation. | 4 | 4 | 3 | C1/D | Metadata-only by default; playback probes are external side-effect tests and require isolated accounts/endpoints, explicit opt-in, and no automatic rescans or deletes. |
| Remediation cost and blast forecast | Estimate disk, CPU, time, network, provider-search, backup, and review costs for repair, reacquire, tag repair, or import candidates. | 4 | 3 | 4 | A2.5/A3/A4 | Forecasts explain cost and risk only; they cannot relax item-level safety gates. |
| Quarantine candidate planner | Identify files that should be isolated from playback or automation because they are corrupt, ambiguous, or unsafe to mutate, and produce a review packet. | 4 | 3 | 3 | C1/A3 | Planning only; moving files into quarantine is a write-capable operation requiring its own journal and restore path. |
| Rip-log, CUE, and AccurateRip audit | Read `.log`, `.cue`, and optional checksum evidence to catch bad CD rips, missing pregaps, disc-index mismatch, or track-boundary problems. | 3 | 4 | 3 | C1/A5 | Evidence is format-specific and often incomplete; never auto-repair from rip-log signals alone. |
| Album continuity and gapless audit | Detect suspicious album-level continuity issues such as truncated endings, excessive leading silence, hidden-track layout, or broken gapless transitions. | 4 | 4 | 3 | A5/C1 | Slow optional analysis; content heuristics require human review and fresh decode evidence. |
| Custom metadata policy linter | Let operators define read-only tag expectations for genres, sort fields, MusicBrainz IDs, replaygain, artwork size, and local naming conventions. | 4 | 3 | 4 | C1/C2 | Lints only; tag writes still require backup, diff, rollback, canonical ID proof, and approval. |
| Source trust and provenance profile | Let operators classify sources or roots as suspicious, archival, transient, or external so findings can be prioritized and evidence requirements can be increased. | 4 | 3 | 4 | A2.5/C1 | Profiles can only raise scrutiny, priority, or required evidence; they never lower freshness, provenance, redaction, approval, or mutation gates. |
| External-library correlation adapter | Optionally correlate Lidarr-managed tracks with Navidrome or other local-library databases to find playback-only symptoms, stale indexes, or files outside Lidarr's control. | 4 | 4 | 3 | D/C1 | Companion-style and read-only; external app state cannot become Lidarr mutation authority. |
| Release migration delta explainer | Turn future release-migration assistant output into an operator-readable diff of track slots, tags, files, and monitored state under candidate MusicBrainz release remaps. | 4 | 3 | 4 | C1/B | Explanation layer only; migration remains blocked until Lidarr release-remap semantics, rollback, and operator approval are proven. |
| Operator-facing remediation run history | Show what was diagnosed, manually fixed, verified, waived, reacquired, or still blocked across time per album, root, source, and workflow. | 5 | 3 | 5 | C1/A3/A4 | History must preserve auditability without storing raw private paths, tags, or AI prompts. |

## Additional Operational QoL and Governance Ideas

These ideas focus on making a large library manageable day to day. They are intentionally framed as prioritization, policy, review, and explanation layers; none of them converts advisory evidence into permission to mutate files or Lidarr state.

| Feature | Purpose | Usefulness | Complexity | ROI | Likely milestone | Safety caveat |
| --- | --- | ---: | ---: | ---: | --- | --- |
| Risk-prioritized operator queue | Sort review work by risk, blast radius, freshness, recurrence, root health, backup readiness, and user impact. | 5 | 3 | 5 | A2.5/C1 | Priority affects review order only; it must not hide findings or authorize actions. |
| High-risk notification rules | Notify operators about new high-risk findings, root outage storms, stale evidence, backup-readiness failures, or recurring import failures. | 4 | 3 | 4 | A2.5/C1 | Opt-in, redacted, rate-limited, and advisory; notifications cannot enqueue commands. |
| Lidarr configuration conformance inspector | Audit naming, root folders, recycle-bin configuration, quality profiles, metadata settings, permissions, and import behavior that affect healing safety. | 5 | 3 | 5 | A2.5/A3/A4 | Audit-only; unsafe configuration blocks later dry-runs but is never changed automatically. |
| Edition and variant protected-scope extension | Extend the protected-scope model so operators can mark releases, editions, media types, or albums as preserve-only before repair, import, tag, or reacquire features exist. | 5 | 3 | 5 | C1/A3/A4/B | Protection can only block or escalate a plan; it cannot choose a replacement or downgrade evidence gates. |
| Upgrade, downgrade, and edition drift guard | Compare candidate imports, reacquires, and release remaps against current quality, edition, release, medium, and monitored state. | 5 | 4 | 4 | C1/A4/B | Any drift fails closed to review; it never decides deletion, replacement, or upgrade eligibility by itself. |
| Review workload planner | Estimate review effort by cohort, album, root, source, workflow, and missing evidence so users can clear the safest/highest-impact queues first. | 4 | 2 | 4 | A2.5/C1 | Planning is informational; it cannot batch-approve or batch-suppress findings. |
| Library ownership boundary map | Distinguish Lidarr-managed files from external, Spotify-derived, manually managed, unmanaged, and player-only content. | 5 | 3 | 5 | C1/D | External or player state can explain symptoms, but only Lidarr-owned evidence can feed Lidarr mutation gates. |
| Source-of-truth policy simulator | Simulate operator preferences for conflicts between Lidarr DB state, MusicBrainz data, file tags, folder names, and local policy. | 4 | 4 | 4 | C1/B | Policies are versioned simulations until separate write contracts exist; AI may explain conflicts only, while deterministic policy and explicit operator choices set precedence. |
| Release availability scout | For decode-proven unrecoverable files, preview whether Lidarr search candidates likely exist before an operator spends time on repair. | 4 | 4 | 3 | A4 | Read-only search/availability evidence only; no grabs, queue pushes, deletes, or replacements. |
| Folder hygiene and path-collision audit | Detect path length risks, reserved names, case/accent collisions, duplicate artist folders, and normalization drift such as ampersands, apostrophes, dashes, and accents. | 4 | 2 | 4 | C1 | Report-only; renames and path rewrites require separate simulation, approval, and rollback contracts. |
| Review packet bulk-labeling UX | Add saved filters, keyboard-friendly labels, same-root annotations, and bulk "same cause" review metadata. | 4 | 3 | 4 | C1 | Bulk labels are review context only; suppression, waiver, or approval requires a separate fingerprint-bound, expiring, audited artifact. |
| Post-automation reconciliation checklist | After operator-approved or separately execution-authorized repair, reacquire, tag, or Lidarr-orchestrated work, verify ledger state, backup state, TrackFile mapping, TagLib read, evidence closure, and optional downstream visibility. | 5 | 3 | 5 | A3/A4/C2 | Checklist results are fresh evidence for that operation only; they do not cover manual fixes or grant future automatic repair permission. |
| AI provider policy guardrail | Define no-AI, local-only, cloud-allowed, redaction-required, token-budget, and adversarial-check policies for future AI-assisted review tools. | 4 | 3 | 4 | B/C | AI policy can only restrict, redact, or require review; it cannot authorize mutation or certify safety. |

Highest-ROI additions to consider pulling forward after A2 projection conformance: evidence contract golden packs, field sensitivity annotations, classifier replay benches, root outage coalescing, container/codec mismatch signatures, library inventory snapshots, backup-manifest checksum audits, FLAC STREAMINFO MD5 verification, recycle-bin and backup readiness, known-bad signatures, import rejection explanations, privacy/AI prompt preflight, risk-prioritized queues, Lidarr configuration conformance, edition/variant protected scopes, ownership boundary mapping, post-automation reconciliation, append-only fact storage, cross-tool contention detection, manual repair intake verification after fresh-state gates exist, machine-checkable gate proofs, and mutation chaos drills. These improve later repair/reacquire safety without requiring media writes in the near term.

## Recommended Next Pulls

The best near-term sequence is intentionally smaller than the full A2.5/C/A3 backlog and stays read-only until the synthetic mutation drills:

1. Finish A2 projection conformance: treatment plans and summary counts in `healer/getfindings`.
2. Evidence contract golden pack; initial `healer/getfindings` projection and treatment-vocabulary snapshots are the first slice, and future slices should add fixtures as fields, workflows, exports, or AI review packets are introduced.
3. Field sensitivity annotations; initial `healer/getfieldcatalog` coverage for `healer/getfindings` is in place, and future export or AI-review fields must extend the catalog with tests.
4. Finding revalidation loop.
5. Classifier replay/regression bench.
6. State schema migration/corruption harness plus legacy-state fixture corpus.
7. Evidence provenance fingerprint.
8. Evidence TTL policy.
9. Global read-only kill switch and approval authority model.
10. Fingerprint acquisition and retention policy.
11. Lidarr state snapshot/diff inspector.
12. Basic storage/root health audit plus root outage storm coalescing.
13. Lidarr host API conformance harness plus tool availability quick check.
14. Redaction policy verifier for all stores plus export leak-scanner fixture corpus.
15. Local diagnostic evidence export.
16. Basic triage filters/API; initial `workflow`, `risk`, `blockedReason`, and `authorized` filters are in the first A2.5 slice.
17. Read-only probe collector plus container/codec mismatch signatures.
18. Targeted decode-on-finding verifier plus FLAC STREAMINFO MD5 verification.
19. Fixture truth-table lab, local fixture health check, toolchain reproducibility runner, and synthetic fixture generator.
20. Metrics/cardinality and scan-cost budget plus healer scan-cost report.
21. Album-level finding packet aggregator and evidence change explainer.
22. Library inventory snapshot/diff export.
23. Backup-manifest checksum auditor.
24. Playlist and collection impact analyzer.
25. Remediation cost and blast forecast.
26. Privacy budget and AI prompt preflight for any later AI-assisted review note.
27. Append-only library fact store.
28. Cross-tool contention detector.
29. Manual repair intake verifier after fresh-state and contention gates.
30. Mutation-grade root containment proof plus root capability atlas.
31. Recycle-bin and backup readiness doctor.
32. Mutation safety drill on synthetic files plus dummy restore rehearsal.
33. Operation ledger state machine and machine-checkable gate proof bundle.
34. Risk-prioritized operator queue, review workload planner, and high-risk notification rules.
35. Lidarr configuration conformance inspector.
36. Protected scope list with edition/variant semantics plus upgrade/downgrade drift guard.
37. Library ownership boundary map and folder hygiene/path-collision audit.
38. Post-automation reconciliation checklist.

This sequence improves trust, debuggability, scale, privacy, and evidence quality without introducing media writes through A2.5. It also reduces A3/A4 risk by proving contract stability, field sensitivity, freshness, state migration, probe behavior, fixture behavior, redaction, root containment, backup readiness, host semantics, and synthetic recovery before repair or reacquire exists.

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
- evidence contract golden packs and classifier replay;
- field-sensitivity expansion for future export and AI-review packet fields;
- schema migration and corruption handling;
- provenance and TTL;
- read-only kill switch, protected scopes, and approval authority model;
- fingerprint policy;
- Lidarr state snapshots and diffing;
- storage/root health audit and root outage coalescing;
- host/API conformance and tool availability checks;
- redaction verification and leak-scanner fixtures;
- local diagnostic export;
- triage filters, with initial workflow/risk/blocked-reason/authorization filtering before freshness and review-lifecycle expansion;
- risk-prioritized queues, workload planning, and redacted high-risk notifications;
- probe collection and container/codec signatures;
- targeted decode verification and FLAC STREAMINFO MD5 checks;
- fixture truth-table work, local fixture health checks, and toolchain reproducibility;
- metrics/cardinality and scan-cost budgets;
- evidence change explanations and album-level finding packets;
- Lidarr configuration conformance, edition/variant protected scopes, and upgrade/downgrade drift guards;
- library inventory snapshots and backup-manifest checksum audits;
- playlist/collection impact analysis and remediation cost forecasts;
- AI prompt privacy preflight before any model-assisted review notes;
- library ownership boundaries and folder hygiene/path-collision audits;
- append-only fact storage;
- cross-tool contention detection;
- manual repair intake verification after fresh-state and contention gates.

A3 should start only after A2.5 has enough fixture and live-read-only evidence to make repair dry-runs meaningful, after mutation-grade root containment and the root capability atlas are proven, after backup/recycle readiness is audited, and after synthetic mutation plus dummy-restore drills prove the journal/backup/rollback path outside the library. A4 should remain separate from A3 because reacquire is a Lidarr workflow with album-wide and recycle-bin risks, not a file repair workflow.

The C-series data-management tools should be developed mostly read-only first. Track-slot coverage, deterministic slot indexes, release topology, empty-slot local candidate discovery, queue/history diagnostics, import rejection explanations, source-of-truth conflict detection, duplicates, survivorship review, sidecars, playlists, external-library correlation, artwork, ReplayGain, metadata policy linting, inventory snapshots, backup manifests, and tag canonicalization can deliver operator value before any write path exists.

AI-assisted tools should remain late and advisory. They may draft review notes, suggest missing evidence from an allowlist, explain deterministic policy decisions, challenge ambiguous identity/release decisions, or block suspicious privacy leaks, but model output is never evidence, never execution authorization, never a privacy certification, and never a source of filesystem paths or Lidarr commands.
