namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record FileFingerprint(bool Exists, long? Size, DateTime? ModifiedUtc);

public sealed record FileExistenceEvidence(
    bool CheckAttempted,
    bool CheckSucceeded,
    bool Exists,
    string? ErrorType,
    string? ErrorMessage);

public interface IFileFingerprintService
{
    FileExistenceEvidence CheckExists(string path, CancellationToken cancellationToken);

    FileFingerprint Read(string path);
}
