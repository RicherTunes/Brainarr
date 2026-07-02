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

    private static List<TrackFile> InconclusiveFilesUnderOfflineRoot(int count)
    {
        return BuildFiles(count);
    }

    private static List<TrackFile> MissingFilesUnderOfflineRoot(int count)
    {
        return BuildFiles(count);
    }

    private static List<TrackFile> BuildFiles(int count)
    {
        var files = new List<TrackFile>();
        for (var i = 1; i <= count; i++)
        {
            files.Add(new TrackFile
            {
                Id = i,
                Path = OfflineRoot + @"\Private Artist\track" + i + ".flac",
                Size = 100 + i,
                Modified = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                AlbumId = 10,
            });
        }

        return files;
    }

    private static LibraryHealerScanRunner CreateRunner(
        List<TrackFile> files,
        ILibraryHealerFindingStore store,
        Func<string, bool> rootOnlineProbe)
    {
        var artistService = new Mock<IArtistService>();
        artistService.Setup(x => x.GetAllArtists()).Returns(new List<Artist> { new() { Id = 1 } });
        var mediaFileService = new Mock<IMediaFileService>();
        mediaFileService.Setup(x => x.GetFilesByArtist(1)).Returns(files);

        return new LibraryHealerScanRunner(
            artistService.Object,
            mediaFileService.Object,
            new NoopTagReader(),
            new OfflineFingerprintService(),
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
