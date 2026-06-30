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

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var artistIds = GetArtistIds(scanRequest.ArtistId);
            totalArtists = artistIds.Count;
            var candidates = GatherCandidates(artistIds, cancellationToken)
                .Where(candidate => !scanRequest.AfterTrackFileId.HasValue
                    || candidate.TrackFile.Id > scanRequest.AfterTrackFileId.Value)
                .OrderBy(candidate => candidate.TrackFile.Id)
                .ToList();

            availableTrackFiles = candidates.Count;
            var findings = new List<LibraryHealerFinding>();
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

                var finding = ScanCandidate(candidate, cancellationToken);
                scannedTrackFiles++;
                lastScannedTrackFileId = candidate.TrackFile.Id;
                if (finding is not null)
                {
                    findings.Add(finding);
                }
            }

            if (findings.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _findingStore.SaveBatch(findings);
                persistedFindings = findings.Count;
            }

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

    private List<Candidate> GatherCandidates(IReadOnlyCollection<int> artistIds, CancellationToken cancellationToken)
    {
        var candidates = new List<Candidate>();
        foreach (var artistId in artistIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = _mediaFileService.GetFilesByArtist(artistId) ?? new List<TrackFile>();
            foreach (var trackFile in files.Where(static file => file is not null))
            {
                candidates.Add(new Candidate(artistId, trackFile));
            }
        }

        return candidates;
    }

    private LibraryHealerFinding? ScanCandidate(Candidate candidate, CancellationToken cancellationToken)
    {
        var trackFile = candidate.TrackFile;
        var path = trackFile.Path ?? string.Empty;
        var tagReader = _tagReader.Read(path, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsCancellationEvidence(tagReader))
        {
            throw new OperationCanceledException("Canceled reading audio tags");
        }

        var classification = LibraryHealerClassifier.Classify(tagReader, null);
        if (classification.Label == LibraryHealerLabel.FalsePositive)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var fingerprint = _fingerprints.Read(path);
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

    private static bool IsCancellationType(string? actual, string? expected)
    {
        return !string.IsNullOrWhiteSpace(expected)
            && string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private sealed record Candidate(int ArtistId, TrackFile TrackFile);
}
