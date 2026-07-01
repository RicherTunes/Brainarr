# Brainarr vNext Import Brain and Library Healer Design

Status: Draft for iteration
Date: 2026-06-30
Owner: RicherTunes / Brainarr

## Purpose

Brainarr vNext should expand from AI-powered discovery into two separate, safety-first capabilities:

1. Library Healer: a Lidarr-managed audio-file health workflow that detects, reports, and later repairs broken or TagLib-unreadable files.
2. Import Brain: an AI-assisted import adjudicator for ambiguous manual-import cases where deterministic matching refuses to claim data that is probably correct.

These are separate milestones. They share review, audit, observability, and provider infrastructure, but they must not share write-path assumptions. Library Healer can touch files in later phases; Import Brain can influence imports. Both need stricter gates than recommendation approval.

## Product Direction

Ship the first milestone as a Lidarr-first plugin feature because Lidarr is the Arr ecosystem with the strongest plugin architecture today. Keep core models and policy boundaries portable so Radarr/Sonarr adapters can be added later if their plugin ecosystems catch up.

The first implementation milestone is Library Healer read-only detection. It must not write, replace, delete, or move media files. Its output is a report, metrics, and reviewable evidence. Repair-in-place and reacquire workflows are later milestones after the detection model is proven on fixtures and real libraries.

Import Brain remains a later milestone. It can use AI for judgment over ambiguous identity, edition, numbering, weak tags, and foreign-title cases, but deterministic safety gates decide whether any action is allowed.

## Non-Negotiable Engineering Policy

All development for these features follows test-driven development.

- No production code without a failing test first.
- Every behavior change starts with a focused RED test.
- The test must be run and observed failing for the expected reason.
- Implementation must be the minimum GREEN change.
- Refactoring happens only after tests pass.
- Bug fixes require a regression test that fails before the fix.
- Feature tasks must document the exact test command and observed red/green output.

All implementation work also requires adversarial review.

- The design gets an adversarial plan review before implementation begins.
- Each implementation task gets a task-scoped review for spec compliance, data-loss risk, and test quality.
- The full branch gets a final adversarial review before merge.
- Any Critical or Important finding blocks progress until fixed and re-reviewed.
- Reviewers must be asked to challenge destructive assumptions, API drift, crash recovery, rollback, stale metadata, and scope creep.

## Current Brainarr Fit

Brainarr already has patterns worth reusing:

- Provider abstraction and model routing through `IAIProvider` and Common's `ILlmProvider`.
- Confidence floors, MusicBrainz enrichment, hallucination filters, and review queue behavior for recommendations.
- Structured JSON validation and parsing patterns.
- Metrics, correlation IDs, provider health, rate limiting, and cache discipline.
- File-backed review queue patterns using atomic JSON storage.
- Lidarr plugin visibility through `BrainarrInstalledPlugin`, plus the current import-list surface through `Brainarr`.
- Review-action audit concepts, dry-run behavior, idempotency keys, and rollback-oriented action records.
- Explainable triage patterns with reason codes and risk scoring.

Do not reuse the recommendation safety gate directly for file import or repair. Recommendation approval adds metadata. Import and repair workflows can move files, replace paths, or trigger album-wide behavior in Lidarr. They require separate safety contracts, stronger audit records, and explicit rollback plans.

Do not reuse these recommendation-domain pieces directly:

- `Recommendation` and recommendation contracts. Import and repair proposals need file ids, paths, fingerprints, destination paths, operation ids, planned operations, evidence, and rollback data.
- `SafetyGateService` semantics. File-affecting workflows need no-op defaults, filesystem boundary checks, collision checks, free-space checks, strict preflight, and explicit confirmation.
- `ReviewQueueService` storage shape. Its `artist|album` key is not stable enough for file operations.
- `RecommendationJsonParser` salvage behavior. Salvaging partial model JSON is acceptable for suggestions, not mutation plans. Any future action plan schema must validate strictly and fail closed.

Brainarr currently has three relevant host surfaces:

