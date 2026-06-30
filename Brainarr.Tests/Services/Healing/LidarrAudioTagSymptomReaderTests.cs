using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;

namespace Brainarr.Tests.Services.Healing;

public class LidarrAudioTagSymptomReaderTests
{
    [Fact]
    public void Read_ShouldReturnSuccessfulEvidence_WhenLidarrReaderReturnsDuration()
    {
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags("song.flac"))
            .Returns(new ParsedTrackInfo
            {
                Duration = TimeSpan.FromSeconds(245)
            });

        var result = new LidarrAudioTagSymptomReader(audio.Object).Read("song.flac", CancellationToken.None);

        result.ReadAttempted.Should().BeTrue();
        result.ReadSucceeded.Should().BeTrue();
        result.DurationSeconds.Should().Be(245);
        result.ErrorType.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Read_ShouldReturnFailureEvidence_WhenLidarrReaderThrows()
    {
        const string path = @"D:\Music\Private Artist\bad.m4a";
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags(path))
            .Throws(new InvalidDataException(@"broken D:\Music\Private Artist\bad.m4a"));

        var result = new LidarrAudioTagSymptomReader(audio.Object).Read(path, CancellationToken.None);

        result.ReadAttempted.Should().BeTrue();
        result.ReadSucceeded.Should().BeFalse();
        result.DurationSeconds.Should().BeNull();
        result.ErrorType.Should().Be(nameof(InvalidDataException));
        result.ErrorMessage.Should().Contain("broken");
        result.ErrorMessage.Should().NotContain("Private Artist");
        result.ErrorMessage.Should().NotContain(@"D:\Music");
    }

    [Fact]
    public void Read_ShouldReturnTimeoutEvidence_WhenLidarrReaderHangsPastTimeout()
    {
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags("slow.flac"))
            .Returns(() =>
            {
                Thread.Sleep(250);
                return new ParsedTrackInfo
                {
                    Duration = TimeSpan.FromSeconds(245)
                };
            });

        var result = new LidarrAudioTagSymptomReader(audio.Object, TimeSpan.FromMilliseconds(25))
            .Read("slow.flac", CancellationToken.None);

        result.ReadAttempted.Should().BeTrue();
        result.ReadSucceeded.Should().BeFalse();
        result.DurationSeconds.Should().BeNull();
        result.ErrorMessage.Should().Contain("Timed out");
    }

    [Fact]
    public async Task Read_ShouldNotStartSecondLidarrRead_WhenFirstTimedOutReadIsStillRunning()
    {
        var releaseRead = new ManualResetEventSlim(false);
        var firstReadStarted = new ManualResetEventSlim(false);
        var firstReadFinished = new ManualResetEventSlim(false);
        var readAttempts = 0;
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags(It.IsAny<string>()))
            .Returns(() =>
            {
                var attempt = Interlocked.Increment(ref readAttempts);
                if (attempt == 1)
                {
                    firstReadStarted.Set();
                    try
                    {
                        releaseRead.Wait();
                    }
                    finally
                    {
                        firstReadFinished.Set();
                    }
                }

                return new ParsedTrackInfo
                {
                    Duration = TimeSpan.FromSeconds(245)
                };
            });

        var reader = new LidarrAudioTagSymptomReader(audio.Object, TimeSpan.FromMilliseconds(100));
        Task<TagReaderEvidence>? firstReadTask = null;

        try
        {
            firstReadTask = Task.Run(() => reader.Read("first.flac", CancellationToken.None));
            firstReadStarted.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();

            var firstResult = await firstReadTask.WaitAsync(TimeSpan.FromSeconds(2));
            firstResult.ReadSucceeded.Should().BeFalse();
            firstResult.ErrorType.Should().Be(nameof(TimeoutException));

            var secondResult = reader.Read("second.flac", CancellationToken.None);

            secondResult.ReadSucceeded.Should().BeFalse();
            secondResult.ErrorType.Should().Be(nameof(TimeoutException));
            Volatile.Read(ref readAttempts).Should().Be(1);
            audio.Verify(x => x.ReadTags(It.IsAny<string>()), Times.Once);
        }
        finally
        {
            releaseRead.Set();
            firstReadFinished.Wait(TimeSpan.FromSeconds(2));
            releaseRead.Dispose();
            firstReadStarted.Dispose();
            firstReadFinished.Dispose();
            if (firstReadTask is { IsCompleted: false })
            {
                await firstReadTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
    }

    [Fact]
    public void Read_ShouldUsePreciseTimeoutEvidence_WhenWaitingForLidarrReaderTimesOut()
    {
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags("slow.flac"))
            .Returns(() =>
            {
                Thread.Sleep(250);
                return new ParsedTrackInfo
                {
                    Duration = TimeSpan.FromSeconds(245)
                };
            });

        var result = new LidarrAudioTagSymptomReader(audio.Object, TimeSpan.FromMilliseconds(25))
            .Read("slow.flac", CancellationToken.None);

        result.ErrorType.Should().Be(nameof(TimeoutException));
        result.ErrorMessage.Should().Be("Timed out waiting for Lidarr audio tag reader");
    }

    [Fact]
    public void Read_ShouldReturnCanceledEvidenceWithoutCallingReader_WhenTokenAlreadyCanceled()
    {
        var audio = new Mock<IAudioTagService>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = new LidarrAudioTagSymptomReader(audio.Object).Read("song.flac", cancellation.Token);

        result.ReadAttempted.Should().BeTrue();
        result.ReadSucceeded.Should().BeFalse();
        result.DurationSeconds.Should().BeNull();
        result.ErrorType.Should().Be(nameof(OperationCanceledException));
        audio.Verify(x => x.ReadTags(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ReadFingerprint_ShouldReturnSizeAndModifiedUtc_WhenFileExists()
    {
        var path = typeof(PathPrivacy).Assembly.Location;
        var expected = new FileInfo(path);

        var result = new FileFingerprintService().Read(path);

        result.Exists.Should().BeTrue();
        result.Size.Should().Be(expected.Length);
        result.ModifiedUtc.Should().Be(expected.LastWriteTimeUtc);
    }

    [Fact]
    public void ReadFingerprint_ShouldReturnMissingFingerprint_WhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".flac");

        var result = new FileFingerprintService().Read(path);

        result.Exists.Should().BeFalse();
        result.Size.Should().BeNull();
        result.ModifiedUtc.Should().BeNull();
    }

    [Fact]
    public void ReadFingerprint_ShouldReturnMissingFingerprint_WhenPathIsInvalid()
    {
        var result = new FileFingerprintService().Read("\0");

        result.Exists.Should().BeFalse();
        result.Size.Should().BeNull();
        result.ModifiedUtc.Should().BeNull();
    }
}
