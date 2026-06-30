namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed class FileFingerprintService : IFileFingerprintService
{
    public FileFingerprint Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new FileFingerprint(false, null, null);
        }

        var info = new FileInfo(path);
        return new FileFingerprint(true, info.Length, info.LastWriteTimeUtc);
    }
}