- `Brainarr : ImportListBase<BrainarrSettings>` is the live Lidarr import-list surface for fetch, test, and `RequestAction` workflows.
- `BrainarrPluginHost : StreamingPlugin<BrainarrModule, BrainarrSettings>` is the Common bridge surface and currently returns no indexer.
- `BrainarrInstalledPlugin : NzbDrone.Core.Plugins.Plugin` makes Brainarr visible in Lidarr's System -> Plugins UI. This uses Lidarr's host plugin interface, not the Common sandbox abstraction.

The Library Healer must verify its final integration point against the active Lidarr plugins-branch assemblies before implementation. The vendored source currently exposes the expected services and commands, but `Brainarr.Plugin.csproj` can compile against multiple `LidarrPath` sources. The implementation plan must pin and record the exact assembly source used for the feature branch.

## Milestone A: Library Healer

### Goal

Detect Lidarr-managed audio files that are broken, unreadable by Lidarr's own tag reader, or likely header-repairable, then present a read-only report with evidence. Later phases may repair or reacquire, but only after the detection phase is proven.

### Why In-Process Lidarr Plugin

The earlier script had to read Navidrome SQLite, translate container/host paths, replicate a separate tag reader through a sidecar, and rely on a non-admin user to trigger rescans. Inside Lidarr, the relevant state is already in-process:

- `TrackFile.Path` is the real managed on-disk path.
- Lidarr's own tag reader defines the user-visible symptom.
- Lidarr command queue can trigger refresh, rescan, or search workflows without HTTP automation.
- Existing plugin loading and service discovery can wire command handlers and event handlers if the target Lidarr branch supports them.

Expected Lidarr services and commands to verify:

- `IArtistService.GetAllArtists()`
- `IMediaFileService.GetFilesByArtist(int artistId)`
- `IAudioTagService` for the same tag-reading path Lidarr uses during import.
- `IManageCommandQueue` for later rescan, refresh, or search phases only.
- `RefreshArtistCommand`, `BulkRefreshArtistCommand`, `RescanFoldersCommand`, and `AlbumSearchCommand` for later phases.

Scheduled-task caveat: Lidarr's scheduled task set is host-owned and seeded by its task manager. The design must not assume a plugin can add a first-class scheduled task to Lidarr's task UI/history. If periodic scanning is needed, use a plugin-owned timer only to request a bounded Brainarr-owned scan runner, and dispose that timer through plugin unload/shutdown hooks. Do not run scans inline from the timer.

### Prior `music-helper` PoC Lessons

The earlier `D:\Alex\music\music-helper` proof of concept is useful for Brainarr, but mostly as design pressure, fixture material, and safety invariants rather than direct plugin code.

Port now or keep as A2/A3 prerequisites:

- Keep reader-tagged evidence. The dominant failure class can be "TagLib reads zero/unreadable while ffmpeg decodes cleanly", so ffmpeg success alone is not proof the Lidarr/Navidrome symptom is fixed.
- Add an action-eligibility concept before any mutating phase. A classifier label and permission to repair/reacquire are separate facts. Sibling-median or weak external-duration suspicion must remain non-destructive.
- Use the PoC's ffprobe/decode parsing model for future probe phases: split fatal errors, truncation hints, and recoverable warnings. Recoverable warnings are never decisive by themselves.
- Use the PoC's fixture-gate pattern. Real fixture files must prove that intact-audio cases are never routed to destructive actions and that malformed/truncated cases are classified conservatively under the pinned toolchain.
- Use the PoC's repair-in-place safety ordering for A3: same-directory temp file, complete verification before swap, journal `APPLYING` before moving the source, backup-first rename, same-mount proof, post-move reverify, startup reconciliation, and backup purge only after confirmation.
- Do not preserve old mtimes after a successful file migration; old mtimes made repaired files invisible to incremental scans in the PoC.
- Treat `tools/taglibprobe` as a parity oracle for Navidrome go-taglib during experiments, not as a runtime dependency of the Lidarr plugin. The plugin's live symptom reader remains Lidarr's in-process TagLib path.
- Keep the script's Navidrome SQLite reader, path mapper, REST client, and Docker deploy notes as companion-service reference only. They should not leak back into the in-process Lidarr A1 architecture.

