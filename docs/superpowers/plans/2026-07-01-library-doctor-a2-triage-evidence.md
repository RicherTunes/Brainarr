# Library Doctor A2 Triage Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the A2 Library Doctor treatment-plan contract, deterministic triage advisor, and `healer/getfindings` treatment-plan summaries while keeping Brainarr read-only over media files and Lidarr state.

**Architecture:** Add focused treatment-plan DTOs and a pure `HealerTriageAdvisor` under `Brainarr.Plugin/Services/Healing`. `LibraryHealerActionHandler` projects sanitized findings through the advisor and computes summaries from the same projected plans. No scan, filesystem, Lidarr command queue, repair, reacquire, tag-write, or AI behavior is added.

**Tech Stack:** C# 12, .NET 8, xUnit, FluentAssertions, Brainarr.Plugin, Brainarr.Tests, existing Lidarr assemblies from `C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0`.

## Global Constraints

- Follow TDD: no production code before a focused failing test is observed.
- A2 is read-only over media files and Lidarr state.
- A2 must not repair, remux, retag, search, delete, move, replace, rescan, import, run `ffmpeg`, or call AI providers.
- `executionAuthorization.authorized` is always `false` in A2.
- `executionAuthorization.authority` is always `none` in A2.
- `executionAuthorization.reason` is always `A2_READ_ONLY` in A2.
- Treatment plans use fixed vocabularies and must not echo raw paths, tag values, MusicBrainz IDs, provider prompts, or exception text.
- Stale, malformed, hand-edited, or unknown evidence fails closed to `review`, high risk, and no execution authorization.
- Summary blocked-reason counts are exhaustive for all non-zero blocked reasons in returned plans.
- Use `apply_patch` for manual edits.

---

## File Structure

- Create `Brainarr.Plugin/Services/Healing/HealerTreatmentPlan.cs`
  - Owns A2 treatment-plan records and fixed string vocabularies.
- Create `Brainarr.Plugin/Services/Healing/HealerTriageAdvisor.cs`
  - Pure deterministic advisor from sanitized `LibraryHealerFinding` plus freshness context to `HealerTreatmentPlan`.
- Create `Brainarr.Plugin/Services/Healing/HealerTriageSummary.cs`
  - Pure summary builder over projected treatment plans.
- Modify `Brainarr.Plugin/Services/Healing/LibraryHealerActionHandler.cs`
  - Build sanitized/projected findings once.
  - Attach `treatmentPlan` to each item returned from `healer/getfindings`.
  - Add summary counts computed from the same plans.
- Add tests:
  - `Brainarr.Tests/Services/Healing/HealerTriageAdvisorTests.cs`
  - `Brainarr.Tests/Services/Healing/HealerTriageAdvisorAuthorizationTests.cs`
  - `Brainarr.Tests/Services/Healing/HealerTriageFreshnessTests.cs`
  - `Brainarr.Tests/Services/Healing/HealerTriageVocabularyTests.cs`
  - `Brainarr.Tests/Services/Healing/HealerTriageSummaryTests.cs`
  - Extend `Brainarr.Tests/Services/Core/BrainarrOrchestratorHealerActionsTests.cs`
  - Extend `Brainarr.Tests/Services/Healing/LibraryHealerReadOnlyArchitectureTests.cs`
- Modify docs:
  - `docs/library-healer.md`
  - `docs/superpowers/specs/2026-07-01-library-doctor-a2-triage-evidence-design.md` only if implementation discovers a contract correction.

Use this Lidarr path in commands unless the local environment changes:

```powershell
$lidarrPath = "C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0"
```

---

### Task 1: Treatment Plan Contract And First Advisor Rules

**Files:**
- Create: `Brainarr.Tests/Services/Healing/HealerTriageAdvisorTests.cs`
- Create: `Brainarr.Plugin/Services/Healing/HealerTreatmentPlan.cs`
- Create: `Brainarr.Plugin/Services/Healing/HealerTriageAdvisor.cs`

**Interfaces:**
- Produces:
  - `HealerTreatmentPlan`
  - `HealerExecutionAuthorization`
  - `HealerFindingFreshness`
  - `HealerTreatmentVocab`
  - `HealerTriageAdvisor.Advise(LibraryHealerFinding finding, HealerFindingFreshness? freshness = null)`
- Consumes:
  - existing `LibraryHealerFinding`, `LibraryHealerLabel`, `TagReaderEvidence`, `ProbeEvidence`

- [ ] **Step 1: Write the failing advisor rule tests**

Create `Brainarr.Tests/Services/Healing/HealerTriageAdvisorTests.cs`:

