# Library Doctor A2 Triage and Evidence Planning Design

Status: Draft for review
Date: 2026-07-01
Owner: RicherTunes / Brainarr

## Purpose

A2 should make Library Healer findings useful without making the feature write-capable. The scalable long-term shape is a deterministic triage and action-plan layer that explains what the next safe treatment would be, why it is not yet executable, and which evidence must exist before a later milestone can act.

A2 is still read-only over the media library. It does not repair, remux, retag, search, delete, move, replace, rescan, or import. It extends A1 reports with a stable treatment-plan contract that future A3 repair, A4 reacquire, Import Brain, and Tagging Brain work can reuse.

## Design Choice

Use a richer plan object instead of a single recommendation label.

Labels describe the current symptom. Treatment plans describe the next safe workflow state. Keeping those separate prevents future milestones from treating a diagnostic label as permission to mutate a library file.

Example:

- `TagReaderSymptom` means Lidarr's reader has a symptom.
- `repairDryRunCandidate` means a repair workflow may be worth exploring later.
- `executionAuthorization.authorized=false` means this plan is not permission to execute that workflow.

## Scope

A2 adds:

- a pure deterministic `HealerTriageAdvisor`;
- a versioned treatment-plan DTO attached to each finding returned by `healer/getfindings`;
- summary counts grouped by candidate workflow, risk, authorization, and blocked reason;
- fixed vocabularies for candidate workflows, risk, blocked reasons, required evidence, required policy gates, and rationale codes;
- tests proving all file-affecting plans are advisory-only in A2;
- docs that explain how A2 feeds later repair, reacquire, and AI-assisted milestones.

A2 does not add:

- `ffmpeg` execution;
- temp repaired files;
- tag writes;
- Lidarr search, refresh, rescan, command queue, or import actions;
- backup or rollback ledgers for media mutation;
- AI provider calls;
- full-path disclosure in persisted diagnostics or default action output.

## Data Contract

Each projected finding gets a `treatmentPlan` object. Field names are intentionally app-neutral so the same contract can later back Lidarr plugin UI, exported reports, and cross-arr companion adapters.

```json
{
  "schemaVersion": 1,
  "candidateWorkflow": "repairDryRunCandidate",
  "confidence": 0.72,
  "risk": "medium",
  "safetyLevel": "readOnly",
  "evidenceFreshness": "current",
  "identityFreshness": "current",
  "executionAuthorization": {
    "authorized": false,
    "authority": "none",
    "reason": "A2_READ_ONLY"
  },
  "blockedReasons": [
    "REPAIR_DRY_RUN_NOT_IMPLEMENTED",
    "FULL_DECODE_EVIDENCE_MISSING"
  ],
  "requiredEvidence": [
    "FFMPEG_AVAILABLE",
    "FULL_DECODE_CLEAN",
    "TAGLIB_REREAD_AFTER_REWRAP"
  ],
  "requiredPolicyGates": [
    "BACKUP_POLICY_APPROVED"
  ],
  "rationaleCodes": [
    "TAG_READER_DURATION_ZERO",
    "NO_PROBE_EVIDENCE"
  ]
}
```

### Fields

- `schemaVersion`: integer contract version for action-output and persisted report compatibility.
- `candidateWorkflow`: fixed vocabulary value describing the next workflow candidate.
- `confidence`: deterministic confidence from 0.0 to 1.0. It is not an authorization threshold.
- `risk`: fixed vocabulary: `none`, `low`, `medium`, `high`, `critical`.
- `safetyLevel`: `readOnly` for all A2 plans.
- `evidenceFreshness`: `current`, `stale`, `missing`, or `unknown`.
- `identityFreshness`: `current`, `stale`, `missing`, or `unknown`.
- `executionAuthorization`: explicit authorization object. In A2 it is always `{ authorized: false, authority: "none", reason: "A2_READ_ONLY" }`. Freshness, corruption, and malformed-record details live in `blockedReasons`, not in authorization text.
- `blockedReasons`: fixed vocabulary explaining why the plan cannot execute now.
- `requiredEvidence`: fixed vocabulary listing evidence needed by a later milestone.
- `requiredPolicyGates`: fixed vocabulary listing operator or safety-policy gates needed by a later milestone.
- `rationaleCodes`: fixed diagnostic reason codes derived from sanitized finding evidence.

