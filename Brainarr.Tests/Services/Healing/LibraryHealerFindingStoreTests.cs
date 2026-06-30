using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;

namespace Brainarr.Tests.Services.Healing;

public sealed class LibraryHealerFindingStoreTests : IDisposable
{
    private readonly string _tempRoot;

    public LibraryHealerFindingStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "brainarr-healer-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    [Fact]
    public void SaveBatch_ShouldPersistOnlyRedactedPathAndHash()
    {
        var store = CreateStore();
        var finding = CreateFinding(
            id: "finding-one",
            redactedPath: "track01.flac#abcdef123456",
            pathHash: "callerhash001",
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(10).Should().ContainSingle().Subject;
        persisted.Id.Should().Be("finding-one");
        persisted.File.RedactedPath.Should().Be("track01.flac#abcdef123456");
        persisted.File.PathHash.Should().Be("callerhash001");

        var json = File.ReadAllText(StorePath);
        json.Should().Contain("track01.flac#abcdef123456");
        json.Should().Contain("callerhash001");
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"D:\Music");
        json.Should().NotContain(@"D:\\Music");
        json.Should().NotContain("/mnt/music");
    }

    [Fact]
    public void SaveBatch_ShouldDefensivelySanitizeAccidentalRawPathAndEvidenceMessages()
    {
        var store = CreateStore();
        const string callerHash = "preservehash42";
        var rawWindowsPath = @"D:\Music\Private Artist\Album\track01.flac";
        var rawUnixPath = "/mnt/music/Private Artist/Album/track02.flac";
        var finding = CreateFinding(
            id: "raw-path",
            redactedPath: rawWindowsPath,
            pathHash: callerHash,
            tagReaderErrorMessage: "TagLib failed reading " + rawWindowsPath,
            probeErrorMessage: "ffprobe failed opening " + rawUnixPath,
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.File.RedactedPath.Should().StartWith("track01.flac#");
        persisted.File.PathHash.Should().Be(callerHash);
        persisted.TagReader.ErrorMessage.Should().NotContain("Private Artist");
        persisted.TagReader.ErrorMessage.Should().NotContain(@"D:\Music");
        persisted.TagReader.ErrorMessage.Should().NotContain(@"D:\\Music");
        persisted.Probe.Should().NotBeNull();
        persisted.Probe!.ErrorMessage.Should().NotContain("Private Artist");
        persisted.Probe.ErrorMessage.Should().NotContain("/mnt/music");

        var json = File.ReadAllText(StorePath);
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"D:\Music");
        json.Should().NotContain(@"D:\\Music");
        json.Should().NotContain("/mnt/music");
    }

    [Fact]
    public void SaveBatch_ShouldSanitizeUncAndRelativeWindowsPathsInEvidenceMessages()
    {
        var store = CreateStore();
        var rawUncPath = @"\\server\share\Private Artist\Album\track01.flac";
        var rawRelativePath = @"Music\Private Artist\Album\track02.flac";
        var finding = CreateFinding(
            id: "safe-finding-id",
            redactedPath: "track01.flac#abcdef123456",
            pathHash: "callerhash001",
            tagReaderErrorMessage: "TagLib failed reading " + rawUncPath,
            probeErrorMessage: "ffprobe failed opening " + rawRelativePath,
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.TagReader.ErrorMessage.Should().NotContain("Private Artist");
        persisted.TagReader.ErrorMessage.Should().NotContain(@"\\server\share");
        persisted.TagReader.ErrorMessage.Should().NotContain("server");
        persisted.Probe.Should().NotBeNull();
        persisted.Probe!.ErrorMessage.Should().NotContain("Private Artist");
        persisted.Probe.ErrorMessage.Should().NotContain(@"Music\Private Artist");

        var json = File.ReadAllText(StorePath);
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"\\server\share");
        json.Should().NotContain(@"\\\\server\\share");
        json.Should().NotContain(@"Music\Private Artist");
        json.Should().NotContain(@"Music\\Private Artist");
    }

