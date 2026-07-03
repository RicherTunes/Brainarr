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
            new HealerFindingFreshness(
                HealerTreatmentVocab.Freshness.Stale,
                HealerTreatmentVocab.Freshness.Current));

        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.Risk.Should().Be(HealerTreatmentVocab.Risk.High);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.EvidenceFreshnessNotCurrent);
        plan.RequiredEvidence.Should().Contain(HealerTreatmentVocab.RequiredEvidence.FreshFileFingerprint);
        plan.RequiredPolicyGates.Should().Equal(HealerTreatmentVocab.RequiredPolicyGate.None);
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
            new HealerFindingFreshness(
                HealerTreatmentVocab.Freshness.Current,
                HealerTreatmentVocab.Freshness.Stale));

        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.Risk.Should().Be(HealerTreatmentVocab.Risk.High);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.IdentityFreshnessNotCurrent);
        plan.RequiredEvidence.Should().Contain(HealerTreatmentVocab.RequiredEvidence.FreshPathIdentity);
        plan.RequiredPolicyGates.Should().Equal(HealerTreatmentVocab.RequiredPolicyGate.None);
        plan.ExecutionAuthorization.Authorized.Should().BeFalse();
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
        plan.Risk.Should().Be(HealerTreatmentVocab.Risk.High);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.MalformedFindingRecord);
        plan.RequiredEvidence.Should().Equal(HealerTreatmentVocab.RequiredEvidence.None);
        plan.RequiredPolicyGates.Should().Equal(HealerTreatmentVocab.RequiredPolicyGate.None);
        plan.ExecutionAuthorization.Authorized.Should().BeFalse();
    }

    [Fact]
    public void Advise_ShouldPreferMalformedRecord_WhenMalformedFindingAlsoHasUnknownFreshness()
    {
        var plan = HealerTriageAdvisor.Advise(
            HealerTriageAdvisorTests.Finding(
                LibraryHealerLabel.TagReaderSymptom,
                new[] { "TAG_READER_ZERO_DURATION" },
                new TagReaderEvidence(true, true, 0, null, null),
                null),
            new HealerFindingFreshness(
                HealerTreatmentVocab.Freshness.Unknown,
                HealerTreatmentVocab.Freshness.Unknown,
                MalformedRecord: true));

        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.MalformedFindingRecord);
        plan.BlockedReasons.Should().NotContain(HealerTreatmentVocab.BlockedReason.EvidenceFreshnessNotCurrent);
        plan.RequiredEvidence.Should().Equal(HealerTreatmentVocab.RequiredEvidence.None);
    }

    [Fact]
    public void Advise_ShouldReturnReview_WhenPersistedFindingHasMissingTagReader()
    {
        var plan = HealerTriageAdvisor.Advise(new LibraryHealerFinding(
            "triage-malformed-test",
            new LibraryHealerFileIdentity(
                1,
                2,
                3,
                "track01.flac#abcdef123456",
                "abcdef123456",
                123,
                new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_ZERO_DURATION" },
            null!,
            null,
            new DateTime(2026, 7, 1, 1, 2, 3, DateTimeKind.Utc)));

        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.Risk.Should().Be(HealerTreatmentVocab.Risk.High);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.MalformedFindingRecord);
        plan.RationaleCodes.Should().Contain(HealerTreatmentVocab.Rationale.MalformedFindingRecord);
        plan.ExecutionAuthorization.Authorized.Should().BeFalse();
    }

    [Fact]
    public void Advise_ShouldUseFindingFreshness_WhenFreshnessArgumentOmitted()
    {
        var finding = HealerTriageAdvisorTests.Finding(
            LibraryHealerLabel.TagMetadataIssue,
            new[] { "TAG_METADATA_MISSING" },
            new TagReaderEvidence(true, true, 245.2, null, null),
            null) with
        {
            EvidenceFreshness = HealerTreatmentVocab.Freshness.Unknown,
            IdentityFreshness = HealerTreatmentVocab.Freshness.Current,
        };

        var plan = HealerTriageAdvisor.Advise(finding);

        plan.CandidateWorkflow.Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.Risk.Should().Be(HealerTreatmentVocab.Risk.High);
        plan.EvidenceFreshness.Should().Be(HealerTreatmentVocab.Freshness.Unknown);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.EvidenceFreshnessNotCurrent);
        plan.RequiredEvidence.Should().Contain(HealerTreatmentVocab.RequiredEvidence.FreshFileFingerprint);
    }
}
