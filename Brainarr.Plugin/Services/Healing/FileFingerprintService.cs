using NzbDrone.Core.ImportLists.Brainarr.Utils;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed class FileFingerprintService : IFileFingerprintService
{
    private static readonly TimeSpan DefaultExistenceTimeout = TimeSpan.FromSeconds(5);

    private readonly Func<string, bool>? _existsProbe;
    private SemaphoreSlim _existsGate = new(1, 1);

    public FileFingerprintService()
    {
    }

    internal FileFingerprintService(Func<string, bool> existsProbe)
    {
        _existsProbe = existsProbe ?? throw new ArgumentNullException(nameof(existsProbe));
    }

    public FileExistenceEvidence CheckExists(string path, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Unknown(nameof(OperationCanceledException), "Canceled checking file existence");
        }

        var boundedTimeout = NormalizeTimeout(timeout);
        if (boundedTimeout == TimeSpan.Zero)
        {
            return TimeoutEvidence();
        }

        var stopwatch = boundedTimeout == Timeout.InfiniteTimeSpan
            ? null
            : System.Diagnostics.Stopwatch.StartNew();

        var gate = Volatile.Read(ref _existsGate);
        try
        {
            if (!gate.Wait(boundedTimeout, cancellationToken))
            {
                return TimeoutEvidence();
            }

            var releaseInTask = false;
            try
            {
                var probeTimeout = RemainingTimeout(boundedTimeout, stopwatch);
                if (probeTimeout == TimeSpan.Zero)
                {
                    return TimeoutEvidence();
                }

                var existsTask = Task.Factory.StartNew(
                    () =>
                    {
                        try
                        {
                            return ProbePath(path);
                        }
                        catch (Exception ex)
                        {
                            return Unknown(ReasonCodeFor(ex), PathPrivacy.RedactMessage(ex.Message, path));
                        }
                        finally
                        {
                            gate.Release();
                        }
                    },
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
                releaseInTask = true;

                try
                {
                    return SafeAsyncHelper.RunSafeSync(() => existsTask.WaitAsync(probeTimeout, cancellationToken));
                }
                catch (TimeoutException)
                {
                    // The probe hung past its own timeout and is still holding `gate` (it will
                    // eventually Release() it in its finally, whenever it finally completes/never
                    // does). Abandon the stale gate and swap in a fresh one so subsequent
                    // CheckExists calls are not starved waiting behind a probe that may never
                    // return, mirroring the busy-signal/slot-abandon treatment LidarrAudioTagSymptomReader
                    // uses for its read gate.
                    Interlocked.CompareExchange(ref _existsGate, new SemaphoreSlim(1, 1), gate);
                    return TimeoutEvidence();
                }
            }
            finally
            {
                if (!releaseInTask)
                {
                    gate.Release();
                }
            }
        }
        catch (TimeoutException)
        {
            return TimeoutEvidence();
        }
        catch (OperationCanceledException)
        {
            return Unknown(nameof(OperationCanceledException), "Canceled checking file existence");
        }
        catch (Exception ex)
        {
            return Unknown(ReasonCodeFor(ex), PathPrivacy.RedactMessage(ex.Message, path));
        }
    }

    public FileFingerprint Read(string path)
    {
        try
        {
            var existence = CheckExists(path, DefaultExistenceTimeout, CancellationToken.None);
            if (!existence.CheckSucceeded || !existence.Exists)
            {
                return Missing();
            }

            var info = new FileInfo(path);
            return new FileFingerprint(true, info.Length, info.LastWriteTimeUtc);
        }
        catch (Exception ex) when (IsExpectedFileChurn(ex))
        {
            return Missing();
        }
    }

    private FileExistenceEvidence ProbePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Unknown("PATH_EMPTY", "Path is empty");
        }

        if (_existsProbe is not null)
        {
            return ProbeInjected(path);
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                return Unknown("PATH_IS_DIRECTORY", "Path points to a directory");
            }

            return Found();
        }
        catch (FileNotFoundException)
        {
            return MissingEvidence();
        }
        catch (DirectoryNotFoundException ex)
        {
            return Unknown("PATH_PARENT_UNAVAILABLE", PathPrivacy.RedactMessage(ex.Message, path));
        }
        catch (Exception ex) when (IsExpectedFileChurn(ex))
        {
            return Unknown(ReasonCodeFor(ex), PathPrivacy.RedactMessage(ex.Message, path));
        }
    }

    private FileExistenceEvidence ProbeInjected(string path)
    {
        try
        {
            return _existsProbe!(path) ? Found() : MissingEvidence();
        }
        catch (Exception ex) when (IsExpectedFileChurn(ex))
        {
            return Unknown(ReasonCodeFor(ex), PathPrivacy.RedactMessage(ex.Message, path));
        }
    }

    private static FileFingerprint Missing()
    {
        return new FileFingerprint(false, null, null);
    }

    private static FileExistenceEvidence Found()
    {
        return new FileExistenceEvidence(true, true, true, null, null);
    }

    private static FileExistenceEvidence MissingEvidence()
    {
        return new FileExistenceEvidence(true, true, false, null, null);
    }

    private static FileExistenceEvidence Unknown(string? errorType, string? errorMessage)
    {
        return new FileExistenceEvidence(true, false, false, errorType, errorMessage);
    }

    private static FileExistenceEvidence TimeoutEvidence()
    {
        return Unknown(nameof(TimeoutException), "Timed out checking file existence");
    }

    private static TimeSpan NormalizeTimeout(TimeSpan timeout)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return timeout;
        }

        return timeout > TimeSpan.Zero ? timeout : TimeSpan.Zero;
    }

    private static TimeSpan RemainingTimeout(
        TimeSpan timeout,
        System.Diagnostics.Stopwatch? stopwatch)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return timeout;
        }

        var remaining = timeout - (stopwatch?.Elapsed ?? TimeSpan.Zero);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private static string ReasonCodeFor(Exception ex)
    {
        return ex switch
        {
            UnauthorizedAccessException => "PATH_ACCESS_DENIED",
            System.Security.SecurityException => "PATH_ACCESS_DENIED",
            ArgumentException => "PATH_INVALID",
            NotSupportedException => "PATH_INVALID",
            PathTooLongException => "PATH_INVALID",
            IOException => "PATH_PROBE_IO_ERROR",
            _ => ex.GetType().Name,
        };
    }

    private static bool IsExpectedFileChurn(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or System.Security.SecurityException
            or ArgumentException
            or NotSupportedException;
    }
}
