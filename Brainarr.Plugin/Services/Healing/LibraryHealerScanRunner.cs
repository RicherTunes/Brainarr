using System.Diagnostics;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

public sealed record LibraryHealerScanRequest(
    int? ArtistId = null,
    int? AfterTrackFileId = null,
    int MaxFiles = 100,
    int MaxSeconds = 10);

public sealed record LibraryHealerScanResult(
    LibraryHealerScanStatus Status,
    int TotalArtists,
    int AvailableTrackFiles,
    int ScannedTrackFiles,
    int PersistedFindings,
    bool Truncated,
    int? NextAfterTrackFileId,
    string? ErrorMessage);

public interface ILibraryHealerScanRunner
{
    LibraryHealerScanResult Scan(
        LibraryHealerScanRequest? request = null,
        CancellationToken cancellationToken = default);
}

public sealed class LibraryHealerScanRunner : ILibraryHealerScanRunner
{
    private readonly IArtistService _artistService;
    private readonly IMediaFileService _mediaFileService;
    private readonly ITagLibSymptomReader _tagReader;
    private readonly IFileFingerprintService _fingerprints;
    private readonly ILibraryHealerFindingStore _findingStore;
    private readonly Func<TimeSpan>? _elapsedProvider;

    public LibraryHealerScanRunner(
        IArtistService artistService,
        IMediaFileService mediaFileService,
        ITagLibSymptomReader tagReader,
        IFileFingerprintService fingerprints,
        ILibraryHealerFindingStore findingStore)
        : this(artistService, mediaFileService, tagReader, fingerprints, findingStore, null)
    {
    }

    internal LibraryHealerScanRunner(
        IArtistService artistService,
        IMediaFileService mediaFileService,
        ITagLibSymptomReader tagReader,
        IFileFingerprintService fingerprints,
        ILibraryHealerFindingStore findingStore,
        Func<TimeSpan>? elapsedProvider)
    {
        _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
        _mediaFileService = mediaFileService ?? throw new ArgumentNullException(nameof(mediaFileService));
        _tagReader = tagReader ?? throw new ArgumentNullException(nameof(tagReader));
        _fingerprints = fingerprints ?? throw new ArgumentNullException(nameof(fingerprints));
        _findingStore = findingStore ?? throw new ArgumentNullException(nameof(findingStore));
        _elapsedProvider = elapsedProvider;
    }