Before enabling any repair-in-place or reacquire milestone, rerun an adversarial review over the C# port of these invariants, with special attention to filesystem state transitions, backup lifecycle, reader divergence, and album-wide Lidarr replacement behavior.

### Phase A1: Detect and Report Only

This is the first build target.

Inputs:

- Managed artists from Lidarr.
- Managed track files for each artist.
- Direct on-disk file path from each `TrackFile`.
- TagLib-derived duration and tag-read success/failure.
- TagLib-derived tag presence for title, artist, album, and MusicBrainz identifiers, stored only as booleans and generic missing-field names.
- Optional `ffprobe` metadata where available.
- Optional full-decode result only in explicit integrity-sweep mode.

Outputs:

- Finding records stored in Brainarr plugin AppData.
- UI/action endpoint or report view listing affected files and evidence.
- Metrics: scanned files, unreadable files, duration-zero files, probe failures, skipped files, elapsed time.
- No file writes except the plugin's own report/ledger files.

Phase A1 must not:

- Modify media files.
- Delete media files.
- Trigger search/reacquire.
- Trigger repair.
- Rename files.
- Preserve or change media mtimes.

### Phase A1 Mutation Boundary

Phase A1 is read-only over the media library. It may write only Brainarr-owned diagnostic/report files under the plugin AppData root. It must not reference or invoke media-mutating APIs.

Forbidden A1 references:

- `IAudioTagService.WriteTags`
- `IAudioTagService.SyncTags`
- `IAudioTagService.RemoveMusicBrainzTags`
- `IMediaFileService.Update`
- `IMediaFileService.Delete`
- `IMediaFileService.DeleteMany`
- `IMediaFileService.UpdateMediaInfo`
- `IManageCommandQueue.Push`
- `RefreshArtistCommand`
- `BulkRefreshArtistCommand`
- `RescanFoldersCommand`
- `AlbumSearchCommand`
- direct filesystem rename, move, delete, copy-over, or metadata timestamp writes against media paths.

Required enforcement:

- Architecture tests fail if A1 production code references the forbidden APIs or commands.
- Filesystem canary tests prove scan execution does not change media file bytes, file length, or mtime.
- A1 tests prove no provider/LLM call is made during detection.
- Any future exception to this boundary requires a new milestone and spec revision.

### Phase A1 Components

`LibraryHealerScanRunner`

- Plugin-owned scan runner that scans in bounded batches.
- Must be resumable and cancellable.
- Must rate-limit disk and probe work.
- Must not run scan logic inline from a timer or UI action; UI/timer may request a Brainarr-owned background scan runner only.
- Must not use `IManageCommandQueue.Push` in A1.
- If the API spike proves a safe host command handler is preferable, the spec must be revised before coding to define the allowed Brainarr-owned command path and update the A1 mutation-boundary tests.

`ManagedTrackFileEnumerator`

- Uses Lidarr services to enumerate managed `TrackFile` records.
- Records artist id, album ids when cheaply available, track file id, path, size, mtime, extension, and media profile facts.
- Skips missing paths and records them separately.

`TagLibSymptomReader`

- Uses the same Lidarr tag-reading path that Lidarr imports use.
- Captures read success/failure, duration, tag presence, and cover-art presence when available.
- Treats TagLib duration zero or read failure as a symptom, not as proof of corruption.

`MediaProbe`

- Detects availability and version of `ffprobe` and `ffmpeg`.
- Phase A1 can run without `ffmpeg`; repair phases cannot.
- Uses `ffprobe` to capture container, streams, codec, duration, and structural errors where available.
- Never shells out with string-built commands; arguments are passed structurally.
- Canonicalizes configured/discovered binary paths and records the resolved path and version in diagnostics.
- Runs with bounded timeout plus stdout/stderr byte caps.
- Uses a non-media working directory.
- Disables network/protocol inputs where the tool supports it.
- Treats symlink/reparse-point media paths as review-only until root containment policy is proven.
- Tests hostile filenames, including names beginning with `-`, Unicode, quotes, newlines, very long paths, missing permissions, and symlink/reparse paths.

