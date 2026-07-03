using System.Diagnostics;
using NzbDrone.Core.ImportLists.Brainarr.Utils;
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
    private readonly StorageRootAvailabilityProbe _rootOnlineProbe;

    // Once this many path-unresolved (missing/inconclusive) findings share one storage root, the root
    // itself becomes the more likely explanation than that many independent per-file faults.
    private const int RootCoalesceThreshold = 3;

    // Cap on how many distinct roots we will health-check per scan, so one offline-mount scan cannot
    // fan out into an unbounded number of root probes.
    private const int MaxRootHealthProbes = 8;

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
        Func<TimeSpan>? elapsedProvider,
        Func<string, bool>? rootOnlineProbe = null)
    {
        _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
        _mediaFileService = mediaFileService ?? throw new ArgumentNullException(nameof(mediaFileService));
        _tagReader = tagReader ?? throw new ArgumentNullException(nameof(tagReader));
        _fingerprints = fingerprints ?? throw new ArgumentNullException(nameof(fingerprints));
        _findingStore = findingStore ?? throw new ArgumentNullException(nameof(findingStore));
        _elapsedProvider = elapsedProvider;
        _rootOnlineProbe = new StorageRootAvailabilityProbe(rootOnlineProbe ?? DefaultRootOnlineProbe);
    }

    // Bounded, read-only reachability check for a storage root. A hung or throwing probe (dead mount)
    // is treated as offline so the scan cannot stall and the outage is correctly coalesced.
    private static bool DefaultRootOnlineProbe(string root)
    {
        try
        {
            var probe = Task.Factory.StartNew(
                () => Directory.Exists(root),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            return SafeAsyncHelper.RunSafeSync(() => probe.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        catch (Exception)
        {
            return false;
        }
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
        var rootGroups = new Dictionary<string, RootGroupAccumulator>(StringComparer.Ordinal);

        void SavePendingFindings()
        {
            if (findings.Count == 0)
            {
                return;
            }

            // Trust the store's actual result, not the batch size: a failed write must report
            // zero persisted rather than silently claiming the findings reached disk.
            var saved = _findingStore.SaveBatch(findings);
            persistedFindings = saved ? findings.Count : 0;
        }

        void CoalesceAndSavePendingFindings()
        {
            CoalesceOfflineStorageRoots(findings, rootGroups, cancellationToken);
            SavePendingFindings();
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var artistIds = GetArtistIds(scanRequest.ArtistId);
            totalArtists = artistIds.Count;
            var gatherResult = GatherCandidates(
                artistIds,
                scanRequest.AfterTrackFileId,
                maxFiles + 1,
                scanRequest.ArtistId.HasValue,
                maxElapsed,
                Elapsed,
                cancellationToken);

            availableTrackFiles = gatherResult.AvailableTrackFiles;
            int? lastScannedTrackFileId = null;

            foreach (var candidate in gatherResult.Candidates)
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
                    var existenceTimeout = GetCandidateOperationTimeout(scannedTrackFiles, Elapsed(), maxElapsed);
                    finding = ScanCandidate(candidate, existenceTimeout, cancellationToken);
                }
                catch (ReaderBusyException ex)
                {
                    if (findings.Count > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        CoalesceAndSavePendingFindings();
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
                    TrackStorageRootCandidate(rootGroups, finding, candidate.TrackFile.Path);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            if (findings.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CoalesceAndSavePendingFindings();
            }

            var truncated = gatherResult.BudgetTruncated || scannedTrackFiles < availableTrackFiles;
            return new LibraryHealerScanResult(
                LibraryHealerScanStatus.Completed,
                totalArtists,
                availableTrackFiles,
                scannedTrackFiles,
                persistedFindings,
                truncated,
                truncated && !gatherResult.BudgetTruncated ? lastScannedTrackFileId : null,
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

    private CandidateGatherResult GatherCandidates(
        IReadOnlyCollection<int> artistIds,
        int? afterTrackFileId,
        int targetCandidateCount,
        bool requestedArtistScope,
        TimeSpan maxElapsed,
        Func<TimeSpan> elapsed,
        CancellationToken cancellationToken)
    {
        var candidates = new List<Candidate>();
        var availableTrackFiles = 0;
        var budgetTruncated = false;

        foreach (var artistId in artistIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (availableTrackFiles > 0 && elapsed() >= maxElapsed)
            {
                budgetTruncated = true;
                break;
            }

            var files = _mediaFileService.GetFilesByArtist(artistId) ?? new List<TrackFile>();
            foreach (var trackFile in files
                .Where(static file => file is not null)
                .Where(file => !afterTrackFileId.HasValue || file.Id > afterTrackFileId.Value)
                .OrderBy(static file => file.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                availableTrackFiles++;
                AddBoundedCandidate(candidates, new Candidate(artistId, trackFile), targetCandidateCount);

                if (requestedArtistScope && availableTrackFiles >= targetCandidateCount)
                {
                    return new CandidateGatherResult(
                        OrderCandidates(candidates),
                        availableTrackFiles,
                        false);
                }

                if (elapsed() >= maxElapsed)
                {
                    budgetTruncated = true;
                    break;
                }
            }

            if (budgetTruncated)
            {
                break;
            }
        }

        return new CandidateGatherResult(
            OrderCandidates(candidates),
            availableTrackFiles,
            budgetTruncated);
    }

    private static List<Candidate> OrderCandidates(List<Candidate> candidates)
    {
        return candidates
            .OrderBy(static candidate => candidate.TrackFile.Id)
            .ToList();
    }

    private static void AddBoundedCandidate(List<Candidate> candidates, Candidate candidate, int targetCandidateCount)
    {
        candidates.Add(candidate);
        if (candidates.Count <= targetCandidateCount)
        {
            return;
        }

        var highestIndex = 0;
        var highestId = candidates[0].TrackFile.Id;
        for (var i = 1; i < candidates.Count; i++)
        {
            if (candidates[i].TrackFile.Id <= highestId)
            {
                continue;
            }

            highestId = candidates[i].TrackFile.Id;
            highestIndex = i;
        }

        candidates.RemoveAt(highestIndex);
    }

    private LibraryHealerFinding? ScanCandidate(
        Candidate candidate,
        TimeSpan existenceTimeout,
        CancellationToken cancellationToken)
    {
        var trackFile = candidate.TrackFile;
        var path = trackFile.Path ?? string.Empty;
        var existence = _fingerprints.CheckExists(path, existenceTimeout, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
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
                DateTime.UtcNow,
                // The path could not be probed, so we cannot judge whether the recorded evidence
                // still reflects reality.
                HealerFreshnessEvaluator.Unprobeable.Evidence,
                HealerFreshnessEvaluator.Unprobeable.Identity);
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
                DateTime.UtcNow,
                // The file is confirmed gone at its recorded path.
                HealerFreshnessEvaluator.Gone.Evidence,
                HealerFreshnessEvaluator.Gone.Identity);
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
        // Compare Lidarr's recorded state (DB Size/Modified) against the live on-disk probe to derive
        // real freshness -- read-only, using values the scan already gathered.
        var freshness = HealerFreshnessEvaluator.ForProbedFile(trackFile.Size, trackFile.Modified, fingerprint);
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
            DateTime.UtcNow,
            freshness.Evidence,
            freshness.Identity);
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

    private static TimeSpan GetCandidateOperationTimeout(
        int scannedTrackFiles,
        TimeSpan elapsed,
        TimeSpan maxElapsed)
    {
        var remaining = maxElapsed - elapsed;
        if (remaining > TimeSpan.Zero)
        {
            return remaining;
        }

        return scannedTrackFiles == 0 ? maxElapsed : TimeSpan.Zero;
    }

    private static void TrackStorageRootCandidate(
        Dictionary<string, RootGroupAccumulator> rootGroups,
        LibraryHealerFinding finding,
        string? realPath)
    {
        if (!IsPathUnresolved(finding))
        {
            return;
        }

        var root = HealerStorageRoot.Extract(realPath);
        if (root is null)
        {
            return;
        }

        var key = HealerStorageRoot.Key(root);
        if (!rootGroups.TryGetValue(key, out var group))
        {
            group = new RootGroupAccumulator(root, finding);
            rootGroups[key] = group;
        }

        group.Count++;
        group.FindingIds.Add(finding.Id);
    }

    // A finding is "path-unresolved" when the file could not be confirmed at its recorded path --
    // either confirmed missing or the probe was inconclusive. These are the findings a single offline
    // storage root would produce en masse; healthy-file tag/probe findings are never coalesced.
    private static bool IsPathUnresolved(LibraryHealerFinding finding)
    {
        return finding.InternalReasonCodes is not null
            && finding.InternalReasonCodes.Any(reason =>
                string.Equals(reason, "FILE_MISSING", StringComparison.Ordinal)
                || string.Equals(reason, "PATH_PROBE_INCONCLUSIVE", StringComparison.Ordinal));
    }

    // When many path-unresolved findings share one storage root AND that root is itself
    // offline/unreachable, collapse them into a single "storage root offline" finding instead of
    // burying real issues under thousands of per-file entries. A healthy root leaves its per-file
    // findings untouched. Bounded: at most MaxRootHealthProbes distinct roots are ever probed, one
    // bounded probe each.
    private void CoalesceOfflineStorageRoots(
        List<LibraryHealerFinding> findings,
        Dictionary<string, RootGroupAccumulator> rootGroups,
        CancellationToken cancellationToken)
    {
        if (rootGroups.Count == 0)
        {
            return;
        }

        var probes = 0;
        var idsToRemove = new HashSet<string>(StringComparer.Ordinal);
        var coalesced = new List<LibraryHealerFinding>();

        foreach (var group in rootGroups.Values.OrderByDescending(g => g.Count))
        {
            if (group.Count < RootCoalesceThreshold)
            {
                continue;
            }

            if (probes >= MaxRootHealthProbes)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            probes++;

            bool online;
            try
            {
                online = _rootOnlineProbe.IsOnline(group.RootPath);
            }
            catch (Exception)
            {
                online = false;
            }

            if (online)
            {
                continue;
            }

            foreach (var id in group.FindingIds)
            {
                idsToRemove.Add(id);
            }

            coalesced.Add(BuildCoalescedRootFinding(group));
        }

        if (idsToRemove.Count == 0)
        {
            return;
        }

        findings.RemoveAll(finding => idsToRemove.Contains(finding.Id));
        findings.AddRange(coalesced);
    }

    private static LibraryHealerFinding BuildCoalescedRootFinding(RootGroupAccumulator group)
    {
        var representative = group.Representative;
        var rootHash = PathPrivacy.HashPath(group.RootPath);
        return new LibraryHealerFinding(
            "storage-root-" + rootHash,
            new LibraryHealerFileIdentity(
                representative.File.TrackFileId,
                representative.File.ArtistId,
                representative.File.AlbumId,
                PathPrivacy.Redact(group.RootPath),
                rootHash,
                null,
                null),
            LibraryHealerLabel.PathInconsistency,
            new[] { "PATH_PROBE_INCONCLUSIVE", "STORAGE_ROOT_OFFLINE" },
            new TagReaderEvidence(false, false, null, null, null),
            null,
            DateTime.UtcNow,
            HealerFreshnessEvaluator.Unprobeable.Evidence,
            HealerFreshnessEvaluator.Unprobeable.Identity,
            group.Count);
    }

    private sealed class RootGroupAccumulator
    {
        public RootGroupAccumulator(string rootPath, LibraryHealerFinding representative)
        {
            RootPath = rootPath;
            Representative = representative;
        }

        public string RootPath { get; }

        public LibraryHealerFinding Representative { get; }

        public List<string> FindingIds { get; } = new();

        public int Count { get; set; }
    }

    private sealed record Candidate(int ArtistId, TrackFile TrackFile);

    private sealed record CandidateGatherResult(
        List<Candidate> Candidates,
        int AvailableTrackFiles,
        bool BudgetTruncated);

    private sealed class ReaderBusyException : Exception
    {
        public ReaderBusyException(string message)
            : base(message)
        {
        }
    }
}