The A2 treatment plan is advisory. Mutating milestones must define a separate execution authorization, preflight, journal, and rollback contract. No later executor may treat `candidateWorkflow` plus `confidence` as permission to mutate a file or Lidarr state.

### Candidate Workflow Vocabulary

- `none`: no treatment proposed.
- `review`: human review is the next safest action.
- `repairDryRunCandidate`: later repair dry-run may be appropriate after probe/decode evidence exists.
- `tagRepairCandidate`: later tag repair may be appropriate after canonical metadata verification and backup policy exist.
- `reacquireCandidate`: later Lidarr reacquire/search workflow may be appropriate only after unrecoverable corruption evidence exists.
- `releaseReviewCandidate`: later release/edition/manual identity review may be appropriate when tags and canonical release data disagree.

The first A2 implementation may not emit every value, but tests should reserve the vocabulary and verify unknown values fail closed.

### Summary Contract

`healer/getfindings` should add a summary block:

```json
{
  "total": 42,
  "byWorkflow": {
    "repairDryRunCandidate": 13,
    "tagRepairCandidate": 9,
    "review": 20
  },
  "byRisk": {
    "medium": 22,
    "high": 20
  },
  "byWorkflowByRisk": {
    "repairDryRunCandidate": {
      "medium": 13
    },
    "review": {
      "high": 20
    },
    "tagRepairCandidate": {
      "medium": 9
    }
  },
  "authorization": {
    "authorized": 0,
    "unauthorized": 42
  },
  "blockedReasons": {
    "PROBE_EVIDENCE_MISSING": 13,
    "FULL_DECODE_EVIDENCE_MISSING": 13,
    "BACKUP_POLICY_MISSING": 13,
    "JOURNAL_POLICY_MISSING": 13,
    "REPAIR_DRY_RUN_NOT_IMPLEMENTED": 13,
    "CANONICAL_METADATA_VALIDATION_MISSING": 9,
    "TAG_WRITE_BACKUP_POLICY_MISSING": 9,
    "TAG_REPAIR_NOT_IMPLEMENTED": 9,
    "HUMAN_REVIEW_REQUIRED": 20
  }
}
```

Blocked-reason counts are exhaustive for all non-zero reasons in the returned plans, not top-N. Their sum may exceed `total` because one plan may have multiple blocked reasons. Summary values must be computed from the same sanitized plans returned to the caller. They must not read raw paths, tag values, or provider output.

## Fixed Vocabularies

All treatment-plan strings are recomputed from sanitized evidence at projection time. They are never passed through from persisted findings, AI output, tag values, raw exception text, or imported JSON.

### Blocked Reasons

- `NONE`
- `A2_READ_ONLY`
- `HUMAN_REVIEW_REQUIRED`
- `EVIDENCE_FRESHNESS_NOT_CURRENT`
- `IDENTITY_FRESHNESS_NOT_CURRENT`
- `PATH_STATE_STALE_OR_MISSING`
- `PATH_PROBE_INCONCLUSIVE`
- `PROBE_EVIDENCE_MISSING`
- `FULL_DECODE_EVIDENCE_MISSING`
- `TAGLIB_REREAD_AFTER_REWRAP_MISSING`
- `EVIDENCE_CONFLICT`
- `BACKUP_POLICY_MISSING`
- `JOURNAL_POLICY_MISSING`
- `ROLLBACK_GUIDE_MISSING`
- `CANONICAL_METADATA_VALIDATION_MISSING`
- `TAG_WRITE_BACKUP_POLICY_MISSING`
- `TAG_REPAIR_NOT_IMPLEMENTED`
- `REPAIR_DRY_RUN_NOT_IMPLEMENTED`
- `RECYCLE_BIN_POLICY_MISSING`
- `ALBUM_WIDE_SCOPE_NOT_DISCLOSED`
- `LIDARR_SEARCH_DRY_RUN_NOT_IMPLEMENTED`
- `MALFORMED_FINDING_RECORD`
- `UNKNOWN_FINDING_LABEL`