`DecodeVerifier`

- Optional in A1. Used for explicit truncation/integrity sweeps, not default scans.
- Runs full decode checks only under configured concurrency and time limits.
- Fatal decode error or decoded duration less than 95% of the best canonical duration signal can classify a file as genuinely bad only when the decode verifier actually ran and produced a complete result.

`FileClassifier`

Pure function over evidence. No IO.

Classifications:

- `FalsePositive`: evidence does not prove a user-visible problem.
- `TagMetadataIssue`: Lidarr can read the file and duration, but core tags needed for reliable matching are missing or incomplete.
- `TagReaderSymptom`: Lidarr's tag reader sees zero duration or fails, but audio integrity is not yet proven.
- `ProbeEvidence`: optional probe/decode evidence attached to a finding without implying repair authorization.
- `HeaderRepairCandidate`: hidden/internal until Phase A2; requires TagLib failure plus successful probe/decode evidence that suggests intact audio in a problematic container/header.
- `GenuinelyBad`: fatal decode error, decoded duration zero, or materially truncated decode.
- `NeedsHumanReview`: evidence conflicts or is incomplete.

Rules:

- Never classify `GenuinelyBad` from sibling medians alone.
- Never classify corruption from stale stored metadata.
- A TagLib symptom must be confirmed by fresh file read.
- A1 user-facing labels are observational: `FalsePositive`, `PathInconsistency`, `TagMetadataIssue`, `TagReaderSymptom`, `ProbeEvidence`, and `NeedsHumanReview`.
- Tag metadata evidence must not persist raw artist names, album titles, track titles, or MusicBrainz values; it records only presence booleans and generic missing-field names.
- Missing tag field names must be recomputed from the presence booleans only, with a fixed output vocabulary (`title`, `artist`, `album`, `musicBrainzId`) at classifier, persistence, and action-output boundaries instead of trusting stored or reader-provided strings.
- Tag metadata reason codes must be recomputed from the same booleans and merged only with an allow-listed diagnostic reason vocabulary before persistence or action output.
- Stale `TagMetadataIssue` labels must be downgraded when the sanitized booleans indicate complete metadata, and free-text errors containing tag-field terms or MusicBrainz-like identifiers must be fully redacted before persistence or action output.
- `HeaderRepairCandidate` is not shown as an actionable label until A2 dry-run verification exists.
- `HeaderRepairCandidate` requires successful full decode or equivalent structural proof from the repair dry-run.
- `GenuinelyBad` requires decode verifier evidence; absent full decode, use `NeedsHumanReview`.
- Duration comparisons use tolerance bands: within 2 seconds or 1%, whichever is larger, is equivalent; less than 95% of the best canonical duration signal is materially short; between those bands is `NeedsHumanReview`.
- Ambiguous evidence routes to review, not action.

Disagreement matrix:

- TagLib OK, ffprobe OK, decode OK: `FalsePositive`.
- TagLib zero/fail, ffprobe OK, decode not run: `TagReaderSymptom` with `ProbeEvidence`.
- TagLib zero/fail, ffprobe OK, decode OK: hidden `HeaderRepairCandidate` for A2 only; A1 displays `TagReaderSymptom`.
- TagLib zero/fail, ffprobe fail, decode not run: `NeedsHumanReview`.
- Decode fatal error or decoded duration zero: `GenuinelyBad`.
- Decode materially short against canonical duration: `GenuinelyBad`.
- Any conflict not covered above: `NeedsHumanReview`.

`HealerFindingStore`

