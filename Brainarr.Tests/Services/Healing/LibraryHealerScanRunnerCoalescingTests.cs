using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public sealed class LibraryHealerScanRunnerCoalescingTests
{
    private const string OfflineRoot = @"\\offline-nas\Music";

    [Fact]
    public void Scan_ShouldCoalesceInconclusiveFindings_WhenSharedRootIsOffline()
    {
        var store = new CapturingStore();
        var probedRoots = new List<string>();
        var runner = CreateRunner(
            InconclusiveFilesUnderOfflineRoot(4),
            store,
            rootOnlineProbe: root =>
            {
                probedRoots.Add(root);
                return false; // root offline
            });

        runner.Scan();

        var finding = store.Saved.Should().ContainSingle().Subject;
        finding.Id.Should().StartWith("storage-root-");
        finding.Label.Should().Be(LibraryHealerLabel.PathInconsistency);
        finding.InternalReasonCodes.Should().Contain("STORAGE_ROOT_OFFLINE");
        finding.AffectedTrackCount.Should().Be(4);
        finding.File.RedactedPath.Should().NotContain("offline-nas");
        probedRoots.Should().OnlyContain(root => root == OfflineRoot);
    }

    [Fact]
    public void Scan_ShouldPreservePerFileFindings_WhenSharedRootIsHealthy()
    {
        var store = new CapturingStore();
        var runner = CreateRunner(
            MissingFilesUnderOfflineRoot(4),
            store,
            rootOnlineProbe: _ => true); // root healthy

        runner.Scan();

        store.Saved.Should().HaveCount(4);
        store.Saved.Should().OnlyContain(f => !f.Id.StartsWith("storage-root-"));
    }

    [Fact]
    public void Scan_ShouldNotCoalesce_WhenBelowThreshold()
    {
        var store = new CapturingStore();
        var probeCalls = 0;
        var runner = CreateRunner(
            InconclusiveFilesUnderOfflineRoot(2),
            store,
            rootOnlineProbe: _ =>
            {
                probeCalls++;
                return false;
            });

        runner.Scan();

        store.Saved.Should().HaveCount(2);
        store.Saved.Should().OnlyContain(f => !f.Id.StartsWith("storage-root-"));
        probeCalls.Should().Be(0, "roots below the coalesce threshold are never probed");
    }

    [Fact]
    public void Scan_ShouldCoalescePendingFindings_BeforeReaderBusyEarlyExit()
    {
        var store = new CapturingStore();
        var files = InconclusiveFilesUnderOfflineRoot(3);
        files.Add(new TrackFile
        {
            Id = 99,
            Path = @"D:\Healthy\busy.flac",
            Size = 999,
            Modified = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            AlbumId = 10,
        });
        var runner = CreateRunner(
            files,
            store,
            rootOnlineProbe: root => root != OfflineRoot,
            tagReader: new BusyTagReader(),
            fingerprints: new MixedFingerprintService());

        var result = runner.Scan();

        result.Status.Should().Be(LibraryHealerScanStatus.Failed);
        var finding = store.Saved.Should().ContainSingle().Subject;
        finding.Id.Should().StartWith("storage-root-");
        finding.InternalReasonCodes.Should().Contain("STORAGE_ROOT_OFFLINE");
        finding.AffectedTrackCount.Should().Be(3);
    }

    [Fact]
    public void Scan_ShouldTreatPosixRootsAsCaseSensitive_WhenCoalescing()
    {
        var store = new CapturingStore();
        var files = BuildFiles("/mnt/Music", 1, 3)
            .Concat(BuildFiles("/mnt/music", 10, 3))
            .ToList();
        var runner = CreateRunner(
            files,
            store,
            rootOnlineProbe: root => root == "/mnt/music");

        runner.Scan();

        store.Saved.Should().HaveCount(4);
        store.Saved.Should().ContainSingle(f =>
            f.Id.StartsWith("storage-root-") &&
            f.AffectedTrackCount == 3);
        store.Saved.Where(f => !f.Id.StartsWith("storage-root-"))
            .Should().HaveCount(3)
            .And.OnlyContain(f => f.File.RedactedPath.Contains("track", StringComparison.OrdinalIgnoreCase));
    }

    private static List<TrackFile> InconclusiveFilesUnderOfflineRoot(int count)
    {
        return BuildFiles(count);
    }

    private static List<TrackFile> MissingFilesUnderOfflineRoot(int count)
    {
        return BuildFiles(count);
    }

    private static List<TrackFile> BuildFiles(int count)
        => BuildFiles(OfflineRoot, 1, count);

    private static List<TrackFile> BuildFiles(string root, int firstId, int count)
    {
        var files = new List<TrackFile>();
        for (var i = 0; i < count; i++)
        {
            var id = firstId + i;
            var separator = root.Contains('\\', StringComparison.Ordinal) ? @"\" : "/";
            files.Add(new TrackFile
            {
                Id = id,
                Path = root + separator + "Private Artist" + separator + "track" + id + ".flac",
                Size = 100 + id,
                Modified = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                AlbumId = 10,
            });
        }

        return files;
    }

    private static LibraryHealerScanRunner CreateRunner(
        List<TrackFile> files,
        ILibraryHealerFindingStore store,
        Func<string, bool> rootOnlineProbe,
        ITagLibSymptomReader? tagReader = null,
        IFileFingerprintService? fingerprints = null)
    {
        var artistService = new Mock<IArtistService>();
        artistService.Setup(x => x.GetAllArtists()).Returns(new List<Artist> { new() { Id = 1 } });
        var mediaFileService = new Mock<IMediaFileService>();
        mediaFileService.Setup(x => x.GetFilesByArtist(1)).Returns(files);

        return new LibraryHealerScanRunner(
            artistService.Object,
            mediaFileService.Object,
            tagReader ?? new NoopTagReader(),
            fingerprints ?? new OfflineFingerprintService(),
            store,
            elapsedProvider: null,
            rootOnlineProbe: rootOnlineProbe);
    }

    private sealed class OfflineFingerprintService : IFileFingerprintService
    {
        public FileExistenceEvidence CheckExists(string path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            // Simulate an unreachable mount: the per-file existence probe is inconclusive.
            return new FileExistenceEvidence(true, false, false, "PATH_PARENT_UNAVAILABLE", "Parent unavailable");
        }

        public FileFingerprint Read(string path)
        {
            return new FileFingerprint(false, null, null);
        }
    }

    private sealed class NoopTagReader : ITagLibSymptomReader
    {
        public TagReaderEvidence Read(string path, CancellationToken cancellationToken)
        {
            return new TagReaderEvidence(true, true, 0, null, null);
        }
    }

    private sealed class BusyTagReader : ITagLibSymptomReader
    {
        public TagReaderEvidence Read(string path, CancellationToken cancellationToken)
        {
            return new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: false,
                DurationSeconds: null,
                ErrorType: LidarrAudioTagSymptomReader.ReaderBusyErrorType,
                ErrorMessage: "Tag reader is busy");
        }
    }

    private sealed class MixedFingerprintService : IFileFingerprintService
    {
        public FileExistenceEvidence CheckExists(string path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (path.Contains("busy.flac", StringComparison.OrdinalIgnoreCase))
            {
                return new FileExistenceEvidence(true, true, true, null, null);
            }

            return new FileExistenceEvidence(true, false, false, "PATH_PARENT_UNAVAILABLE", "Parent unavailable");
        }

        public FileFingerprint Read(string path)
        {
            return new FileFingerprint(true, 999, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        }
    }

    private sealed class CapturingStore : ILibraryHealerFindingStore
    {
        public List<LibraryHealerFinding> Saved { get; } = new();

        public bool SaveBatch(IReadOnlyList<LibraryHealerFinding> findings)
        {
            Saved.AddRange(findings);
            return true;
        }

        public IReadOnlyList<LibraryHealerFinding> GetRecent(int limit) => Saved.Take(limit).ToList();

        public IReadOnlyList<LibraryHealerFinding> GetAllRecent() => Saved.ToList();

        public void Clear() => Saved.Clear();
    }
}
