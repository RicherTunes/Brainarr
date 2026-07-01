using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public class LibraryHealerClassifierTests
{
    [Fact]
    public void Classify_ShouldReturnFalsePositive_WhenTagReaderHasPositiveDuration()
    {
        var result = LibraryHealerClassifier.Classify(
            new TagReaderEvidence(true, true, 245.2, null, null),
            null);

        result.Label.Should().Be(LibraryHealerLabel.FalsePositive);
        result.InternalReasonCodes.Should().Contain("TAG_READER_DURATION_POSITIVE");
    }

    [Fact]
    public void Classify_ShouldReturnTagReaderSymptom_WhenTagReaderReportsZeroAndNoProbeExists()
    {
        var result = LibraryHealerClassifier.Classify(
            new TagReaderEvidence(true, true, 0, null, null),
            null);

        result.Label.Should().Be(LibraryHealerLabel.TagReaderSymptom);
        result.InternalReasonCodes.Should().Contain("TAG_READER_ZERO_DURATION");
        result.InternalReasonCodes.Should().NotContain("HEADER_REPAIR_CANDIDATE");
    }

    [Fact]
    public void Classify_ShouldReturnProbeEvidence_WhenTagReaderZeroButProbeShowsAudioDuration()
    {
        var result = LibraryHealerClassifier.Classify(
            new TagReaderEvidence(true, true, 0, null, null),
            new ProbeEvidence(true, true, 245.1, "mov,mp4,m4a", "flac", null, null));

        result.Label.Should().Be(LibraryHealerLabel.ProbeEvidence);
        result.InternalReasonCodes.Should().Contain("TAG_READER_ZERO_DURATION");
        result.InternalReasonCodes.Should().Contain("PROBE_DURATION_POSITIVE");
        result.InternalReasonCodes.Should().Contain("HEADER_REPAIR_CANDIDATE");
    }

    [Fact]
    public void Classify_ShouldReturnNeedsHumanReview_WhenBothReadersFail()
    {
        var result = LibraryHealerClassifier.Classify(
            new TagReaderEvidence(true, false, null, "CorruptFileException", "bad header"),
            new ProbeEvidence(true, false, null, null, null, "InvalidDataException", "decode failed"));

        result.Label.Should().Be(LibraryHealerLabel.NeedsHumanReview);
        result.InternalReasonCodes.Should().Contain("TAG_READER_FAILED");
        result.InternalReasonCodes.Should().Contain("PROBE_FAILED");
    }
}