- Stores read-only findings with a path fingerprint: track file id, path, size, mtime, extension, and hash only if explicitly configured.
- Uses plugin AppData, atomic writes, and schema versioning.
- Supports stale finding detection when file size/mtime/path changes.
- Uses stable operation/finding ids, not artist/album text keys.
- Stores canonical resolved path and root-containment result.
- Treats reparse/symlink paths, hardlink ambiguity, case-only path changes, and mtime precision conflicts as stale/review-only.

`HealerTriageAdvisor`

- Deterministic first.
- AI may later summarize findings or suggest priority, but must not authorize file actions.

### Later Library Healer Phases

Phase A2: Repair dry-run

- Verify `ffmpeg` availability.
- Produce temp repaired outputs in a controlled temp area.
- Run the full verify battery against temp outputs.
- Do not replace originals.

Phase A3: Repair-in-place opt-in

- Backup original outside the active library.
- Write temp in same filesystem as final target.
- Journal intent before moving anything.
- Rename temp to final only if all verification passes.
- Never clobber existing final or backup.
- Do not preserve old mtime.
- Queue Lidarr rescan/refresh.
- Keep backup until Lidarr confirms the replacement.

Repair phases require a crash-recovery state machine before implementation. Each filesystem operation must write an intent record before the operation and a result record after the operation. On startup, reconciliation runs before new work.

Required state transitions:

- `PLANNED`: immutable operation plan written; preflight not yet started.
- `TEMP_CREATING`: intent to create remux temp written.
- `TEMP_CREATED`: temp path, size, and fingerprint recorded.
- `TEMP_VERIFIED`: verify battery passed for temp output.
- `BACKUP_MOVING`: intent to move original to backup written.
- `BACKUP_MOVED`: backup path, size, fingerprint, and original-path absence recorded.
- `FINAL_MOVING`: intent to move verified temp to final path written.
- `FINAL_MOVED`: final path, size, fingerprint, and mtime recorded.
- `POST_VERIFYING`: intent to re-read final with TagLib/probe/decode written.
- `APPLIED`: post-apply verification passed.
- `LIDARR_REFRESH_QUEUED`: rescan/refresh command id recorded when available.
- `CONFIRMED`: Lidarr has reconciled the repaired file.
- `ROLLBACK_PLANNED`: rollback intent written.
- `ROLLED_BACK`: original backup restored or failure documented with remaining manual recovery path.
- `FAILED`: operation failed without media mutation, or failed after mutation with explicit recovery instructions.

Crash-injection tests are required for every transition before A3 implementation can be accepted.

Phase A4: Reacquire fallback

- Only for `GenuinelyBad` or unrecoverable files.
- Must disclose album-wide replacement risk.
- Must require recycle bin configured.
- Must prefer Lidarr's safe deletion/recycle path over raw file deletion.
- Must be opt-in per batch or item.

Phase A5: Truncation sweep

- Explicit, slower integrity mode.
- Full-decode checks for valid-header-but-truncated files.
- Bounded concurrency and resumable progress are mandatory.

### Library Healer Safety Invariants

Repair phases must obey these constraints:

- Never replace/delete an existing file without an explicit, journaled plan.
- Never act below a high confidence threshold.
- Never clobber an existing backup or final path.
- Use same-filesystem rename for final apply.
- Move original to a backup outside the active library before replacing.
- Resolve canonical path, verify root containment, verify same volume, and perform immediate pre-action re-stat before any write-capable action.
- Require content fingerprint before any write-capable phase.
- Re-verify after apply.
- Keep an auditable rollback ledger.
- Reconcile incomplete nonterminal ledger states on startup before any new work.
- Backups are deleted only after Lidarr confirms the repaired file.
- A deterministic safety layer disposes; AI only proposes explanations or priorities.

Ledger statuses:

- `PLANNED`
- `TEMP_CREATING`
- `TEMP_CREATED`
- `TEMP_VERIFIED`
- `BACKUP_MOVING`
- `BACKUP_MOVED`
- `FINAL_MOVING`
- `FINAL_MOVED`
- `POST_VERIFYING`
- `APPLIED`
- `LIDARR_REFRESH_QUEUED`
- `CONFIRMED`
- `ROLLBACK_PLANNED`
- `FAILED`
- `ROLLED_BACK`

