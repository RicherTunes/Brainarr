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
                new TagReaderEvidence(
                    true,
                    true,
                    245.2,
                    null,
                    null,
                    new TagMetadataEvidence(false, true, true, true, new[] { "title" })),
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
        summary.ByWorkflow[HealerTreatmentVocab.Workflow.Review].Should().Be(1);
        summary.ByRisk[HealerTreatmentVocab.Risk.Medium].Should().Be(2);
        summary.ByRisk[HealerTreatmentVocab.Risk.High].Should().Be(1);
        summary.ByWorkflowByRisk[HealerTreatmentVocab.Workflow.RepairDryRunCandidate][HealerTreatmentVocab.Risk.Medium].Should().Be(1);
        summary.Authorization["authorized"].Should().Be(0);
        summary.Authorization["unauthorized"].Should().Be(3);
        summary.BlockedReasons[HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented].Should().Be(1);
        summary.BlockedReasons[HealerTreatmentVocab.BlockedReason.TagRepairNotImplemented].Should().Be(1);
        summary.BlockedReasons[HealerTreatmentVocab.BlockedReason.HumanReviewRequired].Should().Be(1);
    }

    [Fact]
    public void Create_ShouldCountEveryNonNoneBlockedReason()
    {
        var plans = new[]
        {
            HealerTriageAdvisor.Advise(HealerTriageAdvisorTests.Finding(
                LibraryHealerLabel.TagReaderSymptom,
                new[] { "TAG_READER_ZERO_DURATION" },
                new TagReaderEvidence(true, true, 0, null, null),
                null)),
            HealerTriageAdvisor.Advise(HealerTriageAdvisorTests.Finding(
                LibraryHealerLabel.FalsePositive,
                new[] { "TAG_READER_DURATION_POSITIVE" },
                new TagReaderEvidence(true, true, 245.2, null, null),
                null)),
        };

        var summary = HealerTriageSummary.Create(plans);

        summary.BlockedReasons.Should().ContainKey(HealerTreatmentVocab.BlockedReason.ProbeEvidenceMissing);
        summary.BlockedReasons.Should().ContainKey(HealerTreatmentVocab.BlockedReason.FullDecodeEvidenceMissing);
        summary.BlockedReasons.Should().ContainKey(HealerTreatmentVocab.BlockedReason.BackupPolicyMissing);
        summary.BlockedReasons.Should().ContainKey(HealerTreatmentVocab.BlockedReason.JournalPolicyMissing);
        summary.BlockedReasons.Should().ContainKey(HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented);
        summary.BlockedReasons.Should().NotContainKey(HealerTreatmentVocab.BlockedReason.None);
    }
}
