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
            new DateTime(2026, 7, 1, 1, 2, 3, DateTimeKind.Utc),
            EvidenceFreshness: HealerTreatmentVocab.Freshness.Current,
            IdentityFreshness: HealerTreatmentVocab.Freshness.Current);
    }
}