Ledger key:

- `TrackFile.Id`
- canonical resolved path
- size
- mtime
- mandatory content fingerprint for write-capable phases
- volume id or equivalent same-volume proof for write-capable phases

### Library Healer Test Strategy

Development starts with fixture tests before implementation.

Required fixture matrix:

- Healthy FLAC.
- Healthy M4A/MP4.
- FLAC audio in unexpected container/header shape that TagLib reads as zero or unreadable.
- Rewrapped FLAC that TagLib reads correctly.
- Truncated file with readable header.
- Undecodable file.
- Missing path.
- File with tags and cover art to prove preservation later.

The fixture gate must include the `music-helper` invariant: no intact-audio fixture may ever be classified as eligible for a destructive action. A separate truth-table should record, per fixture, Lidarr TagLib duration/read result, optional Navidrome go-taglib result, ffprobe declared duration, full-decode duration/errors, and final classification.

Required test categories:

- Pure classifier unit tests.
- Action-eligibility tests proving labels do not automatically authorize mutation.
- Path/fingerprint staleness tests.
- Tag reader adapter tests behind fixtures.
- Probe parser tests with captured `ffprobe` JSON.
- Scan batching/resume tests.
- Redaction tests for path/report logging.
- No-write tests for Phase A1.
- Metrics cardinality tests.
- Strict schema tests for any stored finding/report contract.
- Architecture tests for forbidden A1 mutation references.
- Filesystem canary tests for bytes, size, and mtime.
- Cancellation mid-scan tests.
- Corrupt finding-store recovery tests.
- Large-library bounded memory/runtime tests.
- Process timeout and output-cap tests.
- No-provider/no-LLM tests for A1.

Required verification commands for the implementation plan:

- Unit and fixture tests: `dotnet test Brainarr.Tests/Brainarr.Tests.csproj --filter "Category=Unit|Category=Healer"`.
- Architecture/mutation-boundary tests: exact command to be defined in the A1 implementation plan once test class names exist.
- Lidarr plugin smoke test: container or local-host command proving the selected trigger runs inside the target Lidarr plugins-branch runtime.
- Full local CI before merge: `pwsh ./test-local-ci.ps1 -ExcludeHeavy` unless the user explicitly approves a different gate.

## Milestone B: Import Brain

### Goal

Help Lidarr claim data it already has when deterministic import matching fails, while preserving Lidarr's conservative safety posture.

Import Brain is for ambiguous cases, not deterministic normalization. Categories include:

- Ambiguous identity.
- Alternate or foreign titles.
- Edition vs upgrade vs duplicate decisions.
- MusicBrainz release mismatch.
- Weak or missing tags.
- Track count/title mismatch.

Do not spend LLM tokens on cosmetic string drift, Unicode normalization, ampersand/and variants, case, punctuation, or duplicate differently named folders when deterministic normalization and ID matching can handle them.

### Import Brain Pipeline

1. Deterministic prefilter
   - ID match.
   - normalized path/name match.
   - MusicBrainz canonical ID verification.
   - duplicate and empty-slot checks.

2. Evidence bundle
   - candidate canonical items.
   - file path facts.
   - tags.
   - durations.
   - track counts/titles.
   - folder siblings.
   - release group or source hints.
   - existing library state.

3. AI adjudicator
   - Outputs structured JSON only.
   - Must cite canonical IDs.
   - Must provide confidence, rationale, and evidence.
   - Must support abstain/no-decision.
   - Must reject malformed, partial, or schema-extra action plans rather than salvaging them.
   - May output only decision IDs from a deterministic candidate set.
   - Must never output filesystem paths, destination paths, command names, delete/replace/move operations, or executable action plans.
   - Treats tags, paths, sibling names, and release hints as untrusted prompt-injection-bearing evidence.