```csharp
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public sealed class HealerTriageAdvisorTests
{
    [Fact]
    public void Advise_ShouldReturnNone_ForFalsePositive()
    {
        var plan = HealerTriageAdvisor.Advise(Finding(
            LibraryHealerLabel.FalsePositive,
            new[] { "TAG_READER_DURATION_POSITIVE" },
            new TagReaderEvidence(true, true, 245.2, null, null),
            null));

        plan.SchemaVersion.Should().Be(1);
        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.None);
        plan.Confidence.Should().Be(1.0);
        plan.Risk.Should().Be(HealerTreatmentVocab.Risk.None);
        plan.BlockedReasons.Should().Equal(HealerTreatmentVocab.BlockedReason.None);
        plan.RequiredEvidence.Should().Equal(HealerTreatmentVocab.RequiredEvidence.None);
        plan.RequiredPolicyGates.Should().Equal(HealerTreatmentVocab.RequiredPolicyGate.None);
    }

    [Fact]
    public void Advise_ShouldReturnTagRepairCandidate_ForTagMetadataIssue()
    {
        var plan = HealerTriageAdvisor.Advise(Finding(
            LibraryHealerLabel.TagMetadataIssue,
            new[] { "TAG_METADATA_MISSING", "TAG_MISSING_TITLE", "TAG_MISSING_MUSICBRAINZID" },
            new TagReaderEvidence(
                true,
                true,
                245.2,
                null,
                null,
                new TagMetadataEvidence(false, true, true, false, new[] { "title", "musicBrainzId" })),
            null));

        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.TagRepairCandidate);
        plan.Confidence.Should().Be(0.65);
        plan.Risk.Should().Be(HealerTreatmentVocab.Risk.Medium);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.CanonicalMetadataValidationMissing);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.TagWriteBackupPolicyMissing);
        plan.RequiredEvidence.Should().Contain(HealerTreatmentVocab.RequiredEvidence.CanonicalMetadataValidation);
        plan.RequiredEvidence.Should().Contain(HealerTreatmentVocab.RequiredEvidence.MusicBrainzReleaseValidation);
        plan.RequiredPolicyGates.Should().Contain(HealerTreatmentVocab.RequiredPolicyGate.BackupPolicyApproved);
    }

    [Fact]
    public void Advise_ShouldReturnRepairDryRunCandidate_ForTagReaderSymptomWithoutProbe()
    {
        var plan = HealerTriageAdvisor.Advise(Finding(
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_ZERO_DURATION" },
            new TagReaderEvidence(true, true, 0, null, null),
            null));

        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.RepairDryRunCandidate);
        plan.Confidence.Should().Be(0.55);
        plan.Risk.Should().Be(HealerTreatmentVocab.Risk.Medium);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.ProbeEvidenceMissing);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.FullDecodeEvidenceMissing);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented);
        plan.RequiredEvidence.Should().Contain(HealerTreatmentVocab.RequiredEvidence.FfprobeStreamEvidence);
        plan.RequiredPolicyGates.Should().Contain(HealerTreatmentVocab.RequiredPolicyGate.JournalPolicyApproved);
        plan.RationaleCodes.Should().Contain(HealerTreatmentVocab.Rationale.TagReaderDurationZero);
    }

    [Fact]
    public void Advise_ShouldReturnReview_ForNeedsHumanReview()
    {
        var plan = HealerTriageAdvisor.Advise(Finding(
            LibraryHealerLabel.NeedsHumanReview,
            new[] { "PATH_PROBE_INCONCLUSIVE", "PATH_ACCESS_DENIED" },
            new TagReaderEvidence(false, false, null, null, null),
            null));

        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.Confidence.Should().Be(0.25);
        plan.Risk.Should().Be(HealerTreatmentVocab.Risk.High);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.HumanReviewRequired);
    }

    internal static LibraryHealerFinding Finding(
        LibraryHealerLabel label,
        IReadOnlyList<string> reasons,
        TagReaderEvidence tagReader,
        ProbeEvidence? probe)
    {
        return new LibraryHealerFinding(
            "triage-test",
            new LibraryHealerFileIdentity(
                1,
                2,
                3,
                "track01.flac#abcdef123456",
                "abcdef123456",
                123,
                new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
            label,
            reasons,
            tagReader,
            probe,
            new DateTime(2026, 7, 1, 1, 2, 3, DateTimeKind.Utc));
    }
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~HealerTriageAdvisorTests" -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
```

Expected: FAIL at compile time because `HealerTriageAdvisor`, `HealerTreatmentVocab`, and `HealerTreatmentPlan` do not exist.

- [ ] **Step 3: Add the minimum treatment-plan contract**

Create `Brainarr.Plugin/Services/Healing/HealerTreatmentPlan.cs`:

```csharp
namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record HealerExecutionAuthorization(
    bool Authorized,
    string Authority,
    string Reason);

public sealed record HealerFindingFreshness(
    string EvidenceFreshness,
    string IdentityFreshness,
    bool MalformedRecord = false)
{
    public static HealerFindingFreshness Current { get; } = new(
        HealerTreatmentVocab.Freshness.Current,
        HealerTreatmentVocab.Freshness.Current);
}

public sealed record HealerTreatmentPlan(
    int SchemaVersion,
    string CandidateWorkflow,
    double Confidence,
    string Risk,
    string SafetyLevel,
    string EvidenceFreshness,
    string IdentityFreshness,
    HealerExecutionAuthorization ExecutionAuthorization,
    IReadOnlyList<string> BlockedReasons,
    IReadOnlyList<string> RequiredEvidence,
    IReadOnlyList<string> RequiredPolicyGates,
    IReadOnlyList<string> RationaleCodes);

public static class HealerTreatmentVocab
{
    public const int SchemaVersion = 1;

    public static class Workflow
    {
        public const string None = "none";
        public const string Review = "review";
        public const string RepairDryRunCandidate = "repairDryRunCandidate";
        public const string TagRepairCandidate = "tagRepairCandidate";
        public const string ReacquireCandidate = "reacquireCandidate";
        public const string ReleaseReviewCandidate = "releaseReviewCandidate";
    }

    public static class Risk
    {
        public const string None = "none";
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";
        public const string Critical = "critical";
    }

    public static class SafetyLevel
    {
        public const string ReadOnly = "readOnly";
    }

    public static class Freshness
    {
        public const string Current = "current";
        public const string Stale = "stale";
        public const string Missing = "missing";
        public const string Unknown = "unknown";
    }

    public static class AuthorizationAuthority
    {
        public const string None = "none";
    }

    public static class AuthorizationReason
    {
        public const string A2ReadOnly = "A2_READ_ONLY";
    }

    public static class BlockedReason
    {
        public const string None = "NONE";
        public const string HumanReviewRequired = "HUMAN_REVIEW_REQUIRED";
        public const string EvidenceFreshnessNotCurrent = "EVIDENCE_FRESHNESS_NOT_CURRENT";
        public const string IdentityFreshnessNotCurrent = "IDENTITY_FRESHNESS_NOT_CURRENT";
        public const string PathStateStaleOrMissing = "PATH_STATE_STALE_OR_MISSING";
        public const string PathProbeInconclusive = "PATH_PROBE_INCONCLUSIVE";
        public const string ProbeEvidenceMissing = "PROBE_EVIDENCE_MISSING";
        public const string FullDecodeEvidenceMissing = "FULL_DECODE_EVIDENCE_MISSING";
        public const string TaglibRereadAfterRewrapMissing = "TAGLIB_REREAD_AFTER_REWRAP_MISSING";
        public const string EvidenceConflict = "EVIDENCE_CONFLICT";
        public const string BackupPolicyMissing = "BACKUP_POLICY_MISSING";
        public const string JournalPolicyMissing = "JOURNAL_POLICY_MISSING";
        public const string CanonicalMetadataValidationMissing = "CANONICAL_METADATA_VALIDATION_MISSING";
        public const string TagWriteBackupPolicyMissing = "TAG_WRITE_BACKUP_POLICY_MISSING";
        public const string TagRepairNotImplemented = "TAG_REPAIR_NOT_IMPLEMENTED";
        public const string RepairDryRunNotImplemented = "REPAIR_DRY_RUN_NOT_IMPLEMENTED";
        public const string RecycleBinPolicyMissing = "RECYCLE_BIN_POLICY_MISSING";
        public const string AlbumWideScopeNotDisclosed = "ALBUM_WIDE_SCOPE_NOT_DISCLOSED";
        public const string LidarrSearchDryRunNotImplemented = "LIDARR_SEARCH_DRY_RUN_NOT_IMPLEMENTED";
        public const string MalformedFindingRecord = "MALFORMED_FINDING_RECORD";
        public const string UnknownFindingLabel = "UNKNOWN_FINDING_LABEL";
    }

    public static class RequiredEvidence
    {
        public const string None = "NONE";
        public const string FreshFileFingerprint = "FRESH_FILE_FINGERPRINT";
        public const string FreshPathIdentity = "FRESH_PATH_IDENTITY";
        public const string FfprobeStreamEvidence = "FFPROBE_STREAM_EVIDENCE";
        public const string FullDecodeClean = "FULL_DECODE_CLEAN";
        public const string FullDecodeFatal = "FULL_DECODE_FATAL";
        public const string TaglibRereadAfterRewrap = "TAGLIB_REREAD_AFTER_REWRAP";
        public const string CanonicalMetadataValidation = "CANONICAL_METADATA_VALIDATION";
        public const string MusicBrainzReleaseValidation = "MUSICBRAINZ_RELEASE_VALIDATION";
        public const string LidarrSearchDryRunResult = "LIDARR_SEARCH_DRY_RUN_RESULT";
    }

    public static class RequiredPolicyGate
    {
        public const string None = "NONE";
        public const string BackupPolicyApproved = "BACKUP_POLICY_APPROVED";
        public const string JournalPolicyApproved = "JOURNAL_POLICY_APPROVED";
        public const string RecycleBinConfigured = "RECYCLE_BIN_CONFIGURED";
        public const string AlbumWideScopeAccepted = "ALBUM_WIDE_SCOPE_ACCEPTED";
        public const string ExplicitOperatorOptIn = "EXPLICIT_OPERATOR_OPT_IN";
    }

    public static class Rationale
    {
        public const string None = "NONE";
        public const string FalsePositive = "FALSE_POSITIVE";
        public const string FileMissing = "FILE_MISSING";
        public const string PathProbeInconclusive = "PATH_PROBE_INCONCLUSIVE";
        public const string TagMetadataMissingTitle = "TAG_METADATA_MISSING_TITLE";
        public const string TagMetadataMissingArtist = "TAG_METADATA_MISSING_ARTIST";
        public const string TagMetadataMissingAlbum = "TAG_METADATA_MISSING_ALBUM";
        public const string TagMetadataMissingMusicBrainzId = "TAG_METADATA_MISSING_MUSICBRAINZ_ID";
        public const string TagReaderDurationZero = "TAG_READER_DURATION_ZERO";
        public const string TagReaderReadFailed = "TAG_READER_READ_FAILED";
        public const string NoProbeEvidence = "NO_PROBE_EVIDENCE";
        public const string ProbeSucceeded = "PROBE_SUCCEEDED";
        public const string ProbeFailed = "PROBE_FAILED";
        public const string EvidenceConflict = "EVIDENCE_CONFLICT";
        public const string MalformedFindingRecord = "MALFORMED_FINDING_RECORD";
        public const string UnknownFindingLabel = "UNKNOWN_FINDING_LABEL";
    }
}
```

