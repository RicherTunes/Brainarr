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
        plan.Risk.Should().Be(HealerTreatmentVocab.Risk.High);
        plan.BlockedReasons.Should().Contain(HealerTreatmentVocab.BlockedReason.UnknownFindingLabel);
        plan.RationaleCodes.Should().Contain(HealerTreatmentVocab.Rationale.UnknownFindingLabel);
        plan.RequiredEvidence.Should().Equal(HealerTreatmentVocab.RequiredEvidence.None);
        plan.RequiredPolicyGates.Should().Equal(HealerTreatmentVocab.RequiredPolicyGate.None);
        plan.ExecutionAuthorization.Authorized.Should().BeFalse();
    }

    [Fact]
    public void BlockedReasonVocabulary_ShouldReserveA2ReadOnly()
    {
        HealerTreatmentVocab.BlockedReason.A2ReadOnly.Should().Be("A2_READ_ONLY");
    }
}