4. Deterministic safety gate
   - Verifies cited IDs exist.
   - Requires high confidence.
   - Requires empty target slot.
   - Blocks replace/delete by default.
   - Reconstructs the allowed action from verified canonical IDs and current Lidarr state, never from model-supplied paths or operation text.
   - Milestone B must not issue replace, delete, or move actions under any review path.
   - Second review may only move a case to human/manual handling outside the automated Import Brain executor.

5. Action or review queue
   - High confidence and safe action: claim/import to an already-empty canonical slot only, using deterministic action construction.
   - Anything else: human review queue with evidence and model rationale.

### Import Brain File-Operation Mode

Automated Import Brain may execute an import only if the verified host API supports a source-preserving mode, such as copy or hardlink, with no source deletion, no source move, no destination replacement, and no cleanup of existing files. If the target host API and version cannot prove source-preserving semantics, Import Brain stops at preview/human review.

Before any automated import action, the deterministic gate must verify:

- source fingerprint is fresh.
- destination root containment is valid.
- destination path does not exist.
- copy mode has enough free space.
- hardlink mode is same-volume and does not alter source link semantics in a way the host later cleans up.
- selected host request cannot choose move/delete/replace mode.
- audit record is persisted before action.

An empty DB slot is necessary but not sufficient.

### Import Brain Deployment Shape

For Lidarr, prefer an in-plugin surface when viable:

- Brainarr settings expose Import Brain configuration.
- Plugin actions expose preview, explanation, and queue state.
- Lidarr command queue or in-process services are used only after API verification.

For Radarr/Sonarr, keep the core portable:

- External API adapter can drive manual-import preview where no plugin hook exists.
- External API adapter may drive import commands only after a host/version-specific source-preserving mode is proven.
- Shared contracts remain application-neutral: evidence bundle, candidate, decision, safety result, audit entry.

### Import Brain Safety Invariants

- Never replace/delete existing files.
- Never move existing files.
- Never act below high confidence.
- Always import to an empty item/slot.
- Always verify canonical IDs before action.
- Always write an audit record before action.
- Always route low-confidence or conflicted cases to human review.
- Cache decisions by evidence fingerprint to control cost and reproducibility.
- AI rationale is advisory, not authority.
- Empty DB slot alone is never enough to act; source-preserving file-operation semantics must be proven.
- Malicious tag/path fixture tests must prove injected instructions cannot alter output schema or action selection.
- Import Brain action-plan parsing must use a strict schema with `additionalProperties: false`, complete required fields, and fail-closed behavior.
- Forbidden-reference tests must prevent Import Brain action parsing from using recommendation parser salvage behavior or recommendation queue item shapes.
- API-spike tests must prove generated host requests cannot select move, delete, replace, cleanup, or overwrite modes.
- Destination collision tests must prove existing destination paths block action.

## Documentation Requirements

Each milestone gets:

- User guide.
- Operator safety guide.
- Troubleshooting guide.
- Metrics reference additions.
- Fixture/testing guide.
- Known limitations.
- Rollback/recovery guide before any write-capable phase.
- README/docs map updates when the feature becomes user-facing.
- Wiki-content mirrors for public operational pages after the canonical docs are stable.

Read-only Library Healer documentation must explicitly state that Phase A1 does not repair or reacquire files.

Repair documentation must explain:

- backup location.
- ledger semantics.
- restore procedure.
- recycle-bin requirement for deletion-backed actions.
- ffmpeg/ffprobe dependency.
- why old mtimes are not preserved.

Import Brain documentation must explain:

- what AI is used for.
- what AI is not used for.
- confidence thresholds.
- empty-slot-only policy.
- human review queue.
- cost controls and caching.
- prompt-injection resistance boundaries.

Path privacy policy:

- Logs default to redacted basename plus stable hash.
- UI may show full paths only in an explicit diagnostic/detail view.
- Persisted findings separate raw sensitive paths from shareable diagnostics.
- Export/share flows default to redacted paths.

## Adversarial Review Checklist