    [Fact]
    public void SaveBatch_ShouldSanitizeShortWindowsAndRelativePosixPathsInEvidenceMessages()
    {
        var store = CreateStore();
        var finding = CreateFinding(
            id: "safe-relative-path-finding",
            redactedPath: "track01.flac#abcdef123456",
            pathHash: "callerhash001",
            tagReaderErrorMessage: @"TagLib failed reading Private Artist\Album\track01.flac and Artist\track01.flac",
            probeErrorMessage: "ffprobe failed opening music/Private Artist/Album/a.flac and ./Private Artist/Album/a.flac",
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.Id.Should().Be("safe-relative-path-finding");
        persisted.File.RedactedPath.Should().Be("track01.flac#abcdef123456");
        persisted.File.PathHash.Should().Be("callerhash001");
        persisted.TagReader.ErrorMessage.Should().NotBeNull();
        AssertNoRelativePrivatePathFragments(persisted.TagReader.ErrorMessage!);
        persisted.Probe.Should().NotBeNull();
        persisted.Probe!.ErrorMessage.Should().NotBeNull();
        AssertNoRelativePrivatePathFragments(persisted.Probe.ErrorMessage!);

        var json = File.ReadAllText(StorePath);
        AssertNoRelativePrivatePathFragments(json);
    }

    [Fact]
    public void SaveBatch_ShouldSanitizePathLikeFindingId()
    {
        var store = CreateStore();
        var rawId = @"D:\Music\Private Artist\Album\track01.flac";
        var expectedId = "finding-" + PathPrivacy.HashPath(rawId);
        var finding = CreateFinding(
            id: rawId,
            redactedPath: "track01.flac#abcdef123456",
            pathHash: "callerhash001",
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.Id.Should().Be(expectedId);
        persisted.Id.Should().NotContain("Private Artist");
        persisted.Id.Should().NotContain(@"\");
        persisted.Id.Should().NotContain("/");
        persisted.Id.Should().NotContain(":");

        var json = File.ReadAllText(StorePath);
        json.Should().Contain(expectedId);
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"D:\Music");
        json.Should().NotContain(@"D:\\Music");
        json.Should().NotContain(@"\Album\");
        json.Should().NotContain(@"\\Album\\");
    }

    [Fact]
    public void GetRecent_ShouldReturnNewestFirstAndRespectLimit()
    {
        var store = CreateStore();
        var start = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);
        var findings = Enumerable.Range(0, 501)
            .Select(i => CreateFinding(
                id: "finding-" + i,
                redactedPath: $"track{i:D3}.flac#hash{i:D3}",
                pathHash: $"hash{i:D3}",
                observedAtUtc: start.AddMinutes(i)))
            .ToList();

        store.SaveBatch(findings);

        store.GetRecent(2).Select(f => f.Id).Should().Equal("finding-500", "finding-499");
        store.GetRecent(0).Select(f => f.Id).Should().Equal("finding-500");
        store.GetRecent(600).Should().HaveCount(500);
    }

    [Fact]
    public void Clear_ShouldRemovePersistedFindings()
    {
        var store = CreateStore();
        store.SaveBatch(new[]
        {
            CreateFinding(
                id: "to-clear",
                redactedPath: "track01.flac#abcdef123456",
                pathHash: "callerhash001",
                observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc)),
        });

        File.Exists(StorePath).Should().BeTrue();

        store.Clear();

        CreateStore().GetRecent(10).Should().BeEmpty();
        File.ReadAllText(StorePath).Should().NotContain("to-clear");
    }

    [Fact]
    public void SaveBatch_ShouldIgnoreNullAndEmptyBatches()
    {
        var store = CreateStore();

        store.SaveBatch(null!);
        store.SaveBatch(Array.Empty<LibraryHealerFinding>());

        File.Exists(StorePath).Should().BeFalse();
        store.GetRecent(10).Should().BeEmpty();
    }

    private string StorePath => Path.Combine(_tempRoot, "library_healer_findings.json");

    private LibraryHealerFindingStore CreateStore()
    {
        return new LibraryHealerFindingStore(_tempRoot);
    }

    private static void AssertNoRelativePrivatePathFragments(string value)
    {
        value.Should().NotContain("Private Artist");
        value.Should().NotContain(@"Private Artist\Album\track01.flac");
        value.Should().NotContain(@"Private Artist\\Album\\track01.flac");
        value.Should().NotContain(@"Artist\track01.flac");
        value.Should().NotContain(@"Artist\\track01.flac");
        value.Should().NotContain("music/Private Artist/Album/a.flac");
        value.Should().NotContain("./Private Artist/Album/a.flac");
        value.Should().NotContain("Private Artist/Album/a.flac");
    }

    private static LibraryHealerFinding CreateFinding(
        string id,
        string redactedPath,
        string pathHash,
        DateTime observedAtUtc,
        string? tagReaderErrorMessage = null,
        string? probeErrorMessage = null)
    {
        return new LibraryHealerFinding(
            id,
            new LibraryHealerFileIdentity(
                TrackFileId: 10,
                ArtistId: 20,
                AlbumId: 30,
                RedactedPath: redactedPath,
                PathHash: pathHash,
                Size: 12345,
                ModifiedUtc: observedAtUtc.AddDays(-1)),
            LibraryHealerLabel.ProbeEvidence,
            new[] { "TAG_READER_ZERO_DURATION", "PROBE_DURATION_POSITIVE" },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: tagReaderErrorMessage is null,
                DurationSeconds: tagReaderErrorMessage is null ? 0 : null,
                ErrorType: tagReaderErrorMessage is null ? null : "ReadError",
                ErrorMessage: tagReaderErrorMessage),
            new ProbeEvidence(
                ProbeAttempted: true,
                ProbeSucceeded: probeErrorMessage is null,
                DurationSeconds: probeErrorMessage is null ? 245.1 : null,
                Container: probeErrorMessage is null ? "flac" : null,
                AudioCodec: probeErrorMessage is null ? "flac" : null,
                ErrorType: probeErrorMessage is null ? null : "ProbeError",
                ErrorMessage: probeErrorMessage),
            observedAtUtc);
    }
}
