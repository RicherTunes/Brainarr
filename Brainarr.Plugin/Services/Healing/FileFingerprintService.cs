namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed class FileFingerprintService : IFileFingerprintService
{
    public FileFingerprint Read(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
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

    private static FileFingerprint Missing()
    {
        return new FileFingerprint(false, null, null);
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
