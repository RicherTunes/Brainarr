namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed class FileFingerprintService : IFileFingerprintService
{
    private readonly Func<string, bool>? _existsProbe;

    public FileFingerprintService()
    {
    }

    internal FileFingerprintService(Func<string, bool> existsProbe)
    {
        _existsProbe = existsProbe ?? throw new ArgumentNullException(nameof(existsProbe));
    }

    public FileExistenceEvidence CheckExists(string path, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Unknown(nameof(OperationCanceledException), "Canceled checking file existence");
        }

        try
        {
            return ProbePath(path);
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
            var existence = CheckExists(path, CancellationToken.None);
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
