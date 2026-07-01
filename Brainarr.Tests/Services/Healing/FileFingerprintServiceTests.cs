using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

namespace Brainarr.Tests.Services.Healing;

public sealed class FileFingerprintServiceTests
{
    [Fact]
    public void Read_ShouldReturnSizeAndModifiedUtc_WhenFileExists()
    {
        var path = typeof(PathPrivacy).Assembly.Location;
        var expected = new FileInfo(path);

        var result = new FileFingerprintService().Read(path);

        result.Exists.Should().BeTrue();
        result.Size.Should().Be(expected.Length);
        result.ModifiedUtc.Should().Be(expected.LastWriteTimeUtc);
    }

    [Fact]
    public void Read_ShouldReturnMissingFingerprint_WhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".flac");

        var result = new FileFingerprintService().Read(path);

        result.Exists.Should().BeFalse();
        result.Size.Should().BeNull();
        result.ModifiedUtc.Should().BeNull();
    }

    [Fact]
    public void Read_ShouldReturnMissingFingerprint_WhenPathIsInvalid()
    {
        var result = new FileFingerprintService().Read("\0");

        result.Exists.Should().BeFalse();
        result.Size.Should().BeNull();
        result.ModifiedUtc.Should().BeNull();
    }

    [Fact]
    public void CheckExists_ShouldReturnFoundEvidence_WhenFileExists()
    {
        var path = typeof(PathPrivacy).Assembly.Location;

        var result = new FileFingerprintService().CheckExists(path, CancellationToken.None);

        result.CheckAttempted.Should().BeTrue();
        result.CheckSucceeded.Should().BeTrue();
        result.Exists.Should().BeTrue();
        result.ErrorType.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void CheckExists_ShouldReturnMissingEvidence_WhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".flac");

        var result = new FileFingerprintService().CheckExists(path, CancellationToken.None);

        result.CheckAttempted.Should().BeTrue();
        result.CheckSucceeded.Should().BeTrue();
        result.Exists.Should().BeFalse();
        result.ErrorType.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void CheckExists_ShouldReturnInconclusiveEvidence_WhenPathIsInvalid()
    {
        var result = new FileFingerprintService().CheckExists("\0", CancellationToken.None);

        result.CheckAttempted.Should().BeTrue();
        result.CheckSucceeded.Should().BeFalse();
        result.Exists.Should().BeFalse();
        result.ErrorType.Should().Be("PATH_INVALID");
    }

    [Fact]
    public void CheckExists_ShouldReturnInconclusiveEvidence_WhenTokenAlreadyCanceled()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = new FileFingerprintService().CheckExists("slow.flac", cancellation.Token);

        result.CheckAttempted.Should().BeTrue();
        result.CheckSucceeded.Should().BeFalse();
        result.Exists.Should().BeFalse();
        result.ErrorType.Should().Be(nameof(OperationCanceledException));
    }

}