    public LibraryHealerScanResult Scan(
        LibraryHealerScanRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var scanRequest = request ?? new LibraryHealerScanRequest();
        var maxFiles = Math.Clamp(scanRequest.MaxFiles, 1, 500);
        var maxSeconds = Math.Clamp(scanRequest.MaxSeconds, 1, 30);
        var maxElapsed = TimeSpan.FromSeconds(maxSeconds);
        var stopwatch = Stopwatch.StartNew();
        TimeSpan Elapsed() => _elapsedProvider?.Invoke() ?? stopwatch.Elapsed;
        var totalArtists = 0;
        var availableTrackFiles = 0;
        var scannedTrackFiles = 0;
        var persistedFindings = 0;
        var findings = new List<LibraryHealerFinding>();
        var pendingFindingsSaveAttempted = false;

        void SavePendingFindings()
        {
            if (findings.Count == 0)
            {
                return;
            }

            pendingFindingsSaveAttempted = true;
            _findingStore.SaveBatch(findings);
            persistedFindings = findings.Count;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var artistIds = GetArtistIds(scanRequest.ArtistId);
            totalArtists = artistIds.Count;
            var targetCandidateCount = scanRequest.ArtistId.HasValue ? maxFiles + 1 : int.MaxValue;
            var candidates = GatherCandidates(
                artistIds,
                scanRequest.AfterTrackFileId,
                targetCandidateCount,
                cancellationToken);

            availableTrackFiles = candidates.Count;
            int? lastScannedTrackFileId = null;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scannedTrackFiles >= maxFiles)
                {
                    break;
                }

                if (scannedTrackFiles > 0 && Elapsed() >= maxElapsed)
                {
                    break;
                }

                LibraryHealerFinding? finding;
                try
                {
                    finding = ScanCandidate(candidate, cancellationToken);
                }
                catch (ReaderBusyException ex)
                {
                    if (findings.Count > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SavePendingFindings();
                    }

                    return new LibraryHealerScanResult(
                        LibraryHealerScanStatus.Failed,
                        totalArtists,
                        availableTrackFiles,
                        scannedTrackFiles,
                        persistedFindings,
                        false,
                        null,
                        PathPrivacy.RedactMessage(ex.Message) ?? ex.GetType().Name);
                }

                scannedTrackFiles++;
                lastScannedTrackFileId = candidate.TrackFile.Id;
                if (finding is not null)
                {
                    findings.Add(finding);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            SavePendingFindings();

            var truncated = scannedTrackFiles < availableTrackFiles;
            return new LibraryHealerScanResult(
                LibraryHealerScanStatus.Completed,
                totalArtists,
                availableTrackFiles,
                scannedTrackFiles,
                persistedFindings,
                truncated,
                truncated ? lastScannedTrackFileId : null,
                null);
        }
        catch (OperationCanceledException)
        {
            if (findings.Count > 0 && !pendingFindingsSaveAttempted)
            {
                try
                {
                    SavePendingFindings();
                }
                catch (OperationCanceledException)
                {
                    // Keep cancellation messages path-safe even if the finding store is the canceling component.
                }
            }

            throw new OperationCanceledException("Library healer scan was canceled");
        }
        catch (Exception ex)
        {
            return new LibraryHealerScanResult(
                LibraryHealerScanStatus.Failed,
                totalArtists,
                availableTrackFiles,
                scannedTrackFiles,
                persistedFindings,
                false,
                null,
                PathPrivacy.RedactMessage(ex.Message) ?? ex.GetType().Name);
        }
    }

    private List<int> GetArtistIds(int? artistId)
    {
        if (artistId.HasValue)
        {
            return new List<int> { artistId.Value };
        }

        return (_artistService.GetAllArtists() ?? new List<Artist>())
            .Select(static artist => artist.Id)
            .ToList();
    }

    private List<Candidate> GatherCandidates(
        IReadOnlyCollection<int> artistIds,
        int? afterTrackFileId,
        int targetCandidateCount,
        CancellationToken cancellationToken)
    {
        var candidates = new List<Candidate>();
        foreach (var artistId in artistIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = _mediaFileService.GetFilesByArtist(artistId) ?? new List<TrackFile>();
            foreach (var trackFile in files
                .Where(static file => file is not null)
                .Where(file => !afterTrackFileId.HasValue || file.Id > afterTrackFileId.Value)
                .OrderBy(static file => file.Id))
            {
                candidates.Add(new Candidate(artistId, trackFile));
                if (candidates.Count >= targetCandidateCount)
                {
                    return candidates
                        .OrderBy(static candidate => candidate.TrackFile.Id)
                        .ToList();
                }
            }
        }

        return candidates
            .OrderBy(static candidate => candidate.TrackFile.Id)
            .ToList();
    }