### Required Evidence

- `NONE`
- `FRESH_FILE_FINGERPRINT`
- `FRESH_PATH_IDENTITY`
- `FFPROBE_STREAM_EVIDENCE`
- `FFMPEG_AVAILABLE`
- `FULL_DECODE_CLEAN`
- `FULL_DECODE_FATAL`
- `TAGLIB_REREAD_AFTER_REWRAP`
- `CANONICAL_METADATA_VALIDATION`
- `MUSICBRAINZ_RELEASE_VALIDATION`
- `LIDARR_SEARCH_DRY_RUN_RESULT`

### Required Policy Gates

- `NONE`
- `BACKUP_POLICY_APPROVED`
- `JOURNAL_POLICY_APPROVED`
- `ROLLBACK_GUIDE_PUBLISHED`
- `RECYCLE_BIN_CONFIGURED`
- `ALBUM_WIDE_SCOPE_ACCEPTED`
- `EXPLICIT_OPERATOR_OPT_IN`

### Execution Authorization Values

`executionAuthorization.authority` is fixed to:

- `none`

`executionAuthorization.reason` is fixed to:

- `A2_READ_ONLY`

A2 must not emit any other authority or reason value. Later write-capable milestones must define their own authorization contract in a separate spec instead of extending A2 authorization opportunistically.

### Rationale Codes

- `NONE`
- `FALSE_POSITIVE`
- `FILE_MISSING`
- `PATH_PROBE_INCONCLUSIVE`
- `TAG_METADATA_MISSING_TITLE`
- `TAG_METADATA_MISSING_ARTIST`
- `TAG_METADATA_MISSING_ALBUM`
- `TAG_METADATA_MISSING_MUSICBRAINZ_ID`
- `TAG_READER_DURATION_ZERO`
- `TAG_READER_READ_FAILED`
- `NO_PROBE_EVIDENCE`
- `PROBE_SUCCEEDED`
- `PROBE_FAILED`
- `EVIDENCE_CONFLICT`
- `DECODE_FATAL`
- `DECODE_MATERIALLY_SHORT`
- `STALE_FILE_IDENTITY`
- `MALFORMED_FINDING_RECORD`
- `UNKNOWN_FINDING_LABEL`

## Initial Rule Table

`HealerTriageAdvisor` is pure and deterministic. It consumes a sanitized `LibraryHealerFinding` plus current freshness flags and emits a `HealerTreatmentPlan`. It does not perform IO, call Lidarr services, call AI providers, or inspect paths outside the existing redacted identity fields.

Before applying the label-specific rule table, the advisor runs a freshness gate:

- if `evidenceFreshness` is not `current`, return `review`, high risk, no workflow-specific candidate, `executionAuthorization.authorized=false`, `EVIDENCE_FRESHNESS_NOT_CURRENT`, required evidence `FRESH_FILE_FINGERPRINT`, and required policy gates `NONE`;
- if `identityFreshness` is not `current`, return `review`, high risk, no workflow-specific candidate, `executionAuthorization.authorized=false`, `IDENTITY_FRESHNESS_NOT_CURRENT`, required evidence `FRESH_PATH_IDENTITY`, and required policy gates `NONE`;
- if the finding record is malformed, return `review`, high risk, no workflow-specific candidate, `executionAuthorization.authorized=false`, `MALFORMED_FINDING_RECORD`, required evidence `NONE`, and required policy gates `NONE`;
- if the label is unknown, return `review`, high risk, no workflow-specific candidate, `executionAuthorization.authorized=false`, `UNKNOWN_FINDING_LABEL`, required evidence `NONE`, and required policy gates `NONE`.

