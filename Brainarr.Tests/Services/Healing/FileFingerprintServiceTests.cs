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

        var result = new FileFingerprintService().CheckExists(path, TimeSpan.FromSeconds(1), CancellationToken.None);

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

        var result = new FileFingerprintService().CheckExists(path, TimeSpan.FromSeconds(1), CancellationToken.None);

        result.CheckAttempted.Should().BeTrue();
        result.CheckSucceeded.Should().BeTrue();
        result.Exists.Should().BeFalse();
        result.ErrorType.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void CheckExists_ShouldReturnInconclusiveEvidence_WhenPathIsInvalid()
    {
        var result = new FileFingerprintService().CheckExists("\0", TimeSpan.FromSeconds(1), CancellationToken.None);

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

        var result = new FileFingerprintService().CheckExists("slow.flac", TimeSpan.FromSeconds(1), cancellation.Token);

        result.CheckAttempted.Should().BeTrue();
        result.CheckSucceeded.Should().BeFalse();
        result.Exists.Should().BeFalse();
        result.ErrorType.Should().Be(nameof(OperationCanceledException));
    }

    [Fact]
    public void CheckExists_ShouldReturnTimeoutEvidence_WhenProbeExceedsTimeout()
    {
        using var releaseProbe = new ManualResetEventSlim(false);
        using var probeFinished = new ManualResetEventSlim(false);
        var service = new FileFingerprintService(_ =>
        {
            try
            {
                releaseProbe.Wait();
                return true;
            }
            finally
            {
                probeFinished.Set();
            }
        });
        using var completed = new ManualResetEventSlim(false);
        FileExistenceEvidence? result = null;
        Exception? failure = null;
        var checkThread = new Thread(() =>
        {
            try
            {
                result = service.CheckExists(
                    "slow.flac",
                    TimeSpan.FromMilliseconds(25),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = nameof(CheckExists_ShouldReturnTimeoutEvidence_WhenProbeExceedsTimeout)
        };

        checkThread.Start();

        try
        {
            completed.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue("CheckExists must enforce the supplied timeout");
            failure.Should().BeNull();
            result.Should().NotBeNull();
            var evidence = result!;

            evidence.CheckAttempted.Should().BeTrue();
            evidence.CheckSucceeded.Should().BeFalse();
            evidence.Exists.Should().BeFalse();
            evidence.ErrorType.Should().Be(nameof(TimeoutException));
            evidence.ErrorMessage.Should().Contain("Timed out");
        }
        finally
        {
            releaseProbe.Set();
            completed.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
            probeFinished.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        }
    }

    [Fact]
    public void CheckExists_ShouldUseSingleTimeoutBudgetAcrossGateWaitAndProbe()
    {
        using var releaseFirstProbe = new ManualResetEventSlim(false);
        using var releaseSecondProbe = new ManualResetEventSlim(false);
        using var firstProbeEntered = new ManualResetEventSlim(false);
        using var firstCallCompleted = new ManualResetEventSlim(false);
        using var secondCallCompleted = new ManualResetEventSlim(false);
        var probeCalls = 0;
        var service = new FileFingerprintService(_ =>
        {
            if (Interlocked.Increment(ref probeCalls) == 1)
            {
                firstProbeEntered.Set();
                releaseFirstProbe.Wait();
                return true;
            }

            releaseSecondProbe.Wait();
            return true;
        });

        var firstThread = new Thread(() =>
        {
            try
            {
                service.CheckExists("first-slow.flac", TimeSpan.FromMilliseconds(50), CancellationToken.None);
            }
            finally
            {
                firstCallCompleted.Set();
            }
        })
        {
            IsBackground = true,
            Name = nameof(CheckExists_ShouldUseSingleTimeoutBudgetAcrossGateWaitAndProbe) + "-first"
        };
        firstThread.Start();

        FileExistenceEvidence? secondResult = null;
        Exception? secondFailure = null;
        var secondThread = new Thread(() =>
        {
            try
            {
                secondResult = service.CheckExists("second-slow.flac", TimeSpan.FromMilliseconds(800), CancellationToken.None);
            }
            catch (Exception ex)
            {
                secondFailure = ex;
            }
            finally
            {
                secondCallCompleted.Set();
            }
        })
        {
            IsBackground = true,
            Name = nameof(CheckExists_ShouldUseSingleTimeoutBudgetAcrossGateWaitAndProbe) + "-second"
        };

        try
        {
            firstProbeEntered.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();
            firstCallCompleted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue();

            secondThread.Start();
            Thread.Sleep(300);
            releaseFirstProbe.Set();

            secondCallCompleted.Wait(TimeSpan.FromMilliseconds(950))
                .Should()
                .BeTrue("the timeout budget must cover both gate wait and probe wait");
            secondFailure.Should().BeNull();
            secondResult.Should().NotBeNull();
            secondResult!.CheckSucceeded.Should().BeFalse();
            secondResult.ErrorType.Should().Be(nameof(TimeoutException));
        }
        finally
        {
            releaseFirstProbe.Set();
            releaseSecondProbe.Set();
            firstCallCompleted.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
            secondCallCompleted.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        }
    }

}
