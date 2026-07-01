namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record LibraryHealerFileIdentity(
    int TrackFileId,
    int ArtistId,
    int AlbumId,
    string RedactedPath,
    string PathHash,
    long? Size,
    DateTime? ModifiedUtc);

public sealed record TagReaderEvidence(
    bool ReadAttempted,
    bool ReadSucceeded,
    double? DurationSeconds,
    string? ErrorType,
    string? ErrorMessage);

public sealed record ProbeEvidence(
    bool ProbeAttempted,
    bool ProbeSucceeded,
    double? DurationSeconds,
    string? Container,
    string? AudioCodec,
    string? ErrorType,
    string? ErrorMessage);

public sealed record LibraryHealerFinding(
    string Id,
    LibraryHealerFileIdentity File,
    LibraryHealerLabel Label,
    IReadOnlyList<string> InternalReasonCodes,
    TagReaderEvidence TagReader,
    ProbeEvidence? Probe,
    DateTime ObservedAtUtc);