- [ ] **Step 4: Add the minimum advisor implementation**

Create `Brainarr.Plugin/Services/Healing/HealerTriageAdvisor.cs`:

```csharp
namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public static class HealerTriageAdvisor
{
    public static HealerTreatmentPlan Advise(
        LibraryHealerFinding finding,
        HealerFindingFreshness? freshness = null)
    {
        var state = freshness ?? HealerFindingFreshness.Current;
        var label = LibraryHealerReasonCodes.NormalizeLabel(finding.Label, finding.TagReader.Metadata);
        return label switch
        {
            LibraryHealerLabel.FalsePositive => Plan(
                HealerTreatmentVocab.Workflow.None,
                1.00,
                HealerTreatmentVocab.Risk.None,
                state,
                new[] { HealerTreatmentVocab.BlockedReason.None },
                new[] { HealerTreatmentVocab.RequiredEvidence.None },
                new[] { HealerTreatmentVocab.RequiredPolicyGate.None },
                new[] { HealerTreatmentVocab.Rationale.FalsePositive }),

            LibraryHealerLabel.TagMetadataIssue => Plan(
                HealerTreatmentVocab.Workflow.TagRepairCandidate,
                0.65,
                HealerTreatmentVocab.Risk.Medium,
                state,
                new[]
                {
                    HealerTreatmentVocab.BlockedReason.CanonicalMetadataValidationMissing,
                    HealerTreatmentVocab.BlockedReason.TagWriteBackupPolicyMissing,
                    HealerTreatmentVocab.BlockedReason.TagRepairNotImplemented,
                },
                new[]
                {
                    HealerTreatmentVocab.RequiredEvidence.CanonicalMetadataValidation,
                    HealerTreatmentVocab.RequiredEvidence.MusicBrainzReleaseValidation,
                },
                new[] { HealerTreatmentVocab.RequiredPolicyGate.BackupPolicyApproved },
                Rationale(finding)),

            LibraryHealerLabel.TagReaderSymptom => Plan(
                HealerTreatmentVocab.Workflow.RepairDryRunCandidate,
                0.55,
                HealerTreatmentVocab.Risk.Medium,
                state,
                new[]
                {
                    HealerTreatmentVocab.BlockedReason.ProbeEvidenceMissing,
                    HealerTreatmentVocab.BlockedReason.FullDecodeEvidenceMissing,
                    HealerTreatmentVocab.BlockedReason.BackupPolicyMissing,
                    HealerTreatmentVocab.BlockedReason.JournalPolicyMissing,
                    HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented,
                },
                new[]
                {
                    HealerTreatmentVocab.RequiredEvidence.FfprobeStreamEvidence,
                    HealerTreatmentVocab.RequiredEvidence.FullDecodeClean,
                    HealerTreatmentVocab.RequiredEvidence.TaglibRereadAfterRewrap,
                },
                new[]
                {
                    HealerTreatmentVocab.RequiredPolicyGate.BackupPolicyApproved,
                    HealerTreatmentVocab.RequiredPolicyGate.JournalPolicyApproved,
                },
                Rationale(finding)),

            LibraryHealerLabel.ProbeEvidence when finding.Probe?.ProbeSucceeded == true => Plan(
                HealerTreatmentVocab.Workflow.RepairDryRunCandidate,
                0.70,
                HealerTreatmentVocab.Risk.Medium,
                state,
                new[]
                {
                    HealerTreatmentVocab.BlockedReason.FullDecodeEvidenceMissing,
                    HealerTreatmentVocab.BlockedReason.TaglibRereadAfterRewrapMissing,
                    HealerTreatmentVocab.BlockedReason.BackupPolicyMissing,
                    HealerTreatmentVocab.BlockedReason.JournalPolicyMissing,
                    HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented,
                },
                new[]
                {
                    HealerTreatmentVocab.RequiredEvidence.FullDecodeClean,
                    HealerTreatmentVocab.RequiredEvidence.TaglibRereadAfterRewrap,
                },
                new[]
                {
                    HealerTreatmentVocab.RequiredPolicyGate.BackupPolicyApproved,
                    HealerTreatmentVocab.RequiredPolicyGate.JournalPolicyApproved,
                },
                Rationale(finding)),

            LibraryHealerLabel.NeedsHumanReview => Review(
                0.25,
                state,
                new[] { HealerTreatmentVocab.BlockedReason.HumanReviewRequired },
                new[] { HealerTreatmentVocab.RequiredEvidence.None },
                Rationale(finding)),

            LibraryHealerLabel.PathInconsistency => Review(
                0.70,
                state,
                new[]
                {
                    HealerTreatmentVocab.BlockedReason.HumanReviewRequired,
                    HealerTreatmentVocab.BlockedReason.PathStateStaleOrMissing,
                },
                new[] { HealerTreatmentVocab.RequiredEvidence.FreshPathIdentity },
                Rationale(finding)),

            _ => Review(
                0.40,
                state,
                new[]
                {
                    HealerTreatmentVocab.BlockedReason.EvidenceConflict,
                    HealerTreatmentVocab.BlockedReason.HumanReviewRequired,
                },
                new[] { HealerTreatmentVocab.RequiredEvidence.FreshFileFingerprint },
                Rationale(finding)),
        };
    }

    private static HealerTreatmentPlan Review(
        double confidence,
        HealerFindingFreshness freshness,
        IReadOnlyList<string> blockedReasons,
        IReadOnlyList<string> requiredEvidence,
        IReadOnlyList<string> rationale)
    {
        return Plan(
            HealerTreatmentVocab.Workflow.Review,
            confidence,
            HealerTreatmentVocab.Risk.High,
            freshness,
            blockedReasons,
            requiredEvidence,
            new[] { HealerTreatmentVocab.RequiredPolicyGate.None },
            rationale);
    }

    private static HealerTreatmentPlan Plan(
        string workflow,
        double confidence,
        string risk,
        HealerFindingFreshness freshness,
        IReadOnlyList<string> blockedReasons,
        IReadOnlyList<string> requiredEvidence,
        IReadOnlyList<string> requiredPolicyGates,
        IReadOnlyList<string> rationale)
    {
        return new HealerTreatmentPlan(
            HealerTreatmentVocab.SchemaVersion,
            workflow,
            confidence,
            risk,
            HealerTreatmentVocab.SafetyLevel.ReadOnly,
            freshness.EvidenceFreshness,
            freshness.IdentityFreshness,
            new HealerExecutionAuthorization(false, HealerTreatmentVocab.AuthorizationAuthority.None, HealerTreatmentVocab.AuthorizationReason.A2ReadOnly),
            DistinctOrNone(blockedReasons, HealerTreatmentVocab.BlockedReason.None),
            DistinctOrNone(requiredEvidence, HealerTreatmentVocab.RequiredEvidence.None),
            DistinctOrNone(requiredPolicyGates, HealerTreatmentVocab.RequiredPolicyGate.None),
            DistinctOrNone(rationale, HealerTreatmentVocab.Rationale.None));
    }

    private static IReadOnlyList<string> Rationale(LibraryHealerFinding? finding)
    {
        if (finding is null)
        {
            return new[] { HealerTreatmentVocab.Rationale.MalformedFindingRecord };
        }

        var result = new List<string>();
        foreach (var reason in LibraryHealerReasonCodes.Normalize(finding.InternalReasonCodes, finding.TagReader.Metadata))
        {
            switch (reason)
            {
                case "FILE_MISSING":
                    result.Add(HealerTreatmentVocab.Rationale.FileMissing);
                    break;
                case "PATH_PROBE_INCONCLUSIVE":
                    result.Add(HealerTreatmentVocab.Rationale.PathProbeInconclusive);
                    break;
                case "TAG_MISSING_TITLE":
                    result.Add(HealerTreatmentVocab.Rationale.TagMetadataMissingTitle);
                    break;
                case "TAG_MISSING_ARTIST":
                    result.Add(HealerTreatmentVocab.Rationale.TagMetadataMissingArtist);
                    break;
                case "TAG_MISSING_ALBUM":
                    result.Add(HealerTreatmentVocab.Rationale.TagMetadataMissingAlbum);
                    break;
                case "TAG_MISSING_MUSICBRAINZID":
                    result.Add(HealerTreatmentVocab.Rationale.TagMetadataMissingMusicBrainzId);
                    break;
                case "TAG_READER_ZERO_DURATION":
                    result.Add(HealerTreatmentVocab.Rationale.TagReaderDurationZero);
                    break;
                case "TAG_READER_FAILED":
                    result.Add(HealerTreatmentVocab.Rationale.TagReaderReadFailed);
                    break;
                case "PROBE_DURATION_POSITIVE":
                    result.Add(HealerTreatmentVocab.Rationale.ProbeSucceeded);
                    break;
                case "PROBE_FAILED":
                    result.Add(HealerTreatmentVocab.Rationale.ProbeFailed);
                    break;
            }
        }

        if (finding.Label == LibraryHealerLabel.TagReaderSymptom && finding.Probe is null)
        {
            result.Add(HealerTreatmentVocab.Rationale.NoProbeEvidence);
        }

        return DistinctOrNone(result, HealerTreatmentVocab.Rationale.None);
    }

    private static IReadOnlyList<string> DistinctOrNone(IReadOnlyList<string> values, string noneValue)
    {
        var clean = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return clean.Length == 0 ? new[] { noneValue } : clean;
    }
}
```