| Finding label | Candidate workflow | Confidence | Risk | Execution authorization | Blocked reasons | Required evidence | Required policy gates |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `FalsePositive` | `none` | `1.00` | `none` | unauthorized | `NONE` | `NONE` | `NONE` |
| `PathInconsistency` with confirmed missing file | `review` | `0.70` | `high` | unauthorized | `HUMAN_REVIEW_REQUIRED`, `PATH_STATE_STALE_OR_MISSING` | `FRESH_PATH_IDENTITY` | `NONE` |
| `PathInconsistency` with inconclusive probe | `review` | `0.45` | `high` | unauthorized | `PATH_PROBE_INCONCLUSIVE`, `HUMAN_REVIEW_REQUIRED` | `FRESH_PATH_IDENTITY` | `NONE` |
| `TagMetadataIssue` | `tagRepairCandidate` | `0.65` | `medium` | unauthorized | `CANONICAL_METADATA_VALIDATION_MISSING`, `TAG_WRITE_BACKUP_POLICY_MISSING`, `TAG_REPAIR_NOT_IMPLEMENTED` | `CANONICAL_METADATA_VALIDATION`, `MUSICBRAINZ_RELEASE_VALIDATION` | `BACKUP_POLICY_APPROVED` |
| `TagReaderSymptom` without probe evidence | `repairDryRunCandidate` | `0.55` | `medium` | unauthorized | `PROBE_EVIDENCE_MISSING`, `FULL_DECODE_EVIDENCE_MISSING`, `BACKUP_POLICY_MISSING`, `JOURNAL_POLICY_MISSING`, `REPAIR_DRY_RUN_NOT_IMPLEMENTED` | `FFPROBE_STREAM_EVIDENCE`, `FULL_DECODE_CLEAN`, `TAGLIB_REREAD_AFTER_REWRAP` | `BACKUP_POLICY_APPROVED`, `JOURNAL_POLICY_APPROVED` |
| `ProbeEvidence` with probe success but no decode | `repairDryRunCandidate` | `0.70` | `medium` | unauthorized | `FULL_DECODE_EVIDENCE_MISSING`, `TAGLIB_REREAD_AFTER_REWRAP_MISSING`, `BACKUP_POLICY_MISSING`, `JOURNAL_POLICY_MISSING`, `REPAIR_DRY_RUN_NOT_IMPLEMENTED` | `FULL_DECODE_CLEAN`, `TAGLIB_REREAD_AFTER_REWRAP` | `BACKUP_POLICY_APPROVED`, `JOURNAL_POLICY_APPROVED` |
| `ProbeEvidence` with conflicting probe/tag evidence | `review` | `0.40` | `high` | unauthorized | `EVIDENCE_CONFLICT`, `HUMAN_REVIEW_REQUIRED` | `FRESH_FILE_FINGERPRINT`, `FFPROBE_STREAM_EVIDENCE` | `NONE` |
| Future `GenuinelyBad` evidence | `reacquireCandidate` | `0.80` | `high` | unauthorized | `RECYCLE_BIN_POLICY_MISSING`, `ALBUM_WIDE_SCOPE_NOT_DISCLOSED`, `LIDARR_SEARCH_DRY_RUN_NOT_IMPLEMENTED` | `FULL_DECODE_FATAL`, `LIDARR_SEARCH_DRY_RUN_RESULT` | `RECYCLE_BIN_CONFIGURED`, `ALBUM_WIDE_SCOPE_ACCEPTED`, `EXPLICIT_OPERATOR_OPT_IN` |
| `NeedsHumanReview` | `review` | `0.25` | `high` | unauthorized | `HUMAN_REVIEW_REQUIRED` | `NONE` | `NONE` |

