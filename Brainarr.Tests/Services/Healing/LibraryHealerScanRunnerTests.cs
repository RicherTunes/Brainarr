using System.Text.Json;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Music;

namespace Brainarr.Tests.Services.Healing;

// SavePendingFindings_WhenStoreWriteFails_ReportsZeroPersisted_AndLogs uses the shared
// TestLogger (a real NLog Logger backed by a process-wide MemoryTarget), so this class must be
// serialized against every other test that mutates LogManager.Configuration.
[Collection("LoggingTests")]
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
    public void Scan_ShouldRecordMissingPathWithoutCallingTagReader()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var missingPath = @"D:\Music\Private Artist\missing-track.flac";

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile>
            {
                TrackFile(40, missingPath, 444, new DateTime(2026, 6, 29, 4, 5, 6, DateTimeKind.Utc), albumId: 400),
            });
        fingerprints.Responses[missingPath] = new FileFingerprint(false, null, null);

        var result = CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.AvailableTrackFiles.Should().Be(1);
        result.ScannedTrackFiles.Should().Be(1);
        result.PersistedFindings.Should().Be(1);
        tagReader.Paths.Should().BeEmpty();
        fingerprints.ExistencePaths.Should().Equal(missingPath);
        fingerprints.Paths.Should().BeEmpty();

        var finding = store.AllSaved.Should().ContainSingle().Subject;
        finding.Label.Should().Be(LibraryHealerLabel.PathInconsistency);
        finding.InternalReasonCodes.Should().Contain("FILE_MISSING");
        finding.TagReader.ReadAttempted.Should().BeFalse();
        finding.TagReader.ReadSucceeded.Should().BeFalse();
        finding.File.TrackFileId.Should().Be(40);
        finding.File.ArtistId.Should().Be(1);
        finding.File.AlbumId.Should().Be(400);
        finding.File.RedactedPath.Should().Be(PathPrivacy.Redact(missingPath));
        finding.File.PathHash.Should().Be(PathPrivacy.HashPath(missingPath));
        finding.File.Size.Should().Be(444);
        finding.File.ModifiedUtc.Should().Be(new DateTime(2026, 6, 29, 4, 5, 6, DateTimeKind.Utc));
    }

    [Fact]
    public void Scan_ShouldPersistPositiveDurationFilesWithMissingTagMetadata()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var path = @"D:\Music\Private Artist\untagged.flac";

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(50, path, albumId: 500) });
        tagReader.Responses[path] = new TagReaderEvidence(
            true,
            true,
            245.2,
            null,
            null,
            new TagMetadataEvidence(
                TitlePresent: false,
                ArtistPresent: true,
                AlbumPresent: false,
                AnyMusicBrainzIdPresent: false,
                MissingFields: new[] { "title", "album", "musicBrainzId" }));

        var result = CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.ScannedTrackFiles.Should().Be(1);
        result.PersistedFindings.Should().Be(1);
        fingerprints.Paths.Should().Equal(path);

        var finding = store.AllSaved.Should().ContainSingle().Subject;
        finding.Label.Should().Be(LibraryHealerLabel.TagMetadataIssue);
        finding.InternalReasonCodes.Should().Contain("TAG_METADATA_MISSING");
        finding.TagReader.Metadata.Should().NotBeNull();
        finding.TagReader.Metadata!.MissingFields.Should().Equal("title", "album", "musicBrainzId");
    }

    [Fact]
    public void Scan_ShouldRecordTimedOutPathProbeForHumanReviewWithoutCallingTagReader()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var slowPath = @"\\offline-nas\Music\Private Artist\slow.flac";

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(41, slowPath) });
        fingerprints.ExistenceResponses[slowPath] = new FileExistenceEvidence(
            true,
            false,
            false,
            nameof(TimeoutException),
            @"Timed out checking \\offline-nas\Music\Private Artist\slow.flac");

        var result = CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store)
            .Scan(new LibraryHealerScanRequest(MaxSeconds: 3));

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.AvailableTrackFiles.Should().Be(1);
        result.ScannedTrackFiles.Should().Be(1);
        result.PersistedFindings.Should().Be(1);
        result.ErrorMessage.Should().BeNull();
        tagReader.Paths.Should().BeEmpty();
        fingerprints.Paths.Should().BeEmpty();

        var finding = store.AllSaved.Should().ContainSingle().Subject;
        finding.Label.Should().Be(LibraryHealerLabel.NeedsHumanReview);
        finding.InternalReasonCodes.Should().Contain("PATH_PROBE_INCONCLUSIVE");
        finding.InternalReasonCodes.Should().Contain(nameof(TimeoutException));
        finding.InternalReasonCodes.Should().NotContain("FILE_MISSING");
    }

    [Fact]
    public void Scan_ShouldThrowAndNotPersist_WhenCancellationFiresDuringExistenceCheck()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var slowPath = @"\\offline-nas\Music\Private Artist\slow-cancel.flac";
        using var cancellation = new CancellationTokenSource();

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(42, slowPath) });
        fingerprints.OnCheckExists = () => cancellation.Cancel();
        fingerprints.ExistenceResponses[slowPath] = new FileExistenceEvidence(
            true,
            false,
            false,
            nameof(TimeoutException),
            "Timed out checking file existence");

        var act = () => CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store)
            .Scan(new LibraryHealerScanRequest(MaxSeconds: 1), cancellation.Token);

        act.Should().Throw<OperationCanceledException>()
            .WithMessage("Library healer scan was canceled");
        tagReader.Paths.Should().BeEmpty();
        fingerprints.Paths.Should().BeEmpty();
        fingerprints.ExistenceTokens.Should().ContainSingle().Which.Should().Be(cancellation.Token);
        store.AllSaved.Should().BeEmpty();
    }

    [Fact]
    public void Scan_ShouldThrowAndNotPersist_WhenExternalCancellationPrecedesBudgetButProbeReturnsAfterBudget()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var slowPath = @"\\offline-nas\Music\Private Artist\external-cancel.flac";
        using var cancellation = new CancellationTokenSource();
        var elapsed = TimeSpan.Zero;

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(44, slowPath) });
        fingerprints.OnCheckExists = () =>
        {
            cancellation.Cancel();
            elapsed = TimeSpan.FromSeconds(1);
        };
        fingerprints.ExistenceResponses[slowPath] = new FileExistenceEvidence(
            true,
            false,
            false,
            nameof(OperationCanceledException),
            "Canceled checking file existence");

        var act = () => CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store, () => elapsed)
            .Scan(new LibraryHealerScanRequest(MaxSeconds: 1), cancellation.Token);

        act.Should().Throw<OperationCanceledException>()
            .WithMessage("Library healer scan was canceled");
        tagReader.Paths.Should().BeEmpty();
        fingerprints.Paths.Should().BeEmpty();
        store.AllSaved.Should().BeEmpty();
    }

    [Fact]
    public void Scan_ShouldThrowAndNotPersist_WhenCancellationFiresAfterPathProbeFindingBeforeSave()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var slowPath = @"\\offline-nas\Music\Private Artist\slow-cancel.flac";
        var nextPath = @"D:\Music\Private Artist\next-track.flac";
        using var cancellation = new CancellationTokenSource();
        var checkCount = 0;

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile>
            {
                TrackFile(42, slowPath),
                TrackFile(43, nextPath),
            });
        fingerprints.OnCheckExists = () =>
        {
            if (Interlocked.Increment(ref checkCount) == 1)
            {
                cancellation.Cancel();
            }
        };
        fingerprints.ExistenceResponses[slowPath] = new FileExistenceEvidence(
            true,
            false,
            false,
            nameof(TimeoutException),
            "Timed out checking file existence");
        tagReader.Responses[nextPath] = new TagReaderEvidence(true, true, 0, null, null);

        var act = () => CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store)
            .Scan(new LibraryHealerScanRequest(MaxSeconds: 1), cancellation.Token);

        act.Should().Throw<OperationCanceledException>()
            .WithMessage("Library healer scan was canceled");
        tagReader.Paths.Should().BeEmpty();
        fingerprints.Paths.Should().BeEmpty();
        fingerprints.ExistencePaths.Should().Equal(slowPath);
        store.AllSaved.Should().BeEmpty();
    }

    [Fact]
    public void Scan_ShouldRecordInconclusivePathProbeForHumanReviewWithoutReadingTagsOrFingerprint()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var unknownPath = @"D:\Music\Private Artist\nas-churn.flac";

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile>
            {
                TrackFile(40, unknownPath, 444, new DateTime(2026, 6, 29, 4, 5, 6, DateTimeKind.Utc), albumId: 400),
            });
        fingerprints.ExistenceResponses[unknownPath] = new FileExistenceEvidence(
            true,
            false,
            false,
            "PATH_ACCESS_DENIED",
            "Access denied");

        var result = CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.AvailableTrackFiles.Should().Be(1);
        result.ScannedTrackFiles.Should().Be(1);
        result.PersistedFindings.Should().Be(1);
        tagReader.Paths.Should().BeEmpty();
        fingerprints.ExistencePaths.Should().Equal(unknownPath);
        fingerprints.Paths.Should().BeEmpty();

        var finding = store.AllSaved.Should().ContainSingle().Subject;
        finding.Label.Should().Be(LibraryHealerLabel.NeedsHumanReview);
        finding.InternalReasonCodes.Should().Contain("PATH_PROBE_INCONCLUSIVE");
        finding.InternalReasonCodes.Should().Contain("PATH_ACCESS_DENIED");
        finding.InternalReasonCodes.Should().NotContain("FILE_MISSING");
        finding.TagReader.ReadAttempted.Should().BeFalse();
        finding.File.TrackFileId.Should().Be(40);
        finding.File.Size.Should().Be(444);
        finding.File.ModifiedUtc.Should().Be(new DateTime(2026, 6, 29, 4, 5, 6, DateTimeKind.Utc));
    }

    [Fact]
    public void Scan_ShouldRecordAccessDeniedPathProbeFromRealServiceForHumanReview()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var store = new RecordingFindingStore();
        var accessDeniedPath = @"D:\Music\Private Artist\locked.flac";
        var fingerprints = new FileFingerprintService(_ => throw new UnauthorizedAccessException("Access denied"));

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(40, accessDeniedPath, 444, albumId: 400) });

        var result = CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.ScannedTrackFiles.Should().Be(1);
        result.PersistedFindings.Should().Be(1);
        tagReader.Paths.Should().BeEmpty();

        var finding = store.AllSaved.Should().ContainSingle().Subject;
        finding.Label.Should().Be(LibraryHealerLabel.NeedsHumanReview);
        finding.InternalReasonCodes.Should().Contain("PATH_PROBE_INCONCLUSIVE");
        finding.InternalReasonCodes.Should().Contain("PATH_ACCESS_DENIED");
        finding.InternalReasonCodes.Should().NotContain("FILE_MISSING");
        finding.TagReader.ReadAttempted.Should().BeFalse();
    }

    [Fact]
    public void Scan_ShouldCountConfirmedMissingPathForCursorAndResumeWithNextFile()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var missingPath = @"D:\Music\Private Artist\missing-track.flac";
        var normalPath = @"D:\Music\Private Artist\zero-duration.m4a";

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile>
            {
                TrackFile(40, missingPath, 444, albumId: 400),
                TrackFile(41, normalPath, 555, albumId: 410),
            });
        fingerprints.ExistenceResponses[missingPath] = new FileExistenceEvidence(true, true, false, null, null);
        tagReader.Responses[normalPath] = new TagReaderEvidence(true, true, 0, null, null);

        var first = CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store)
            .Scan(new LibraryHealerScanRequest(MaxFiles: 1));

        first.Status.Should().Be(LibraryHealerScanStatus.Completed);
        first.AvailableTrackFiles.Should().Be(2);
        first.ScannedTrackFiles.Should().Be(1);
        first.PersistedFindings.Should().Be(1);
        first.Truncated.Should().BeTrue();
        first.NextAfterTrackFileId.Should().Be(40);
        tagReader.Paths.Should().BeEmpty();
        fingerprints.ExistencePaths.Should().Equal(missingPath);
        fingerprints.Paths.Should().BeEmpty();
        store.AllSaved.Select(x => x.File.TrackFileId).Should().Equal(40);

        var second = CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store)
            .Scan(new LibraryHealerScanRequest(AfterTrackFileId: 40, MaxFiles: 1));

        second.Status.Should().Be(LibraryHealerScanStatus.Completed);
        second.AvailableTrackFiles.Should().Be(1);
        second.ScannedTrackFiles.Should().Be(1);
        second.PersistedFindings.Should().Be(1);
        second.Truncated.Should().BeFalse();
        second.NextAfterTrackFileId.Should().BeNull();
        tagReader.Paths.Should().Equal(normalPath);
        fingerprints.ExistencePaths.Should().Equal(missingPath, normalPath);
        fingerprints.Paths.Should().Equal(normalPath);
        store.AllSaved.Select(x => x.File.TrackFileId).Should().Equal(40, 41);
    }

    [Fact]
    public void Scan_ShouldNotLeakMissingPathMaterialThroughStoreOrActionOutput()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var tempRoot = Path.Combine(Path.GetTempPath(), "brainarr-healer-missing-boundary-" + Guid.NewGuid().ToString("N"));
        var missingPath = @"D:\Music\Private Artist\Private Album\secret-track.flac";

        try
        {
            Directory.CreateDirectory(tempRoot);
            var store = new LibraryHealerFindingStore(tempRoot);
            artistService.Setup(x => x.GetAllArtists())
                .Returns(new List<Artist> { new() { Id = 1 } });
            mediaFileService.Setup(x => x.GetFilesByArtist(1))
                .Returns(new List<TrackFile>
                {
                    TrackFile(40, missingPath, 444, albumId: 400),
                });
            fingerprints.ExistenceResponses[missingPath] = new FileExistenceEvidence(true, true, false, null, null);

            var scan = CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();
            var actionResult = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store)
                .Handle("healer/getfindings", new Dictionary<string, string>());
            var storeJson = File.ReadAllText(Path.Combine(tempRoot, "library_healer_findings.json"));
            var actionJson = JsonSerializer.Serialize(actionResult);

            scan.PersistedFindings.Should().Be(1);
            storeJson.Should().Contain("FILE_MISSING");
            actionJson.Should().Contain("PathInconsistency");
            AssertNoPrivateMissingPathMaterial(storeJson);
            AssertNoPrivateMissingPathMaterial(actionJson);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void SavePendingFindings_WhenStoreWriteFails_ReportsZeroPersisted_AndLogs()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var path = PathFor(1);
        var tempRoot = Path.Combine(Path.GetTempPath(), "brainarr-healer-save-fail-" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempRoot);

            // Force the real store's underlying write to fail without mocking JsonFileStore:
            // occupy the exact target file path with a directory. JsonFileStore.LoadInitial's
            // File.Exists(...) is false for a directory (safe empty start), but Save()'s atomic
            // File.Replace/File.Move can never land a real file there -- it throws
            // IOException/UnauthorizedAccessException, which LibraryHealerFindingStore now
            // surfaces as a failed SaveBatch instead of silently reporting success.
            var storeFilePath = Path.Combine(tempRoot, "library_healer_findings.json");
            Directory.CreateDirectory(storeFilePath);

            var logger = TestLogger.Create();
            TestLogger.ClearLoggedMessages();
            var store = new LibraryHealerFindingStore(tempRoot, logger);

            artistService.Setup(x => x.GetAllArtists())
                .Returns(new List<Artist> { new() { Id = 1 } });
            mediaFileService.Setup(x => x.GetFilesByArtist(1))
                .Returns(new List<TrackFile> { TrackFile(1, path) });
            tagReader.Responses[path] = new TagReaderEvidence(true, true, 0, null, null);

            var result = CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();

            result.Status.Should().Be(LibraryHealerScanStatus.Completed);
            result.ScannedTrackFiles.Should().Be(1);
            result.PersistedFindings.Should().Be(0, "the write failed, so nothing actually reached disk");

            var logged = TestLogger.GetLoggedMessages();
            logged.Should().Contain(
                line => line.StartsWith("WARN", StringComparison.Ordinal),
                "a failed write must be observable, not silent");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
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
    public void Scan_ShouldStopEnumeratingFilesAfterBoundedLookahead_WhenRequestedArtistMaxFilesReached()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var store = new RecordingFindingStore();
        var firstPath = PathFor(1);
        var lookaheadPath = PathFor(2);

        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile>
            {
                TrackFile(1, firstPath),
                TrackFile(2, lookaheadPath),
            });
        tagReader.Responses[firstPath] = new TagReaderEvidence(true, true, 0, null, null);
        tagReader.Responses[lookaheadPath] = new TagReaderEvidence(true, true, 0, null, null);

        var result = CreateRunner(artistService, mediaFileService, tagReader, new RecordingFingerprintService(), store)
            .Scan(new LibraryHealerScanRequest(ArtistId: 1, MaxFiles: 1));

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.TotalArtists.Should().Be(1);
        result.AvailableTrackFiles.Should().Be(2);
        result.ScannedTrackFiles.Should().Be(1);
        result.PersistedFindings.Should().Be(1);
        result.Truncated.Should().BeTrue();
        result.NextAfterTrackFileId.Should().Be(1);
        tagReader.Paths.Should().Equal(firstPath);
        store.AllSaved.Select(x => x.File.TrackFileId).Should().Equal(1);
        mediaFileService.Verify(x => x.GetFilesByArtist(1), Times.Once);
        mediaFileService.VerifyNoOtherCalls();
        artistService.VerifyNoOtherCalls();
    }

    [Fact]
    public void Scan_ShouldEnumerateAllArtistsBeforeApplyingGlobalCursor()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var store = new RecordingFindingStore();
        var skippedPath = PathFor(1);
        var lowPath = PathFor(60);
        var highPath = PathFor(100);
        var higherPath = PathFor(101);

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 }, new() { Id = 2 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile>
            {
                TrackFile(100, highPath),
                TrackFile(101, higherPath),
            });
        mediaFileService.Setup(x => x.GetFilesByArtist(2))
            .Returns(new List<TrackFile>
            {
                TrackFile(1, skippedPath),
                TrackFile(60, lowPath),
            });
        tagReader.Responses[skippedPath] = new TagReaderEvidence(true, true, 0, null, null);
        tagReader.Responses[lowPath] = new TagReaderEvidence(true, true, 0, null, null);
        tagReader.Responses[highPath] = new TagReaderEvidence(true, true, 0, null, null);
        tagReader.Responses[higherPath] = new TagReaderEvidence(true, true, 0, null, null);

        var result = CreateRunner(artistService, mediaFileService, tagReader, new RecordingFingerprintService(), store)
            .Scan(new LibraryHealerScanRequest(AfterTrackFileId: 50, MaxFiles: 1));

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.TotalArtists.Should().Be(2);
        result.AvailableTrackFiles.Should().Be(3);
        result.ScannedTrackFiles.Should().Be(1);
        result.Truncated.Should().BeTrue();
        result.NextAfterTrackFileId.Should().Be(60);
        tagReader.Paths.Should().Equal(lowPath);
        store.AllSaved.Select(x => x.File.TrackFileId).Should().Equal(60);
        mediaFileService.Verify(x => x.GetFilesByArtist(1), Times.Once);
        mediaFileService.Verify(x => x.GetFilesByArtist(2), Times.Once);
        mediaFileService.VerifyNoOtherCalls();
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

    [Fact]
    public void Scan_ShouldSanitizeThrownCancellationMessages_FromMediaFileService()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var store = new RecordingFindingStore();

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Throws(new OperationCanceledException(@"Canceled scanning D:\Music\Private Artist\track01.m4a"));

        var act = () => CreateRunner(artistService, mediaFileService, tagReader, store: store)
            .Scan();

        var exception = act.Should().Throw<OperationCanceledException>().Which;
        exception.Message.Should().Be("Library healer scan was canceled");
        store.AllSaved.Should().BeEmpty();
    }

    [Fact]
    public void Scan_ShouldSanitizeThrownCancellationMessages_FromTagReader()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var path = PathFor(1);

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(1, path) });
        tagReader.ThrowForPath[path] = new OperationCanceledException(@"Canceled reading D:\Music\Private Artist\track01.m4a");

        var act = () => CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store)
            .Scan();

        var exception = act.Should().Throw<OperationCanceledException>().Which;
        exception.Message.Should().Be("Library healer scan was canceled");
        fingerprints.Paths.Should().BeEmpty();
        store.AllSaved.Should().BeEmpty();
    }

    [Fact]
    public void Scan_ShouldSanitizeStoreCancellationAndNotRetrySave()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore
        {
            ThrowOnSave = new OperationCanceledException(@"Canceled writing D:\Music\Private Artist\track01.m4a"),
        };
        var path = PathFor(1);

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(1, path) });
        tagReader.Responses[path] = new TagReaderEvidence(true, true, 0, null, null);

        var act = () => CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store)
            .Scan();

        var exception = act.Should().Throw<OperationCanceledException>().Which;
        exception.Message.Should().Be("Library healer scan was canceled");
        store.SaveAttempts.Should().Be(1);
    }

    [Fact]
    public void Scan_ShouldThrowAndNotPersist_WhenTagReaderReturnsCancellationEvidence()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var path = PathFor(1);

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(1, path) });
        tagReader.Responses[path] = new TagReaderEvidence(
            true,
            false,
            null,
            nameof(OperationCanceledException),
            @"Canceled reading D:\Music\Private Artist\track01.m4a");

        var act = () => CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store)
            .Scan();

        var exception = act.Should().Throw<OperationCanceledException>().Which;
        exception.Message.Should().NotContain("Private Artist");
        exception.Message.Should().NotContain(@"D:\Music");
        exception.Message.Should().NotContain("track01.m4a");
        fingerprints.Paths.Should().BeEmpty();
        store.AllSaved.Should().BeEmpty();
    }

    [Fact]
    public void Scan_ShouldPersistCompletedFindings_WhenTagReaderReportsBusyAfterPriorTimeout()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var firstPath = PathFor(1);
        var busyPath = PathFor(2);

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile>
            {
                TrackFile(1, firstPath),
                TrackFile(2, busyPath),
            });
        tagReader.Responses[firstPath] = new TagReaderEvidence(true, true, 0, null, null);
        tagReader.Responses[busyPath] = new TagReaderEvidence(
            true,
            false,
            null,
            "TagReaderBusyException",
            "Lidarr audio tag reader is busy with a prior timed-out read");

        var result = CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();

        result.Status.Should().Be(LibraryHealerScanStatus.Failed);
        result.AvailableTrackFiles.Should().Be(2);
        result.ScannedTrackFiles.Should().Be(1);
        result.PersistedFindings.Should().Be(1);
        result.Truncated.Should().BeFalse();
        result.NextAfterTrackFileId.Should().BeNull();
        result.ErrorMessage.Should().Contain("busy");
        fingerprints.Paths.Should().Equal(firstPath);
        store.AllSaved.Select(x => x.File.TrackFileId).Should().Equal(1);
    }

    [Fact]
    public void Scan_ShouldThrowAndNotFingerprint_WhenCancellationIsRequestedAfterTagRead()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var path = PathFor(1);
        using var cancellation = new CancellationTokenSource();

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(1, path) });
        tagReader.Responses[path] = new TagReaderEvidence(true, true, 0, null, null);
        tagReader.AfterRead = () => cancellation.Cancel();

        var act = () => CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store)
            .Scan(cancellationToken: cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
        fingerprints.Paths.Should().BeEmpty();
        store.AllSaved.Should().BeEmpty();
    }

    [Fact]
    public void Scan_ShouldThrowAndNotPersist_WhenCancellationIsRequestedAfterFingerprint()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var path = PathFor(1);
        using var cancellation = new CancellationTokenSource();

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(1, path) });
        tagReader.Responses[path] = new TagReaderEvidence(true, true, 0, null, null);
        fingerprints.AfterRead = () => cancellation.Cancel();

        var act = () => CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store)
            .Scan(cancellationToken: cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
        fingerprints.Paths.Should().Equal(path);
        store.AllSaved.Should().BeEmpty();
    }

    [Fact]
    public void Scan_ShouldScanAlreadyGatheredCandidate_WhenTimeBudgetExpiresBeforeFirstScan()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var elapsed = TimeSpan.Zero;
        var firstPath = PathFor(1);
        var secondPath = PathFor(2);

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 }, new() { Id = 2 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Callback(() => elapsed = TimeSpan.FromSeconds(2))
            .Returns(new List<TrackFile> { TrackFile(2, secondPath) });
        mediaFileService.Setup(x => x.GetFilesByArtist(2))
            .Returns(new List<TrackFile> { TrackFile(1, firstPath) });
        tagReader.Responses[firstPath] = new TagReaderEvidence(true, true, 0, null, null);
        tagReader.Responses[secondPath] = new TagReaderEvidence(true, true, 0, null, null);

        var result = CreateRunner(
                artistService,
                mediaFileService,
                tagReader,
                elapsedProvider: () => elapsed)
            .Scan(new LibraryHealerScanRequest(MaxSeconds: 1));

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.TotalArtists.Should().Be(2);
        result.AvailableTrackFiles.Should().Be(1);
        result.ScannedTrackFiles.Should().Be(1);
        result.Truncated.Should().BeTrue();
        result.NextAfterTrackFileId.Should().BeNull();
        mediaFileService.Verify(x => x.GetFilesByArtist(1), Times.Once);
        mediaFileService.Verify(x => x.GetFilesByArtist(2), Times.Never);
        tagReader.Paths.Should().Equal(secondPath);
    }

    [Fact]
    public void Scan_ShouldStopCandidateGatheringAfterBudgetAndAvoidUnsafeCursor_WhenWholeLibraryEnumerationExceedsBudget()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var store = new RecordingFindingStore();
        var elapsed = TimeSpan.Zero;
        var alreadyGatheredPath = PathFor(200);
        var unseenLowerPath = PathFor(1);

        artistService.Setup(x => x.GetAllArtists())
            .Returns(new List<Artist> { new() { Id = 1 }, new() { Id = 2 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Callback(() => elapsed = TimeSpan.FromSeconds(2))
            .Returns(new List<TrackFile> { TrackFile(200, alreadyGatheredPath) });
        mediaFileService.Setup(x => x.GetFilesByArtist(2))
            .Returns(new List<TrackFile> { TrackFile(1, unseenLowerPath) });
        tagReader.Responses[alreadyGatheredPath] = new TagReaderEvidence(true, true, 0, null, null);
        tagReader.Responses[unseenLowerPath] = new TagReaderEvidence(true, true, 0, null, null);

        var result = CreateRunner(
                artistService,
                mediaFileService,
                tagReader,
                store: store,
                elapsedProvider: () => elapsed)
            .Scan(new LibraryHealerScanRequest(MaxFiles: 1, MaxSeconds: 1));

        result.Status.Should().Be(LibraryHealerScanStatus.Completed);
        result.TotalArtists.Should().Be(2);
        result.AvailableTrackFiles.Should().Be(1);
        result.ScannedTrackFiles.Should().Be(1);
        result.Truncated.Should().BeTrue();
        result.NextAfterTrackFileId.Should().BeNull(
            "a global track-file cursor would skip unseen lower IDs when whole-library enumeration stops early");
        tagReader.Paths.Should().Equal(alreadyGatheredPath);
        mediaFileService.Verify(x => x.GetFilesByArtist(1), Times.Once);
        mediaFileService.Verify(x => x.GetFilesByArtist(2), Times.Never);
    }

    [Fact]
    public void Scan_ShouldRecordCurrentFreshness_WhenProbeMatchesRecordedState()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var path = @"D:\Music\Private Artist\zero.flac";
        var modified = new DateTime(2026, 6, 20, 1, 2, 3, DateTimeKind.Utc);

        artistService.Setup(x => x.GetAllArtists()).Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(1, path, 111, modified) });
        tagReader.Responses[path] = new TagReaderEvidence(true, true, 0, null, null);
        fingerprints.Responses[path] = new FileFingerprint(true, 111, modified);

        CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();

        var finding = store.AllSaved.Should().ContainSingle().Subject;
        finding.EvidenceFreshness.Should().Be(HealerTreatmentVocab.Freshness.Current);
        finding.IdentityFreshness.Should().Be(HealerTreatmentVocab.Freshness.Current);
    }

    [Fact]
    public void Scan_ShouldRecordStaleFreshness_WhenProbeDiffersFromRecordedState()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var path = @"D:\Music\Private Artist\drifted.flac";
        var modified = new DateTime(2026, 6, 20, 1, 2, 3, DateTimeKind.Utc);

        artistService.Setup(x => x.GetAllArtists()).Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(1, path, 111, modified) });
        tagReader.Responses[path] = new TagReaderEvidence(true, true, 0, null, null);
        fingerprints.Responses[path] = new FileFingerprint(true, 999, modified.AddHours(5));

        CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();

        var finding = store.AllSaved.Should().ContainSingle().Subject;
        finding.EvidenceFreshness.Should().Be(HealerTreatmentVocab.Freshness.Stale);
        finding.IdentityFreshness.Should().Be(HealerTreatmentVocab.Freshness.Stale);
    }

    [Fact]
    public void Scan_ShouldRecordMissingFreshness_ForMissingFile()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var path = @"D:\Music\Private Artist\gone.flac";

        artistService.Setup(x => x.GetAllArtists()).Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(1, path, 111) });
        fingerprints.ExistenceResponses[path] = new FileExistenceEvidence(true, true, false, null, null);

        CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();

        var finding = store.AllSaved.Should().ContainSingle().Subject;
        finding.EvidenceFreshness.Should().Be(HealerTreatmentVocab.Freshness.Missing);
        finding.IdentityFreshness.Should().Be(HealerTreatmentVocab.Freshness.Missing);
    }

    [Fact]
    public void Scan_ShouldRecordUnknownFreshness_ForInconclusiveProbe()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var path = @"D:\Music\Private Artist\denied.flac";

        artistService.Setup(x => x.GetAllArtists()).Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(1, path, 111) });
        fingerprints.ExistenceResponses[path] =
            new FileExistenceEvidence(true, false, false, "PATH_ACCESS_DENIED", "Access denied");

        CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();

        var finding = store.AllSaved.Should().ContainSingle().Subject;
        finding.EvidenceFreshness.Should().Be(HealerTreatmentVocab.Freshness.Unknown);
        finding.IdentityFreshness.Should().Be(HealerTreatmentVocab.Freshness.Unknown);
    }

    [Fact]
    public void Scan_ShouldRecordUnknownFreshness_WhenExistsButFingerprintUnreadable()
    {
        var artistService = new Mock<IArtistService>(MockBehavior.Strict);
        var mediaFileService = new Mock<IMediaFileService>(MockBehavior.Strict);
        var tagReader = new RecordingTagReader();
        var fingerprints = new RecordingFingerprintService();
        var store = new RecordingFindingStore();
        var path = @"D:\Music\Private Artist\opaque.flac";

        artistService.Setup(x => x.GetAllArtists()).Returns(new List<Artist> { new() { Id = 1 } });
        mediaFileService.Setup(x => x.GetFilesByArtist(1))
            .Returns(new List<TrackFile> { TrackFile(1, path, 111) });
        tagReader.Responses[path] = new TagReaderEvidence(true, true, 0, null, null);
        fingerprints.Responses[path] = new FileFingerprint(true, null, null);

        CreateRunner(artistService, mediaFileService, tagReader, fingerprints, store).Scan();

        var finding = store.AllSaved.Should().ContainSingle().Subject;
        finding.EvidenceFreshness.Should().Be(HealerTreatmentVocab.Freshness.Unknown);
        finding.IdentityFreshness.Should().Be(HealerTreatmentVocab.Freshness.Unknown);
    }

    private static ILibraryHealerScanRunner CreateRunner(
        Mock<IArtistService> artistService,
        Mock<IMediaFileService> mediaFileService,
        RecordingTagReader tagReader,
        IFileFingerprintService? fingerprints = null,
        ILibraryHealerFindingStore? store = null,
        Func<TimeSpan>? elapsedProvider = null)
    {
        return new LibraryHealerScanRunner(
            artistService.Object,
            mediaFileService.Object,
            tagReader,
            fingerprints ?? new RecordingFingerprintService(),
            store ?? new RecordingFindingStore(),
            elapsedProvider);
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

    private static void AssertNoPrivateMissingPathMaterial(string json)
    {
        json.Should().NotContain(@"D:\Music");
        json.Should().NotContain(@"D:\\Music");
        json.Should().NotContain("Private Artist");
        json.Should().NotContain("Private Album");
        json.Should().NotContain(@"Private Artist\Private Album");
        json.Should().NotContain(@"Private Artist\\Private Album");
    }

    private sealed class RecordingTagReader : ITagLibSymptomReader
    {
        public Dictionary<string, TagReaderEvidence> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Exception> ThrowForPath { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Paths { get; } = new();

        public List<CancellationToken> Tokens { get; } = new();

        public bool ThrowWhenCancellationRequested { get; init; }

        public Action? AfterRead { get; set; }

        public TagReaderEvidence Read(string path, CancellationToken cancellationToken)
        {
            Paths.Add(path);
            Tokens.Add(cancellationToken);
            if (ThrowWhenCancellationRequested)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (ThrowForPath.TryGetValue(path, out var exception))
            {
                throw exception;
            }

            var result = Responses.TryGetValue(path, out var evidence)
                ? evidence
                : new TagReaderEvidence(true, true, 0, null, null);
            AfterRead?.Invoke();
            return result;
        }
    }

    private sealed class RecordingFingerprintService : IFileFingerprintService
    {
        public Dictionary<string, FileFingerprint> Responses { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, FileExistenceEvidence> ExistenceResponses { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> ExistencePaths { get; } = new();

        public List<TimeSpan> ExistenceTimeouts { get; } = new();

        public List<CancellationToken> ExistenceTokens { get; } = new();

        public List<string> Paths { get; } = new();

        public Action? AfterRead { get; set; }

        public Action? OnCheckExists { get; set; }

        public FileExistenceEvidence CheckExists(string path, TimeSpan timeout, CancellationToken cancellationToken)
        {
            ExistencePaths.Add(path);
            ExistenceTimeouts.Add(timeout);
            ExistenceTokens.Add(cancellationToken);
            OnCheckExists?.Invoke();
            if (ExistenceResponses.TryGetValue(path, out var existence))
            {
                return existence;
            }

            var exists = !Responses.TryGetValue(path, out var fingerprint) || fingerprint.Exists;
            return new FileExistenceEvidence(true, true, exists, null, null);
        }

        public FileFingerprint Read(string path)
        {
            Paths.Add(path);
            var result = Responses.TryGetValue(path, out var fingerprint)
                ? fingerprint
                : new FileFingerprint(true, null, null);
            AfterRead?.Invoke();
            return result;
        }
    }

    private sealed class RecordingFindingStore : ILibraryHealerFindingStore
    {
        public List<LibraryHealerFinding> AllSaved { get; } = new();

        public int SaveAttempts { get; private set; }

        public Exception? ThrowOnSave { get; init; }

        public bool SaveSucceeds { get; init; } = true;

        public bool SaveBatch(IReadOnlyList<LibraryHealerFinding> findings)
        {
            SaveAttempts++;
            if (ThrowOnSave is not null)
            {
                throw ThrowOnSave;
            }

            if (!SaveSucceeds)
            {
                return false;
            }

            AllSaved.AddRange(findings);
            return true;
        }

        public IReadOnlyList<LibraryHealerFinding> GetRecent(int limit)
        {
            return AllSaved.Take(limit).ToList();
        }

        public IReadOnlyList<LibraryHealerFinding> GetAllRecent()
        {
            return AllSaved.ToList();
        }

        public void Clear()
        {
            AllSaved.Clear();
        }
    }
}