- [ ] **Step 5: Run the focused test and verify GREEN**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~HealerTriageAdvisorTests" -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
```

Expected: PASS for `HealerTriageAdvisorTests`.

- [ ] **Step 6: Commit Task 1**

```powershell
git add Brainarr.Plugin/Services/Healing/HealerTreatmentPlan.cs Brainarr.Plugin/Services/Healing/HealerTriageAdvisor.cs Brainarr.Tests/Services/Healing/HealerTriageAdvisorTests.cs
git commit -m "feat: add healer triage advisor contract"
```

---

### Task 2: Authorization, Freshness, Vocabulary, And Privacy Guardrails

**Files:**
- Create: `Brainarr.Tests/Services/Healing/HealerTriageAdvisorAuthorizationTests.cs`
- Create: `Brainarr.Tests/Services/Healing/HealerTriageFreshnessTests.cs`
- Create: `Brainarr.Tests/Services/Healing/HealerTriageVocabularyTests.cs`
- Modify: `Brainarr.Plugin/Services/Healing/HealerTriageAdvisor.cs`
- Modify: `Brainarr.Plugin/Services/Healing/HealerTreatmentPlan.cs`

**Interfaces:**
- Consumes: `HealerTriageAdvisor.Advise`
- Produces: stronger fail-closed behavior and fixed-vocabulary filtering

- [ ] **Step 1: Write failing authorization tests**

Create `Brainarr.Tests/Services/Healing/HealerTriageAdvisorAuthorizationTests.cs`:

```csharp
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public sealed class HealerTriageAdvisorAuthorizationTests
{
    [Theory]
    [InlineData(LibraryHealerLabel.FalsePositive)]
    [InlineData(LibraryHealerLabel.TagMetadataIssue)]
    [InlineData(LibraryHealerLabel.TagReaderSymptom)]
    [InlineData(LibraryHealerLabel.ProbeEvidence)]
    [InlineData(LibraryHealerLabel.PathInconsistency)]
    [InlineData(LibraryHealerLabel.NeedsHumanReview)]
    public void Advise_ShouldNeverAuthorizeExecution_InA2(LibraryHealerLabel label)
    {
        var plan = HealerTriageAdvisor.Advise(HealerTriageAdvisorTests.Finding(
            label,
            new[] { "TAG_READER_ZERO_DURATION", "PROBE_DURATION_POSITIVE" },
            new TagReaderEvidence(true, true, 0, null, null),
            new ProbeEvidence(true, true, 245.2, "mov,mp4,m4a", "flac", null, null)));

        plan.ExecutionAuthorization.Authorized.Should().BeFalse();
        plan.ExecutionAuthorization.Authority.Should().Be(HealerTreatmentVocab.AuthorizationAuthority.None);
        plan.ExecutionAuthorization.Reason.Should().Be(HealerTreatmentVocab.AuthorizationReason.A2ReadOnly);
        plan.SafetyLevel.Should().Be(HealerTreatmentVocab.SafetyLevel.ReadOnly);
    }
}
```

- [ ] **Step 2: Write failing freshness tests**

Create `Brainarr.Tests/Services/Healing/HealerTriageFreshnessTests.cs`:

```csharp
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public sealed class HealerTriageFreshnessTests
{
    [Fact]
    public void Advise_ShouldReturnReview_WhenEvidenceFreshnessIsStale()
    {
        var plan = HealerTriageAdvisor.Advise(
            HealerTriageAdvisorTests.Finding(
                LibraryHealerLabel.TagReaderSymptom,
                new[] { "TAG_READER_ZERO_DURATION" },
                new TagReaderEvidence(true, true, 0, null, null),
                null),
            new HealerFindingFreshness(HealerTreatmentVocab.Freshness.Stale, HealerTreatmentVocab.Freshness.Current));

        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.Risk.Should().Be(HealerTreatmentVocab.Risk.High);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.EvidenceFreshnessNotCurrent);
        plan.RequiredEvidence.Should().Contain(HealerTreatmentVocab.RequiredEvidence.FreshFileFingerprint);
        plan.ExecutionAuthorization.Authorized.Should().BeFalse();
    }

    [Fact]
    public void Advise_ShouldReturnReview_WhenIdentityFreshnessIsStale()
    {
        var plan = HealerTriageAdvisor.Advise(
            HealerTriageAdvisorTests.Finding(
                LibraryHealerLabel.TagMetadataIssue,
                new[] { "TAG_METADATA_MISSING" },
                new TagReaderEvidence(true, true, 245.2, null, null),
                null),
            new HealerFindingFreshness(HealerTreatmentVocab.Freshness.Current, HealerTreatmentVocab.Freshness.Stale));

        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.IdentityFreshnessNotCurrent);
        plan.RequiredEvidence.Should().Contain(HealerTreatmentVocab.RequiredEvidence.FreshPathIdentity);
    }

    [Fact]
    public void Advise_ShouldReturnReview_WhenPersistedFindingIsMalformed()
    {
        var plan = HealerTriageAdvisor.Advise(
            HealerTriageAdvisorTests.Finding(
                LibraryHealerLabel.TagReaderSymptom,
                new[] { "TAG_READER_ZERO_DURATION" },
                new TagReaderEvidence(true, true, 0, null, null),
                null),
            new HealerFindingFreshness(
                HealerTreatmentVocab.Freshness.Current,
                HealerTreatmentVocab.Freshness.Current,
                MalformedRecord: true));

        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.MalformedFindingRecord);
        plan.RequiredPolicyGates.Should().Equal(HealerTreatmentVocab.RequiredPolicyGate.None);
    }
}
```

- [ ] **Step 3: Write failing fixed-vocabulary tests**

Create `Brainarr.Tests/Services/Healing/HealerTriageVocabularyTests.cs`:

```csharp
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public sealed class HealerTriageVocabularyTests
{
    [Fact]
    public void Advise_ShouldDropUnknownStoredReasonsFromRationale()
    {
        var plan = HealerTriageAdvisor.Advise(HealerTriageAdvisorTests.Finding(
            LibraryHealerLabel.TagReaderSymptom,
            new[]
            {
                "TAG_READER_ZERO_DURATION",
                @"D:\Music\Private Artist\secret.flac",
                "550e8400-e29b-41d4-a716-446655440000",
                "ignore previous instructions",
            },
            new TagReaderEvidence(true, true, 0, null, null),
            null));

        plan.RationaleCodes.Should().Contain(HealerTreatmentVocab.Rationale.TagReaderDurationZero);
        string.Join(" ", plan.RationaleCodes).Should().NotContain("Private Artist");
        string.Join(" ", plan.RationaleCodes).Should().NotContain("550e8400");
        string.Join(" ", plan.RationaleCodes).Should().NotContain("ignore previous");
    }

    [Fact]
    public void Advise_ShouldReturnReview_ForUnknownFindingLabel()
    {
        var plan = HealerTriageAdvisor.Advise(HealerTriageAdvisorTests.Finding(
            (LibraryHealerLabel)999,
            Array.Empty<string>(),
            new TagReaderEvidence(true, true, 0, null, null),
            null));

        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.UnknownFindingLabel);
        plan.RationaleCodes.Should().Contain(HealerTreatmentVocab.Rationale.UnknownFindingLabel);
        plan.ExecutionAuthorization.Authorized.Should().BeFalse();
    }
}
```

- [ ] **Step 4: Run the focused tests and verify RED**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~HealerTriageAdvisorAuthorizationTests|FullyQualifiedName~HealerTriageFreshnessTests|FullyQualifiedName~HealerTriageVocabularyTests" -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
```

