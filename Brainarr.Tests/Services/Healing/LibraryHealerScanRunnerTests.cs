using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;

namespace Brainarr.Tests.Services.Healing;

public sealed class LibraryHealerScanRunnerTests
{
    [Fact]
    public void Scan_ShouldEnumerateArtistsAndPersistOnlyNonFalsePositiveFindings()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var firstModified = new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc);
        var fallbackModified = new DateTime(2026, 6, 29, 4, 5, 6, DateTimeKind.Utc);
        var positivePath = @"D:\Music\Private Artist\positive.flac";
        var failedPath = @"D:\Music\Private Artist\failed.m4a";
        var zeroPath = @"D:\Music\Private Artist\zero.m4a";

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 2 }, new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile>
            {
                TrackFile(30, zeroPath, 333, fallbackModified, albumId: 300),
                TrackFile(10, positivePath, 111, fallbackModified, albumId: 100),
            });
        mediaFileService.Setup(x => x.GetFilesByArtist(2))
            .Returns(new List<TrackFile>
            {
                TrackFile(20, failedPath, 222, fallbackModified, albumId: 200),
            });
        tagReader.Responses[positivePath] = new TagReaderEvidence(true, true, 123.4, null, null);
        tagReader.Responses[failedPath] = new TagReaderEvidence(true, false, null, nameof(InvalidDataException), "bad header");
        tagReader.Responses[zeroPath] = new TagReaderEvidence(true, true, 0, null, null);
        fingerprints.Responses[failedPath] = new FileFingerprint(true, 999, firstModified);

        var result = CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.TotalArtists.Should().Be(2);
        result.AvailableTrackFiles.Should().Be(3);
        result.ScannedTrackFiles.Should().Be(3);
        result.PersistedFindings.Should().Be(2);
        result.Truncated.Should().BeFalse();
        result.NextAfterTrackFileId.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
        tagReader.Paths.Should().Equal(positivePath, failedPath, zeroPath);
        store.AllSaved.Select(x => x.Id).Should().Equal(
            "track-20-" + PathPrivacy.HashPath(failedPath),
            "track-30-" + PathPrivacy.HashPath(zeroPath));
        store.AllSaved.Select(x => x.Label).Should().Equal(
            LibraryHealerLabel.NeedsHumanReview,
            LibraryHealerLabel.TagReaderSymptom);

        var failedFinding = store.AllSaved.Single(x => x.File.TrackFileId == 20);
        failedFinding.File.ArtistId.Should().Be(2);
        failedFinding.File.AlbumId.Should().Be(200);
        failedFinding.File.RedactedPath.Should().Be(PathPrivacy.Redact(failedPath));
        failedFinding.File.PathHash.Should().Be(PathPrivacy.HashPath(failedPath));
        failedFinding.File.Size.Should().Be(999);
        failedFinding.File.ModifiedUtc.Should().Be(firstModified);
        failedFinding.Probe.Should().BeNull();

        var zeroFinding = store.AllSaved.Single(x => x.File.TrackFileId == 30);
        zeroFinding.File.Size.Should().Be(333);
        zeroFinding.File.ModifiedUtc.Should().Be(fallbackModified);
        zeroFinding.Probe.Should().BeNull();
        mediaFileService.Verify(x => x.GetFilesByArtist(1), Times.Once);
        mediaFileService.Verify(x => x.GetFilesByArtist(2), Times.Once);
        mediaFileService.VerifyNoOtherCalls();
    }

    [Fact]
    public void Scan_ShouldRespectMaxFilesAndExposeCursor()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var store = new RecordingFindingStore();
        var paths = new[] { PathFor(1), PathFor(2), PathFor(3) };

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile>
            {
                TrackFile(3, paths[2]),
                TrackFile(1, paths[0]),
                TrackFile(2, paths[1]),
            });
        foreach (var path in paths)
        {
            tagReader.Responses[path] = new TagReaderEvidence(true, true, 0, null, null);
        }

        var result = CreateRunner(artistService, mediaFileService, tagReader, new RecordingFingerprintService(), store)
            .Scan(new LibraryHealerScanRequest(MaxFiles: 2));

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.AvailableTrackFiles.Should().Be(3);
        result.ScannedTrackFiles.Should().Be(2);
        result.PersistedFindings.Should().Be(2);
        result.Truncated.Should().BeTrue();
        result.NextAfterTrackFileId.Should().Be(2);
        tagReader.Paths.Should().Equal(paths[0], paths[1]);
        store.AllSaved.Select(x => x.File.TrackFileId).Should().Equal(1, 2);
    }

    [Fact]
    public void Scan_ShouldResumeAfterTrackFileId()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var paths = new[] { PathFor(1), PathFor(2), PathFor(3) };

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile>
            {
                TrackFile(1, paths[0]),
                TrackFile(2, paths[1]),
                TrackFile(3, paths[2]),
            });
        foreach (var path in paths)
        {
            tagReader.Responses[path] = new TagReaderEvidence(true, true, 0, null, null);
        }

        var result = CreateRunner(artistService, mediaFileService, tagReader)
            .Scan(new LibraryHealerScanRequest(AfterTrackFileId: 1));

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.AvailableTrackFiles.Should().Be(2);
        result.ScannedTrackFiles.Should().Be(2);
        result.Truncated.Should().BeFalse();
        result.NextAfterTrackFileId.Should().BeNull();
        tagReader.Paths.Should().Equal(paths[1], paths[2]);
    }

    [Fact]
    public void Scan_ShouldFilterToRequestedArtist()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var requestedPath = PathFor(20);

        mediaFileService.Setup(x => x.GetFilesByArtist(2))
            .Returns(new List<TrackFile> { TrackFile(20, requestedPath) });
        tagReader.Responses[requestedPath] = new TagReaderEvidence(true, true, 0, null, null);

        var result = CreateRunner(artistService, mediaFileService, tagReader)
            .Scan(new LibraryHealerScanRequest(ArtistId: 2));

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.TotalArtists.Should().Be(1);
        result.AvailableTrackFiles.Should().Be(1);
        result.ScannedTrackFiles.Should().Be(1);
        tagReader.Paths.Should().Equal(requestedPath);
        mediaFileService.Verify(x => x.GetFilesByArtist(2), Times.Once);
        mediaFileService.VerifyNoOtherCalls();
        artistService.VerifyNoOtherCalls();
    }

    [Fact]
    public void Scan_ShouldPassCancellationTokenToTagReader()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var path = PathFor(1);
        using var cancellation = new CancellationTokenSource();

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(1, path) });
        tagReader.Responses[path] = new TagReaderEvidence(true, true, 0, null, null);

        CreateRunner(artistService, mediaFileService, tagReader)
            .Scan(cancellationToken: cancellation.Token);

        tagReader.Tokens.Should().ContainSingle().Which.Should().Be(cancellation.Token);
    }

    [Fact]
    public void Scan_ShouldNotMutateMediaFileCanary()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var canaryPath = Path.Combine(Path.GetTempPath(), "brainarr-healer-canary-" + Guid.NewGuid().ToString("N") + ".flac");
        var originalBytes = new byte[] { 0x66, 0x4C, 0x61, 0x43, 0x00, 0x01, 0x02, 0x03 };
        var originalModified = new DateTime(2026, 6, 30, 12, 34, 56, DateTimeKind.Utc);

        try
        {
            File.WriteAllBytes(canaryPath, originalBytes);
            File.SetLastWriteTimeUtc(canaryPath, originalModified);
            var beforeModified = File.GetLastWriteTimeUtc(canaryPath);

            artistService.Setup(x => x.GetAllArtists())
                .Returns(new List<Artist> { new() { Id = 1 } });
            mediaFileService.Setup(x => x.GetFilesByArtist(1))
                .Returns(new List<TrackFile>
                {
                    TrackFile(1, canaryPath, originalBytes.Length, beforeModified),
                });
            tagReader.Responses[canaryPath] = new TagReaderEvidence(true, true, 0, null, null);

            CreateRunner(artistService, mediaFileService, tagReader, new FileFingerprintService())
                .Scan();

            File.ReadAllBytes(canaryPath).Should().Equal(originalBytes);
            File.GetLastWriteTimeUtc(canaryPath).Should().Be(beforeModified);
        }
        finally
        {
            if (File.Exists(canaryPath))
            {
                File.Delete(canaryPath);
            }
        }
    }

    [Fact]
    public void Scan_ShouldReturnFailedWithRedactedError_WhenArtistEnumerationFails()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var store = new RecordingFindingStore();

        artistService.Setup(x => x.GetAllArtists())
            .Throws(new InvalidOperationException(@"failed reading D:\Music\Private Artist\bad.flac"));

        var result = CreateRunner(artistService, mediaFileService, tagReader, store: store).Scan();

        result.Status.Should().Be(LibraryHealerScanStatus.Failed);
        result.TotalArtists.Should().Be(0);
        result.AvailableTrackFiles.Should().Be(0);
        result.ScannedTrackFiles.Should().Be(0);
        result.PersistedFindings.Should().Be(0);
        result.Truncated.Should().BeFalse();
        result.NextAfterTrackFileId.Should().BeNull();
        result.ErrorMessage.Should().NotBeNull();
        result.ErrorMessage.Should().NotContain("Private Artist");
        result.ErrorMessage.Should().NotContain(@"D:\Music");
        tagReader.Paths.Should().BeEmpty();
        store.AllSaved.Should().BeEmpty();
    }

    [Fact]
    public void Scan_ShouldThrow_WhenCancellationRequested()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader { ThrowWhenCancellationRequested = true };
        var path = PathFor(1);
        using var cancellation = new CancellationTokenSource();

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(1, path) });
        cancellation.Cancel();

        var act = () => CreateRunner(artistService, mediaFileService, tagReader)
            .Scan(cancellationToken: cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    private static ILibraryHealerScanRunner CreateRunner(
        Mock<IArtistService> artistService,
        Mock<IMediaFileService> mediaFileService,
        RecordingTagReader tagReader,
        IFileFingerprintService? fingerprints = null,
        RecordingFindingStore? store = null)
    {
        return new LibraryHealerScanRunner(
            artistService.Object,
            mediaFileService.Object,
            tagReader,
            fingerprints ?? new RecordingFingerprintService(),
            store ?? new RecordingFindingStore());
    }

    private static TrackFile TrackFile(
        int id,
        string path,
        long size = 123,
        DateTime? modified = null,
        int albumId = 10)
    {
        return new TrackFile
        {
            Id = id,
            Path = path,
            Size = size,
            Modified = modified ?? new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            AlbumId = albumId,
        };
    }

    private static string PathFor(int id)
    {
        return $@"D:\Music\Private Artist\track{id:D2}.m4a";
    }

    private sealed class RecordingTagReader : ITagLibSymptomReader
    {
        public Dictionary<string, TagReaderEvidence> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Paths { get; } = new();

        public List<CancellationToken> Tokens { get; } = new();

        public bool ThrowWhenCancellationRequested { get; init; }

        public TagReaderEvidence Read(string path, CancellationToken cancellationToken)
        {
            Paths.Add(path);
            Tokens.Add(cancellationToken);
            if (ThrowWhenCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Responses.TryGetValue(path, out var evidence)
                ? evidence
                : new TagReaderEvidence(true, true, 0, null, null);
        }
    }

    private sealed class RecordingFingerprintService : IFileFingerprintService
    {
        public Dictionary<string, FileFingerprint> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);

        public FileFingerprint Read(string path)
        {
            return Responses.TryGetValue(path, out var fingerprint)
                ? fingerprint
                : new FileFingerprint(false, null, null);
        }
    }

    private sealed class RecordingFindingStore : ILibraryHealerFindingStore
    {
        public List<LibraryHealerFinding> AllSaved { get; } = new();

        public void SaveBatch(IReadOnlyList<LibraryHealerFinding> findings)
        {
            AllSaved.AddRange(findings);
        }

        public IReadOnlyList<LibraryHealerFinding> GetRecent(int limit)
        {
            return AllSaved.Take(limit).ToList();
        }

        public void Clear()
        {
            AllSaved.Clear();
        }
    }
}