Reviewers must challenge the design and implementation with these questions:

- Can this delete, replace, or orphan a media file?
- Can an LLM cause a file move/delete outside allowed roots?
- Can malformed JSON be repaired into a dangerous action?
- Can replay duplicate a mutation?
- Can a crash between filesystem moves leave the library inconsistent?
- Can a cross-device rename silently degrade into copy/delete semantics?
- Can stale mtime make Lidarr miss a repaired file?
- Can an album-wide replacement path recycle more than the user expected?
- Can an unconfigured recycle bin cause permanent loss?
- Can TagLib, ffprobe, and full decode disagree?
- Can a finding become stale because the file changed after scan?
- Can a low-confidence AI decision reach an action path?
- Can hallucinated canonical IDs pass verification?
- Can logs leak full sensitive paths, API keys, or provider prompts?
- Can large libraries cause unbounded runtime, memory, or provider cost?
- Can the plugin API drift between Lidarr plugin branch versions?
- Can a confidence score override missing evidence?

## Roadmap

1. Stabilize current Brainarr baseline
   - Build and test current mainline.
   - Confirm no unrelated worktree changes are mixed into feature work.
   - Record current Lidarr plugins-branch version and Common pin.
   - Record the active `LidarrPath` assembly source used by the branch.

2. Design review
   - Review this spec.
   - Run adversarial plan review.
   - Resolve all Critical/Important findings in the spec.

3. Pre-implementation A1 API spike
   - Pin exact `LidarrPath`.
   - Record Lidarr assembly version and hash.
   - Run a container/plugin smoke test proving DI resolution for `IMediaFileService` and `IAudioTagService`.
   - Prove the selected scan trigger runs inside Lidarr.
   - Default to a Brainarr-owned non-command trigger for A1.
   - If command discovery is chosen anyway, revise the spec with a narrow exception for a Brainarr-owned scan command before feature coding starts.

4. Implementation plan for Milestone A1
   - Write a task-by-task plan under `docs/superpowers/plans/`.
   - Every task contains RED/GREEN test commands.
   - Every task has a bounded file ownership set.

5. Milestone A1: Library Healer detect/report
   - Implement with TDD.
   - No media writes.
   - Add fixtures and documentation.
   - Run task review after each task and final adversarial review.

6. Milestone A2/A3: repair dry-run then opt-in repair
   - Requires A1 telemetry and fixture confidence.
   - Requires rollback guide before implementation.
   - Requires ffmpeg availability strategy.

7. Milestone B: Import Brain
   - Separate spec refinement.
   - Separate implementation plan.
   - AI adjudication only after deterministic prefilter and safety gate contracts are tested.
   - Host API spike must prove source-preserving import semantics before automated import execution is in scope.

## Open Decisions

- Exact Lidarr plugin branch version to target for A1.
- Whether Library Healer findings live in the existing Brainarr action endpoint or a new action namespace.
- Whether A1 stays on a Brainarr-owned non-command scan trigger or introduces a narrowly-scoped Brainarr-owned command after the API spike.
- Whether `ffprobe` is mandatory for read-only detection or only for higher-confidence classifications.
- Whether full-decode sweeps are A1 optional mode or deferred entirely to A5.
- Whether Import Brain ships as in-plugin only for Lidarr or as shared core plus companion adapter from its first milestone.

## First Feature Definition of Done

Library Healer A1 is done only when:

- All implementation followed TDD with recorded red/green evidence.
- Read-only guarantee is tested.
- Fixture matrix covers healthy, symptom, repairable, truncated, unreadable, and missing-path cases.
- Findings are stored with schema version and path fingerprint.
- Scan is batched, resumable, cancellable, and rate-limited.
- UI/action/report exposes evidence without file mutation.
- Metrics and logs are redaction-safe.
- User docs and testing docs are written.
- Task-level and final adversarial reviews are clean or all blocking findings are resolved.
- Runtime smoke test proves the selected scan trigger runs in the target Lidarr plugins-branch runtime.