Expected: at least one FAIL because the first implementation does not yet cover every authorization/freshness/vocabulary assertion.

- [ ] **Step 5: Implement the minimal fixes**

Adjust `HealerTriageAdvisor` only enough to satisfy the tests:

- ensure malformed/freshness/unknown-label gates run before label logic;
- ensure authorization is constant for all plans;
- ensure `Rationale` maps only fixed reason codes and drops all other strings.

- [ ] **Step 6: Run focused tests and verify GREEN**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~HealerTriageAdvisorTests|FullyQualifiedName~HealerTriageAdvisorAuthorizationTests|FullyQualifiedName~HealerTriageFreshnessTests|FullyQualifiedName~HealerTriageVocabularyTests" -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
```

Expected: PASS for all four advisor test classes.

- [ ] **Step 7: Commit Task 2**

```powershell
git add Brainarr.Plugin/Services/Healing/HealerTriageAdvisor.cs Brainarr.Plugin/Services/Healing/HealerTreatmentPlan.cs Brainarr.Tests/Services/Healing/HealerTriageAdvisorAuthorizationTests.cs Brainarr.Tests/Services/Healing/HealerTriageFreshnessTests.cs Brainarr.Tests/Services/Healing/HealerTriageVocabularyTests.cs
git commit -m "test: harden healer triage authorization gates"
```

---

### Task 3: Action Projection And Summary

**Files:**
- Create: `Brainarr.Tests/Services/Healing/HealerTriageSummaryTests.cs`
- Modify: `Brainarr.Tests/Services/Core/BrainarrOrchestratorHealerActionsTests.cs`
- Create: `Brainarr.Plugin/Services/Healing/HealerTriageSummary.cs`
- Modify: `Brainarr.Plugin/Services/Healing/LibraryHealerActionHandler.cs`

**Interfaces:**
- Produces:
  - `HealerTriageSummary.Create(IReadOnlyList<HealerTreatmentPlan> plans)`
  - `healer/getfindings` response shape `{ items, summary }`
  - per-item `treatmentPlan`
- Consumes:
  - `HealerTriageAdvisor.Advise`

- [ ] **Step 1: Write failing summary tests**

Create `Brainarr.Tests/Services/Healing/HealerTriageSummaryTests.cs`:

```csharp
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public sealed class HealerTriageSummaryTests
{
    [Fact]
    public void Create_ShouldReturnWorkflowRiskAuthorizationAndBlockedReasonCounts()
    {
        var plans = new[]
        {
            HealerTriageAdvisor.Advise(HealerTriageAdvisorTests.Finding(
                LibraryHealerLabel.TagReaderSymptom,
                new[] { "TAG_READER_ZERO_DURATION" },
                new TagReaderEvidence(true, true, 0, null, null),
                null)),
            HealerTriageAdvisor.Advise(HealerTriageAdvisorTests.Finding(
                LibraryHealerLabel.TagMetadataIssue,
                new[] { "TAG_METADATA_MISSING", "TAG_MISSING_TITLE" },
                new TagReaderEvidence(true, true, 245.2, null, null, new TagMetadataEvidence(false, true, true, true, new[] { "title" })),
                null)),
            HealerTriageAdvisor.Advise(HealerTriageAdvisorTests.Finding(
                LibraryHealerLabel.NeedsHumanReview,
                new[] { "PATH_PROBE_INCONCLUSIVE" },
                new TagReaderEvidence(false, false, null, null, null),
                null)),
        };

        var summary = HealerTriageSummary.Create(plans);

        summary.Total.Should().Be(3);
        summary.ByWorkflow[HealerTreatmentVocab.Workflow.RepairDryRunCandidate].Should().Be(1);
        summary.ByWorkflow[HealerTreatmentVocab.Workflow.TagRepairCandidate].Should().Be(1);
        summary.ByRisk[HealerTreatmentVocab.Risk.Medium].Should().Be(2);
        summary.ByWorkflowByRisk[HealerTreatmentVocab.Workflow.RepairDryRunCandidate][HealerTreatmentVocab.Risk.Medium].Should().Be(1);
        summary.Authorization["authorized"].Should().Be(0);
        summary.Authorization["unauthorized"].Should().Be(3);
        summary.BlockedReasons[HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented].Should().Be(1);
        summary.BlockedReasons[HealerTreatmentVocab.BlockedReason.TagRepairNotImplemented].Should().Be(1);
        summary.BlockedReasons[HealerTreatmentVocab.BlockedReason.HumanReviewRequired].Should().Be(1);
    }
}
```

- [ ] **Step 2: Run summary test and verify RED**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~HealerTriageSummaryTests" -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
```