Confidence values are intentionally conservative. They help sort review queues; they do not grant permission to execute actions.

## Safety Invariants

- A2 treatment plans are advisory only.
- A2 action output must never include a media write instruction, destination path, shell command, or Lidarr command name.
- `executionAuthorization.authorized` must be `false` for every A2 treatment plan.
- `safetyLevel` must be `readOnly` for every A2 plan.
- AI is not used in A2.
- Future AI output may enrich rationale or priority only after deterministic candidate construction. It cannot set execution authorization.
- Blocked reasons, required evidence, required policy gates, and rationale codes use fixed vocabularies. Unknown stored values are dropped or normalized to a safe review-only value.
- Plans are computed at projection time from sanitized finding evidence, not trusted from old persisted records.
- Stale, corrupt, or hand-edited finding records must fail closed to `review`, `high` risk, and `executionAuthorization.authorized=false`.
- Plans must not reveal raw paths, artist names, album names, track names, MusicBrainz IDs, or tag-reader exception text.

## Testing Strategy

A2 development starts with tests.

Required RED tests before implementation:

- `HealerTriageAdvisorTests`:
  - `Advise_ShouldReturnNone_ForFalsePositive`
  - `Advise_ShouldReturnTagRepairCandidate_ForTagMetadataIssue`
  - `Advise_ShouldReturnRepairDryRunCandidate_ForTagReaderSymptomWithoutProbe`
  - `Advise_ShouldReturnReview_ForNeedsHumanReview`
- `HealerTriageAdvisorAuthorizationTests`:
  - `Advise_ShouldNeverAuthorizeExecution_InA2`
  - `Advise_ShouldUseOnlyNoneAuthorizationAuthority`
  - `Advise_ShouldUseOnlyA2ReadOnlyAuthorizationReason`
  - `Advise_ShouldKeepSafetyLevelReadOnly_ForEveryWorkflow`
- `HealerTriageFreshnessTests`:
  - `Advise_ShouldReturnReview_WhenEvidenceFreshnessIsStale`
  - `Advise_ShouldReturnReview_WhenIdentityFreshnessIsStale`
  - `Advise_ShouldReturnReview_WhenSizeMtimeOrPathHashChanged`
  - `Advise_ShouldReturnReview_WhenPersistedFindingIsMalformed`
- `LibraryHealerActionHandlerTriageProjectionTests`:
  - `HandleGetFindings_ShouldIncludeTreatmentPlan_ForEveryFinding`
  - `HandleGetFindings_ShouldComputePlansFromSanitizedFindings`
- `LibraryHealerActionHandlerTriageSummaryTests`:
  - `HandleGetFindings_ShouldReturnWorkflowRiskAndAuthorizationSummary`
  - `HandleGetFindings_ShouldCountBlockedReasonsExhaustively`
  - `HandleGetFindings_ShouldExposeHighRiskReacquireCandidatesInSummary`
- `HealerTriagePrivacyTests`:
  - `Advise_ShouldNotEchoRawPathTagValuesOrMusicBrainzIds`
  - `HandleGetFindings_ShouldRedactMetadataBearingTreatmentText`
- `HealerTriageVocabularyTests`:
  - `Advise_ShouldDropUnknownBlockedReasonsAndRequiredEvidence`
  - `Advise_ShouldDropUnknownPolicyGatesAndRationaleCodes`
  - `Advise_ShouldReturnReview_ForUnknownFindingLabel`
- Architecture tests preserving the A1 read-only boundary and adding no AI/provider references.

Expected focused verification gates:

