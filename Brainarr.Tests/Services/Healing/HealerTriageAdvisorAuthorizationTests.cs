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

    [Fact]
    public void Advise_ShouldNeverAuthorizeExecution_ForFailClosedPlans()
    {
        var plans = new[]
        {
            HealerTriageAdvisor.Advise(
                HealerTriageAdvisorTests.Finding(
                    LibraryHealerLabel.TagReaderSymptom,
                    new[] { "TAG_READER_ZERO_DURATION" },
                    new TagReaderEvidence(true, true, 0, null, null),
                    null),
                new HealerFindingFreshness(
                    HealerTreatmentVocab.Freshness.Stale,
                    HealerTreatmentVocab.Freshness.Current)),
            HealerTriageAdvisor.Advise(
                HealerTriageAdvisorTests.Finding(
                    LibraryHealerLabel.TagReaderSymptom,
                    new[] { "TAG_READER_ZERO_DURATION" },
                    new TagReaderEvidence(true, true, 0, null, null),
                    null),
                new HealerFindingFreshness(
                    HealerTreatmentVocab.Freshness.Current,
                    HealerTreatmentVocab.Freshness.Current,
                    MalformedRecord: true)),
            HealerTriageAdvisor.Advise(HealerTriageAdvisorTests.Finding(
                (LibraryHealerLabel)999,
                Array.Empty<string>(),
                new TagReaderEvidence(true, true, 0, null, null),
                null)),
        };

        plans.Should().OnlyContain(plan => plan.ExecutionAuthorization.Authorized == false);
        plans.Should().OnlyContain(plan => plan.ExecutionAuthorization.Authority == HealerTreatmentVocab.AuthorizationAuthority.None);
        plans.Should().OnlyContain(plan => plan.ExecutionAuthorization.Reason == HealerTreatmentVocab.AuthorizationReason.A2ReadOnly);
        plans.Should().OnlyContain(plan => plan.SafetyLevel == HealerTreatmentVocab.SafetyLevel.ReadOnly);
    }
}