Expected: FAIL because `HealerTriageSummary` does not exist.

- [ ] **Step 3: Implement summary**

Create `Brainarr.Plugin/Services/Healing/HealerTriageSummary.cs`:

```csharp
namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record HealerTriageSummary(
    int Total,
    IReadOnlyDictionary<string, int> ByWorkflow,
    IReadOnlyDictionary<string, int> ByRisk,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ByWorkflowByRisk,
    IReadOnlyDictionary<string, int> Authorization,
    IReadOnlyDictionary<string, int> BlockedReasons)
{
    public static HealerTriageSummary Create(IReadOnlyList<HealerTreatmentPlan> plans)
    {
        var byWorkflow = CountBy(plans, plan => plan.CandidateWorkflow);
        var byRisk = CountBy(plans, plan => plan.Risk);
        var byWorkflowByRisk = plans
            .GroupBy(plan => plan.CandidateWorkflow, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, int>)CountBy(group.ToArray(), plan => plan.Risk),
                StringComparer.Ordinal);
        var authorization = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["authorized"] = plans.Count(plan => plan.ExecutionAuthorization.Authorized),
            ["unauthorized"] = plans.Count(plan => !plan.ExecutionAuthorization.Authorized),
        };
        var blockedReasons = plans
            .SelectMany(plan => plan.BlockedReasons)
            .Where(reason => reason != HealerTreatmentVocab.BlockedReason.None)
            .GroupBy(reason => reason, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return new HealerTriageSummary(plans.Count, byWorkflow, byRisk, byWorkflowByRisk, authorization, blockedReasons);
    }

    private static IReadOnlyDictionary<string, int> CountBy(
        IReadOnlyList<HealerTreatmentPlan> plans,
        Func<HealerTreatmentPlan, string> keySelector)
    {
        return plans
            .GroupBy(keySelector, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }
}
```

