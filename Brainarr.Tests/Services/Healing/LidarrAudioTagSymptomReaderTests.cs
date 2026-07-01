using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;

namespace Brainarr.Tests.Services.Healing;

[Collection("ThreadSensitive")]
public class LidarrAudioTagSymptomReaderTests
{
    [Fact]
    public void Read_ShouldReturnSuccessfulEvidence_WhenLidarrReaderReturnsDuration()
    {
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags("song.flac"))
            .Returns(new ParsedTrackInfo
            {
                Duration = TimeSpan.FromSeconds(245),
                Title = "Song",
                ArtistTitle = "Private Artist",
                AlbumTitle = "Album",
                ReleaseMBId = "release-id",
            });

        var result = new LidarrAudioTagSymptomReader(audio.Object).Read("song.flac", CancellationToken.None);

        result.ReadAttempted.Should().BeTrue();
        result.ReadSucceeded.Should().BeTrue();
        result.DurationSeconds.Should().Be(245);
        result.ErrorType.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Read_ShouldCaptureMetadataCompletenessWithoutRawTagValues()
    {
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags("missing-tags.flac"))
            .Returns(new ParsedTrackInfo
            {
                Duration = TimeSpan.FromSeconds(245),
                Title = "   ",
                ArtistTitle = "Private Artist",
                AlbumTitle = null,
                ReleaseMBId = "",
                RecordingMBId = null,
                TrackMBId = null,
                ArtistMBId = null,
                AlbumMBId = null,
            });

        var result = new LidarrAudioTagSymptomReader(audio.Object)
            .Read("missing-tags.flac", CancellationToken.None);

        result.Metadata.Should().NotBeNull();
        result.Metadata!.TitlePresent.Should().BeFalse();
        result.Metadata.ArtistPresent.Should().BeTrue();
        result.Metadata.AlbumPresent.Should().BeFalse();
        result.Metadata.AnyMusicBrainzIdPresent.Should().BeFalse();
        result.Metadata.MissingFields.Should().Equal("title", "album", "musicBrainzId");
        result.Metadata.ToString().Should().NotContain("Private Artist");
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
        using var releaseRead = new ManualResetEventSlim(false);
        using var readFinished = new ManualResetEventSlim(false);
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags("slow.flac"))
            .Returns(() =>
            {
                try
                {
                    releaseRead.Wait();
                    return new ParsedTrackInfo
                    {
                        Duration = TimeSpan.FromSeconds(245)
                    };
                }
                finally
                {
                    readFinished.Set();
                }
            });

        TagReaderEvidence result;
        try
        {
            result = new LidarrAudioTagSymptomReader(audio.Object, TimeSpan.FromMilliseconds(25))
                .Read("slow.flac", CancellationToken.None);
        }
        finally
        {
            releaseRead.Set();
            readFinished.Wait(TimeSpan.FromSeconds(2));
        }

        result.ReadAttempted.Should().BeTrue();
        result.ReadSucceeded.Should().BeFalse();
        result.DurationSeconds.Should().BeNull();
        result.ErrorMessage.Should().Contain("Timed out");
    }

    [Fact]
    public async Task Read_ShouldNotStartSecondLidarrRead_WhenFirstTimedOutReadIsStillRunning()
    {
        var releaseRead = new ManualResetEventSlim(false);
        var firstReadFinished = new ManualResetEventSlim(false);
        var readAttempts = 0;
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags(It.IsAny<string>()))
            .Returns(() =>
            {
                var attempt = Interlocked.Increment(ref readAttempts);
                if (attempt == 1)
                {
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
            firstReadTask = Task.Factory.StartNew(
                () => reader.Read("first.flac", CancellationToken.None),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            var firstResult = await firstReadTask.WaitAsync(TimeSpan.FromSeconds(2));
            firstResult.ReadSucceeded.Should().BeFalse();
            firstResult.ErrorType.Should().Be(nameof(TimeoutException));

            var secondResult = reader.Read("second.flac", CancellationToken.None);

            secondResult.ReadSucceeded.Should().BeFalse();
            secondResult.ErrorType.Should().Be("TagReaderBusyException");
            secondResult.ErrorMessage.Should().Contain("busy");
            releaseRead.Set();
            firstReadFinished.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
            Volatile.Read(ref readAttempts).Should().Be(1);
            audio.Verify(x => x.ReadTags(It.IsAny<string>()), Times.Once);
        }
        finally
        {
            releaseRead.Set();
            firstReadFinished.Wait(TimeSpan.FromSeconds(2));
            releaseRead.Dispose();
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
        using var releaseRead = new ManualResetEventSlim(false);
        using var readFinished = new ManualResetEventSlim(false);
        var audio = new Mock<IAudioTagService>();
        audio.Setup(x => x.ReadTags("slow.flac"))
            .Returns(() =>
            {
                try
                {
                    releaseRead.Wait();
                    return new ParsedTrackInfo
                    {
                        Duration = TimeSpan.FromSeconds(245)
                    };
                }
                finally
                {
                    readFinished.Set();
                }
            });

        TagReaderEvidence result;
        try
        {
            result = new LidarrAudioTagSymptomReader(audio.Object, TimeSpan.FromMilliseconds(25))
                .Read("slow.flac", CancellationToken.None);
        }
        finally
        {
            releaseRead.Set();
            readFinished.Wait(TimeSpan.FromSeconds(2));
        }

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

}
