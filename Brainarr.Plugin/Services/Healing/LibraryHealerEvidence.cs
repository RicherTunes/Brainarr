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
    string? ErrorMessage,
    TagMetadataEvidence? Metadata = null);

public sealed record TagMetadataEvidence(
    bool TitlePresent,
    bool ArtistPresent,
    bool AlbumPresent,
    bool AnyMusicBrainzIdPresent,
    IReadOnlyList<string> MissingFields);

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
    DateTime ObservedAtUtc,
    // Freshness reflects the file's actual on-disk state vs. Lidarr's recorded state at scan time
    // (unchanged -> current, drifted -> stale, gone -> missing, unprobeable -> unknown). It is
    // computed read-only during the scan and persisted so the triage advisor can consume it.
    // Missing legacy fields fail closed as "unknown" rather than silently becoming actionable.
    string EvidenceFreshness = HealerTreatmentVocab.Freshness.Unknown,
    string IdentityFreshness = HealerTreatmentVocab.Freshness.Unknown,
    // Populated only for coalesced storage-root-outage findings: how many per-file findings were
    // collapsed into this one. Null for ordinary per-file findings.
    int? AffectedTrackCount = null);