- focused healer tests: `dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~HealerTriageAdvisorTests|FullyQualifiedName~HealerTriageAdvisorAuthorizationTests|FullyQualifiedName~HealerTriageFreshnessTests|FullyQualifiedName~HealerTriagePrivacyTests|FullyQualifiedName~HealerTriageVocabularyTests|FullyQualifiedName~LibraryHealerActionHandlerTriageProjectionTests|FullyQualifiedName~LibraryHealerActionHandlerTriageSummaryTests"`
- architecture guardrails: `dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryHealerReadOnlyArchitectureTests"`
- full local gate before merge: `dotnet test Brainarr.sln -c Release --no-build`

## Adversarial Review Prompt

Use this review prompt before implementation and again before merge:

> Review the Library Doctor A2 triage/evidence-plan design and implementation. A2 must remain read-only over media files and Lidarr state. Challenge whether a diagnostic label or `candidateWorkflow` can accidentally become execution authorization, whether stale or malformed stored findings can produce an authorized or workflow-specific plan, whether path/tag/MBID data can leak through rationale or blocked reasons, whether the summary can hide high-risk cases, whether future AI integration could bypass deterministic gates, and whether the action-plan contract is stable enough for A3 repair, A4 reacquire, and later cross-arr adapters. Return Critical, Important, and Minor findings with concrete file and test references.

## Implementation Sequence After Spec Approval

1. Contract and failing tests
   - Add enums/records for treatment plans.
   - Add pure advisor tests first.

2. Pure advisor
   - Implement deterministic rule table.
   - Keep all IO out of the advisor.

3. Action projection and summary
   - Attach treatment plans in `healer/getfindings`.
   - Add summary counts from projected plans.

4. Hardening
   - Add stale/corrupt/privacy tests.
   - Extend architecture tests to prove no mutation, command queue, or provider references.

5. Documentation and review
   - Update `docs/library-healer.md`.
   - Run focused and full verification.
   - Run adversarial code review and fix blockers with regression tests.

## Roadmap Impact

A2 updates the broader Library Doctor roadmap by making the treatment-plan contract the handoff between read-only diagnosis and every later write-capable milestone:

1. A2 owns candidate workflow selection, freshness gates, fixed vocabularies, and read-only summaries.
2. A2 never owns execution authorization. It always returns `authorized=false`, `authority=none`, and `reason=A2_READ_ONLY`.
3. A3 repair dry-run must consume A2 candidates but define its own dry-run verification contract before producing temp outputs.
4. A3 repair-in-place must define a separate write authorization, preflight, journal, backup, rollback, and Lidarr confirmation contract.
5. A4 reacquire must consume only high-risk unrecoverable candidates and define separate recycle-bin, album-wide disclosure, and Lidarr-search dry-run gates.
6. Import Brain and Tagging Brain may reuse A2's evidence and review vocabulary, but AI output cannot set execution authorization or bypass freshness and policy gates.

## Approval Gate

This spec is the stop point before implementation. Implementation should begin only after the design is reviewed and the A2 treatment-plan contract is accepted.

## Adversarial Review Resolution

The first adversarial design review found no A2 read-only violation, but blocked implementation until the contract stopped looking like action authorization. This revision resolves the blocking findings by:

- replacing `recommendedAction` with `candidateWorkflow`;
- replacing the boolean `actionable` with an explicit `executionAuthorization` object that is unauthorized for every A2 plan;
- adding evidence and identity freshness gates before label-specific triage;
- adding complete fixed vocabularies for blocked reasons, required evidence, required policy gates, and rationale codes;
- fixing `executionAuthorization.authority` and `executionAuthorization.reason` to constant A2 values;
- making blocked-reason summary counts exhaustive for all non-zero reasons;
- adding workflow-by-risk summary counts;
- aligning milestone names with the parent roadmap;
- naming the required TDD test classes for contract, authorization, freshness, privacy, vocabulary, projection, and summary behavior.
- splitting the rule table into blocked reasons, required evidence, and required policy gates.
- making the summary example exhaustive for all non-zero blocked reasons.