    private LibraryHealerFinding? ScanCandidate(Candidate candidate, CancellationToken cancellationToken)
    {
        var trackFile = candidate.TrackFile;
        var path = trackFile.Path ?? string.Empty;
        var existence = _fingerprints.CheckExists(path, cancellationToken);
        if (IsCancellationEvidence(existence))
        {
            throw new OperationCanceledException("Canceled checking file existence");
        }

        if (!existence.CheckSucceeded)
        {
            var inconclusiveClassification = LibraryHealerClassifier.ClassifyFileExistence(existence);
            var inconclusivePathHash = PathPrivacy.HashPath(path);
            return new LibraryHealerFinding(
                "track-" + trackFile.Id + "-" + inconclusivePathHash,
                new LibraryHealerFileIdentity(
                    trackFile.Id,
                    candidate.ArtistId,
                    trackFile.AlbumId,
                    PathPrivacy.Redact(path),
                    inconclusivePathHash,
                    trackFile.Size,
                    trackFile.Modified),
                inconclusiveClassification.Label,
                inconclusiveClassification.InternalReasonCodes,
                new TagReaderEvidence(false, false, null, null, null),
                null,
                DateTime.UtcNow);
        }

        if (!existence.Exists)
        {
            var missingPathClassification = LibraryHealerClassifier.ClassifyFileExistence(existence);
            var missingPathHash = PathPrivacy.HashPath(path);
            return new LibraryHealerFinding(
                "track-" + trackFile.Id + "-" + missingPathHash,
                new LibraryHealerFileIdentity(
                    trackFile.Id,
                    candidate.ArtistId,
                    trackFile.AlbumId,
                    PathPrivacy.Redact(path),
                    missingPathHash,
                    trackFile.Size,
                    trackFile.Modified),
                missingPathClassification.Label,
                missingPathClassification.InternalReasonCodes,
                new TagReaderEvidence(false, false, null, null, null),
                null,
                DateTime.UtcNow);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var tagReader = _tagReader.Read(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsCancellationEvidence(tagReader))
        {
            throw new OperationCanceledException("Canceled reading audio tags");
        }

        if (IsReaderBusyEvidence(tagReader))
        {
            throw new ReaderBusyException(tagReader.ErrorMessage ?? "Lidarr audio tag reader is busy");
        }

        var classification = LibraryHealerClassifier.Classify(tagReader, null);
        if (classification.Label == LibraryHealerLabel.FalsePositive)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fingerprint = _fingerprints.Read(path);
        cancellationToken.ThrowIfCancellationRequested();
        var pathHash = PathPrivacy.HashPath(path);
        return new LibraryHealerFinding(
            "track-" + trackFile.Id + "-" + pathHash,
            new LibraryHealerFileIdentity(
                trackFile.Id,
                candidate.ArtistId,
                trackFile.AlbumId,
                PathPrivacy.Redact(path),
                pathHash,
                fingerprint.Size ?? trackFile.Size,
                fingerprint.ModifiedUtc ?? trackFile.Modified),
            classification.Label,
            classification.InternalReasonCodes,
            tagReader,
            null,
            DateTime.UtcNow);
    }

    private static bool IsCancellationEvidence(TagReaderEvidence tagReader)
    {
        return IsCancellationType(tagReader.ErrorType, nameof(OperationCanceledException))
            || IsCancellationType(tagReader.ErrorType, typeof(OperationCanceledException).FullName)
            || IsCancellationType(tagReader.ErrorType, nameof(TaskCanceledException))
            || IsCancellationType(tagReader.ErrorType, typeof(TaskCanceledException).FullName);
    }

    private static bool IsReaderBusyEvidence(TagReaderEvidence tagReader)
    {
        return string.Equals(
            tagReader.ErrorType,
            LidarrAudioTagSymptomReader.ReaderBusyErrorType,
            StringComparison.Ordinal);
    }

    private static bool IsCancellationEvidence(FileExistenceEvidence existence)
    {
        return IsCancellationType(existence.ErrorType, nameof(OperationCanceledException))
            || IsCancellationType(existence.ErrorType, typeof(OperationCanceledException).FullName)
            || IsCancellationType(existence.ErrorType, nameof(TaskCanceledException))
            || IsCancellationType(existence.ErrorType, typeof(TaskCanceledException).FullName);
    }

    private static bool IsCancellationType(string? actual, string? expected)
    {
        return !string.IsNullOrWhiteSpace(expected)
            && string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private sealed record Candidate(int ArtistId, TrackFile TrackFile);

    private sealed class ReaderBusyException : Exception
    {
        public ReaderBusyException(string message)
            : base(message)
        {
        }
    }
}