- [ ] **Step 4: Verify summary GREEN**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~HealerTriageSummaryTests" -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
```

Expected: PASS.

- [ ] **Step 5: Write failing action projection tests**

Modify `Brainarr.Tests/Services/Core/BrainarrOrchestratorHealerActionsTests.cs` by adding:

```csharp
[Fact]
public void HandleAction_ShouldIncludeTreatmentPlan_ForEveryFinding()
{
    var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
    var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
    var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
    var store = new FakeFindingStore(new[]
    {
        Finding("track-1-redacted", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
    });
    var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);
    var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

    var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
    var json = JsonSerializer.Serialize(result);

    json.Should().Contain("\"treatmentPlan\"");
    json.Should().Contain("\"candidateWorkflow\":\"repairDryRunCandidate\"");
    json.Should().Contain("\"executionAuthorization\"");
    json.Should().Contain("\"authorized\":false");
    json.Should().Contain("\"authority\":\"none\"");
    json.Should().Contain("\"reason\":\"A2_READ_ONLY\"");
    providerFactory.VerifyNoOtherCalls();
    providerInvoker.VerifyNoOtherCalls();
    promptBuilder.VerifyNoOtherCalls();
}

[Fact]
public void HandleAction_ShouldReturnTriageSummary_ForReturnedFindings()
{
    var store = new FakeFindingStore(new[]
    {
        Finding("repair", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
        Finding("review", trackFileId: 2, label: LibraryHealerLabel.NeedsHumanReview),
    });
    var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);

    var result = handler.Handle("healer/getfindings", new Dictionary<string, string>());
    var json = JsonSerializer.Serialize(result);

    json.Should().Contain("\"summary\"");
    json.Should().Contain("\"byWorkflow\"");
    json.Should().Contain("\"repairDryRunCandidate\":1");
    json.Should().Contain("\"review\":1");
    json.Should().Contain("\"authorization\"");
    json.Should().Contain("\"unauthorized\":2");
    json.Should().Contain("\"blockedReasons\"");
    json.Should().Contain("\"REPAIR_DRY_RUN_NOT_IMPLEMENTED\":1");
    json.Should().Contain("\"HUMAN_REVIEW_REQUIRED\":1");
}
```

- [ ] **Step 6: Run action tests and verify RED**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~BrainarrOrchestratorHealerActionsTests" -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
```

Expected: FAIL because `healer/getfindings` does not yet include `treatmentPlan` or `summary`.

- [ ] **Step 7: Implement action projection**

Modify `LibraryHealerActionHandler.GetFindings` to:

```csharp
private object GetFindings(IDictionary<string, string> query)
{
    var limit = Math.Clamp(TryParseQueryInt(query, "limit") ?? 100, 1, 500);
    var projectedFindings = _store.GetRecent(limit)
        .Select(SanitizeFindingForProjection)
        .ToList();
    var plans = projectedFindings
        .Select(finding => HealerTriageAdvisor.Advise(finding))
        .ToList();
    var items = projectedFindings
        .Zip(plans, (finding, plan) => ProjectFinding(finding, plan))
        .ToList();

    return new
    {
        items,
        summary = HealerTriageSummary.Create(plans),
    };
}
```

Add a sanitized finding helper and update `ProjectFinding` to receive the plan:

```csharp
private static LibraryHealerFinding SanitizeFindingForProjection(LibraryHealerFinding finding)
{
    var tagReader = ProjectTagReader(finding.TagReader);
    var probe = finding.Probe is null ? null : ProjectProbe(finding.Probe);
    var label = LibraryHealerReasonCodes.NormalizeLabel(finding.Label, tagReader.Metadata);
    var reasons = LibraryHealerReasonCodes.Normalize(finding.InternalReasonCodes, tagReader.Metadata);

    return finding with
    {
        Id = SanitizeTokenString(finding.Id) ?? string.Empty,
        File = finding.File with
        {
            RedactedPath = SanitizePathDisplayString(finding.File.RedactedPath) ?? string.Empty,
            PathHash = SanitizeTokenString(finding.File.PathHash) ?? string.Empty,
        },
        Label = label,
        InternalReasonCodes = reasons,
        TagReader = tagReader,
        Probe = probe,
    };
}

private static object ProjectFinding(LibraryHealerFinding finding, HealerTreatmentPlan treatmentPlan)
{
    return new
    {
        id = finding.Id,
        trackFileId = finding.File.TrackFileId,
        artistId = finding.File.ArtistId,
        albumId = finding.File.AlbumId,
        path = finding.File.RedactedPath,
        pathHash = finding.File.PathHash,
        size = finding.File.Size,
        modifiedUtc = finding.File.ModifiedUtc,
        label = finding.Label.ToString(),
        reasons = finding.InternalReasonCodes,
        observedAtUtc = finding.ObservedAtUtc,
        tagReader = finding.TagReader,
        probe = finding.Probe,
        treatmentPlan,
    };
}
```

Keep `ProjectTagReader`, `ProjectProbe`, and existing redaction helpers unchanged unless the compiler requires narrow nullability fixes.

