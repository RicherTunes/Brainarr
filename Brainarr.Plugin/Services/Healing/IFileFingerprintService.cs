namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record FileFingerprint(bool Exists, long? Size, DateTime? ModifiedUtc);

public interface IFileFingerprintService
{
    FileFingerprint Read(string path);
}