- [ ] **Step 8: Verify action GREEN**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~BrainarrOrchestratorHealerActionsTests|FullyQualifiedName~HealerTriageSummaryTests" -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
```

Expected: PASS.

- [ ] **Step 9: Commit Task 3**

```powershell
git add Brainarr.Plugin/Services/Healing/HealerTriageSummary.cs Brainarr.Plugin/Services/Healing/LibraryHealerActionHandler.cs Brainarr.Tests/Services/Healing/HealerTriageSummaryTests.cs Brainarr.Tests/Services/Core/BrainarrOrchestratorHealerActionsTests.cs
git commit -m "feat: expose healer triage treatment plans"
```

---

### Task 4: Architecture Guardrails And Documentation

**Files:**
- Modify: `Brainarr.Tests/Services/Healing/LibraryHealerReadOnlyArchitectureTests.cs`
- Modify: `docs/library-healer.md`
- Modify: `docs/superpowers/specs/2026-07-01-library-doctor-a2-triage-evidence-design.md` only for implementation-confirmed clarifications

**Interfaces:**
- Consumes: A2 production files
- Produces: read-only/AI-free architecture proof and user-facing docs

- [ ] **Step 1: Add failing architecture assertions**

Extend `LibraryHealerReadOnlyArchitectureTests` with assertions that the healing subsystem still does not reference:

```csharp
private static readonly string[] ForbiddenA2References =
{
    "IAIProvider",
    "ILlmProvider",
    "IManageCommandQueue",
    "AlbumSearchCommand",
    "RefreshArtistCommand",
    "RescanFoldersCommand",
    "WriteTags",
    "SyncTags",
    "DeleteMany",
    "File.Move",
    "File.Delete",
    "ProcessStartInfo",
    "ffmpeg",
};
```

Add a test:

```csharp
[Fact]
public void HealingProductionCode_ShouldNotReferenceMutationCommandQueueExternalToolsOrAi()
{
    var productionFiles = Directory.GetFiles(
        Path.Combine(GetRepoRoot(), "Brainarr.Plugin", "Services", "Healing"),
        "*.cs",
        SearchOption.TopDirectoryOnly);

    var combined = string.Join(Environment.NewLine, productionFiles.Select(File.ReadAllText));

    foreach (var forbidden in ForbiddenA2References)
    {
        combined.Should().NotContain(forbidden);
    }
}
```

Use the existing repo-root helper in the file if present. If no helper exists, add:

```csharp
private static string GetRepoRoot()
{
    var current = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(current))
    {
        if (File.Exists(Path.Combine(current, "Brainarr.sln")))
        {
            return current;
        }

        current = Directory.GetParent(current)?.FullName;
    }

    throw new InvalidOperationException("Could not locate Brainarr.sln");
}
```

- [ ] **Step 2: Run architecture test and verify RED or GREEN with explanation**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~LibraryHealerReadOnlyArchitectureTests" -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
```

Expected: PASS if existing guardrails already cover the new strings, or FAIL if the new test finds an accidental reference. A PASS is acceptable here because this is a guardrail expansion over existing behavior, but record the output.

- [ ] **Step 3: Update docs**

Modify `docs/library-healer.md` under `Next Milestones` or a new `A2 Scope` section to state:

```markdown
A2 adds a read-only treatment plan to each finding returned by `healer/getfindings`. The plan includes `candidateWorkflow`, confidence, risk, freshness, fixed blocked reasons, required evidence, required policy gates, rationale codes, and `executionAuthorization.authorized=false`. The `summary` block counts workflows, risks, workflow-by-risk, authorization state, and all non-zero blocked reasons for the returned findings.
```

- [ ] **Step 4: Run focused and doc checks**

Run:

```powershell
dotnet test Brainarr.Tests/Brainarr.Tests.csproj -c Release --filter "FullyQualifiedName~HealerTriage|FullyQualifiedName~BrainarrOrchestratorHealerActionsTests|FullyQualifiedName~LibraryHealerReadOnlyArchitectureTests" -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
git diff --check
```

Expected: tests PASS and `git diff --check` produces no output.

- [ ] **Step 5: Commit Task 4**

```powershell
git add Brainarr.Tests/Services/Healing/LibraryHealerReadOnlyArchitectureTests.cs docs/library-healer.md docs/superpowers/specs/2026-07-01-library-doctor-a2-triage-evidence-design.md
git commit -m "docs: document healer triage treatment plans"
```

---

### Task 5: Full Verification And Adversarial Review

**Files:**
- No planned production files unless review finds blockers.

**Interfaces:**
- Consumes: all A2 tasks
- Produces: final verification evidence before merge consideration

- [ ] **Step 1: Build**

Run:

```powershell
dotnet build Brainarr.sln -c Release -m:1 -p:UseSharedCompilation=false -p:BuildInParallel=false -p:LidarrPath=C:\R\Alex\github\brainarr\ext\Lidarr\_output\net8.0
```

Expected: build succeeds. Existing known warnings are acceptable only if unrelated to A2.

- [ ] **Step 2: Full tests**

Run:

```powershell
dotnet test Brainarr.sln -c Release --no-build -m:1 --logger "trx;LogFileName=library-doctor-a2-triage.trx"
```

Expected: Brainarr test projects pass with no A2 failures.

- [ ] **Step 3: Guardrail searches**

Run:

```powershell
rg -n "IManageCommandQueue|AlbumSearchCommand|RefreshArtistCommand|RescanFoldersCommand|WriteTags|SyncTags|DeleteMany|File\\.Move|File\\.Delete|ProcessStartInfo|ffmpeg|IAIProvider|ILlmProvider" Brainarr.Plugin\Services\Healing
rg -n "Private Artist|550e8400-e29b-41d4-a716-446655440000|D:\\\\Music" Brainarr.Plugin\Services\Healing docs\library-healer.md
```

Expected: first command returns no production mutation/AI references except test-doc examples outside production if the command scope is changed intentionally. Second command returns no production/docs privacy canaries.

- [ ] **Step 4: Adversarial review**

Ask a read-only reviewer:

```text
Review the Library Doctor A2 triage implementation. A2 must remain read-only over media files and Lidarr state. Challenge whether candidateWorkflow can become execution authorization, whether stale/malformed findings can produce an authorized or workflow-specific plan, whether path/tag/MBID data can leak through treatment plans or summaries, whether summaries hide high-risk cases, whether AI or Lidarr command queue references entered the healing subsystem, and whether tests prove the spec. Return Critical, Important, and Minor findings with file/line references.
```

Expected: no Critical or Important findings. Fix accepted findings with RED tests before changing production code.

- [ ] **Step 5: Final status**

Run:

```powershell
git status --short --branch
git log --oneline -8
```

Expected: branch contains only intentional A2 commits and is clean.

## Self-Review

- Spec coverage: tasks cover treatment-plan DTOs, advisor rules, authorization constants, freshness gates, fixed vocabularies, action projection, summaries, privacy, read-only guardrails, docs, full verification, and adversarial review.
- Placeholder scan: no implementation step contains unfinished-marker text or an unspecified error-handling placeholder.
- Type consistency: `HealerTreatmentPlan`, `HealerExecutionAuthorization`, `HealerFindingFreshness`, `HealerTreatmentVocab`, `HealerTriageAdvisor`, and `HealerTriageSummary` are introduced before use by later tasks.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-07-01-library-doctor-a2-triage-evidence.md`.

Recommended execution mode for this branch: Subagent-driven per task, with main-agent review after each task and full adversarial review after Task 5. Inline execution is also viable because the file ownership is narrow.
