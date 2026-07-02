using System.Text.Json;
using System.Text.Json.Nodes;
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
    public void SaveBatch_ShouldReplacePathContainingEvidenceMessagesWithGenericRedaction()
    {
        var store = CreateStore();
        const string safeMessage = "Tag reader failed because metadata duration was empty.";
        const string genericRedaction = "<path-containing message redacted>";
        var start = new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc);
        var findings = new[]
        {
            CreateFinding(
                id: "very-private-relative",
                redactedPath: "track01.flac#abcdef123456",
                pathHash: "callerhash001",
                tagReaderErrorMessage: @"TagLib failed reading Very Private Artist\Album\track01.flac",
                observedAtUtc: start),
            CreateFinding(
                id: "buddy-and-pal-relative",
                redactedPath: "track02.flac#abcdef123457",
                pathHash: "callerhash002",
                tagReaderErrorMessage: @"TagLib failed reading Artist & Friend\Album\track01.flac",
                observedAtUtc: start.AddMinutes(1)),
            CreateFinding(
                id: "unicode-posix-relative",
                redactedPath: "a.flac#abcdef123458",
                pathHash: "callerhash003",
                probeErrorMessage: "ffprobe failed opening música/Très Private Artist/Album/a.flac",
                observedAtUtc: start.AddMinutes(2)),
            CreateFinding(
                id: "parent-posix-relative",
                redactedPath: "a.flac#abcdef123459",
                pathHash: "callerhash004",
                probeErrorMessage: "ffprobe failed opening ../Private Artist/Album/a.flac",
                observedAtUtc: start.AddMinutes(3)),
            CreateFinding(
                id: "safe-message",
                redactedPath: "track03.flac#abcdef123460",
                pathHash: "callerhash005",
                tagReaderErrorMessage: safeMessage,
                observedAtUtc: start.AddMinutes(4)),
        };

        store.SaveBatch(findings);

        var persisted = CreateStore().GetRecent(10);
        persisted.Should().HaveCount(5);
        persisted.Single(finding => finding.Id == "very-private-relative")
            .TagReader.ErrorMessage.Should().Be(genericRedaction);
        persisted.Single(finding => finding.Id == "buddy-and-pal-relative")
            .TagReader.ErrorMessage.Should().Be(genericRedaction);
        persisted.Single(finding => finding.Id == "unicode-posix-relative")
            .Probe!.ErrorMessage.Should().Be(genericRedaction);
        persisted.Single(finding => finding.Id == "parent-posix-relative")
            .Probe!.ErrorMessage.Should().Be(genericRedaction);
        persisted.Single(finding => finding.Id == "safe-message")
            .TagReader.ErrorMessage.Should().Be(safeMessage);
        persisted.Single(finding => finding.Id == "very-private-relative")
            .File.PathHash.Should().Be("callerhash001");

        var json = File.ReadAllText(StorePath);
        json.Should().Contain("path-containing message redacted");
        json.Should().Contain(safeMessage);
        AssertNoGenericRedactionPrivatePathFragments(json);
        foreach (var finding in persisted)
        {
            AssertNoGenericRedactionPrivatePathFragments(finding.TagReader.ErrorMessage);
            AssertNoGenericRedactionPrivatePathFragments(finding.Probe?.ErrorMessage);
        }
    }

    [Fact]
    public void SaveBatch_ShouldSanitizeFilenameLikeValuesInEvidenceMessages()
    {
        var store = CreateStore();
        const string genericRedaction = "<path-containing message redacted>";
        const string rawFilenameLikeValue = "Private Artist - Private Album - track01.flac";
        const string rawDriveLikeValue = "D:Private Artist track02.m4a";
        var finding = CreateFinding(
            id: "safe-message-finding",
            redactedPath: "track01.flac#abcdef123456",
            pathHash: "callerhash001",
            tagReaderErrorMessage: "TagLib failed reading " + rawFilenameLikeValue,
            probeErrorMessage: "ffprobe failed opening " + rawDriveLikeValue,
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.File.RedactedPath.Should().Be("track01.flac#abcdef123456");
        persisted.TagReader.ErrorMessage.Should().Be(genericRedaction);
        persisted.Probe.Should().NotBeNull();
        persisted.Probe!.ErrorMessage.Should().Be(genericRedaction);

        var json = File.ReadAllText(StorePath);
        json.Should().NotContain("Private Artist");
        json.Should().NotContain("Private Album");
        json.Should().NotContain("D:Private");
        json.Should().NotContain("track02.m4a");
    }

    [Fact]
    public void SaveBatch_ShouldSanitizeBareFilenameLikeRedactedPath()
    {
        var store = CreateStore();
        const string rawFilenameLikeValue = "Private Artist - Private Album - track01.flac";
        var finding = CreateFinding(
            id: "safe-path-finding",
            redactedPath: rawFilenameLikeValue,
            pathHash: "callerhash001",
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.File.RedactedPath.Should().Be("track01.flac#" + PathPrivacy.HashPath(rawFilenameLikeValue));

        var json = File.ReadAllText(StorePath);
        json.Should().NotContain("Private Artist");
        json.Should().NotContain("Private Album");
        json.Should().NotContain(rawFilenameLikeValue);
    }

    [Fact]
    public void SaveBatch_ShouldSanitizePathMaterialBeforeExistingDisplayHash()
    {
        var store = CreateStore();
        const string rawDisplayPath = @"D:\Music\Private Artist\Album\track01.flac#abcdef123456";
        var finding = CreateFinding(
            id: "safe-path-finding",
            redactedPath: rawDisplayPath,
            pathHash: "callerhash001",
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.File.RedactedPath.Should().Be("track01.flac#abcdef123456");

        var json = File.ReadAllText(StorePath);
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"D:\Music");
        json.Should().NotContain(@"D:\\Music");
        json.Should().NotContain(@"\Album\");
        json.Should().NotContain(@"\\Album\\");
        json.Should().NotContain(rawDisplayPath);
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
    public void SaveBatch_ShouldSanitizePathLikeValuesInAllPersistedStringFields()
    {
        var store = CreateStore();
        var rawWindowsPath = @"D:\Music\Private Artist\Album\track01.flac";
        var rawUnixPath = "/mnt/music/Private Artist/Album/track02.flac";
        var finding = CreateFinding(
            id: "safe-finding-id",
            redactedPath: "track01.flac#abcdef123456",
            pathHash: rawWindowsPath,
            internalReasonCodes: new[] { "TAG_READER_ZERO_DURATION", rawWindowsPath },
            tagReaderErrorType: rawWindowsPath,
            probeContainer: rawUnixPath,
            probeAudioCodec: rawWindowsPath,
            probeErrorType: rawUnixPath,
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.File.PathHash.Should().Be(PathPrivacy.HashPath(rawWindowsPath));
        persisted.InternalReasonCodes.Should().Contain("TAG_READER_ZERO_DURATION");
        persisted.InternalReasonCodes.Should().OnlyContain(reason => !reason.Contains("Private Artist", StringComparison.Ordinal));
        persisted.TagReader.ErrorType.Should().NotContain("Private Artist");
        persisted.TagReader.ErrorType.Should().NotContain(@"D:\Music");
        persisted.Probe.Should().NotBeNull();
        persisted.Probe!.Container.Should().NotContain("Private Artist");
        persisted.Probe.Container.Should().NotContain("/mnt/music");
        persisted.Probe.AudioCodec.Should().NotContain("Private Artist");
        persisted.Probe.ErrorType.Should().NotContain("Private Artist");
        persisted.Probe.ErrorType.Should().NotContain("/mnt/music");

        var json = File.ReadAllText(StorePath);
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"D:\Music");
        json.Should().NotContain(@"D:\\Music");
        json.Should().NotContain("/mnt/music");
    }

    [Fact]
    public void SaveBatch_ShouldSanitizeFilenameLikeValuesInTokenFields()
    {
        var store = CreateStore();
        const string rawFilenameLikeValue = "Private Artist - Private Album - track01.flac";
        const string rawDriveLikeValue = "D:Private Artist track02.m4a";
        var finding = CreateFinding(
            id: "safe-finding-id",
            redactedPath: "track01.flac#abcdef123456",
            pathHash: rawFilenameLikeValue,
            internalReasonCodes: new[] { "TAG_READER_ZERO_DURATION", rawFilenameLikeValue },
            tagReaderErrorType: rawFilenameLikeValue,
            probeContainer: rawDriveLikeValue,
            probeAudioCodec: rawFilenameLikeValue,
            probeErrorType: rawDriveLikeValue,
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.File.RedactedPath.Should().Be("track01.flac#abcdef123456");
        persisted.File.PathHash.Should().Be(PathPrivacy.HashPath(rawFilenameLikeValue));
        persisted.InternalReasonCodes.Should().Contain("TAG_READER_ZERO_DURATION");
        persisted.TagReader.ErrorType.Should().NotBe(rawFilenameLikeValue);
        persisted.Probe.Should().NotBeNull();
        persisted.Probe!.Container.Should().NotBe(rawDriveLikeValue);
        persisted.Probe.AudioCodec.Should().NotBe(rawFilenameLikeValue);
        persisted.Probe.ErrorType.Should().NotBe(rawDriveLikeValue);

        var json = File.ReadAllText(StorePath);
        json.Should().NotContain("Private Artist");
        json.Should().NotContain("Private Album");
        json.Should().NotContain(rawFilenameLikeValue);
        json.Should().NotContain("D:Private");
        json.Should().NotContain("track02.m4a");
    }

    [Fact]
    public void SaveBatch_ShouldNormalizeFreshnessBeforePersisting()
    {
        var store = CreateStore();
        const string privatePathFreshness = @"D:\Music\Private Artist\track01.flac";
        const string mbidLikeFreshness = "550e8400-e29b-41d4-a716-446655440000";
        var finding = CreateFinding(
            id: "freshness-sanitize",
            redactedPath: "track01.flac#abcdef123456",
            pathHash: "abcdef123456",
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc),
            evidenceFreshness: privatePathFreshness,
            identityFreshness: mbidLikeFreshness);

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.EvidenceFreshness.Should().Be(HealerTreatmentVocab.Freshness.Unknown);
        persisted.IdentityFreshness.Should().Be(HealerTreatmentVocab.Freshness.Unknown);

        var json = File.ReadAllText(StorePath);
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"D:\Music");
        json.Should().NotContain("550e8400-e29b-41d4-a716-446655440000");
    }

    [Fact]
    public void GetRecent_ShouldFailClosed_WhenPersistedStoreEntryHasNoFreshnessFields()
    {
        var value = JsonSerializer.SerializeToNode(CreateFinding(
            id: "legacy-freshness",
            redactedPath: "track01.flac#abcdef123456",
            pathHash: "abcdef123456",
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc),
            evidenceFreshness: HealerTreatmentVocab.Freshness.Current,
            identityFreshness: HealerTreatmentVocab.Freshness.Current))?.AsObject()
            ?? throw new InvalidOperationException("Unable to serialize legacy finding.");
        value.Remove("EvidenceFreshness");
        value.Remove("IdentityFreshness");

        var envelope = new JsonObject
        {
            ["legacy-freshness"] = new JsonObject
            {
                ["value"] = value,
                ["timestamp"] = "2026-06-30T12:00:00Z",
            },
        };
        File.WriteAllText(StorePath, envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;

        persisted.Id.Should().Be("legacy-freshness");
        persisted.EvidenceFreshness.Should().Be(HealerTreatmentVocab.Freshness.Unknown);
        persisted.IdentityFreshness.Should().Be(HealerTreatmentVocab.Freshness.Unknown);
    }

    [Fact]
    public void SaveBatch_ShouldAllowListTagMetadataMissingFields()
    {
        var store = CreateStore();
        var rawMetadataValue = "PrivateArtist";
        var rawMusicBrainzLikeValue = "550e8400-e29b-41d4-a716-446655440000";
        var finding = CreateFinding(
            id: "safe-finding-id",
            redactedPath: "track01.flac#abcdef123456",
            pathHash: "callerhash001",
            tagMetadata: new TagMetadataEvidence(
                TitlePresent: false,
                ArtistPresent: false,
                AlbumPresent: true,
                AnyMusicBrainzIdPresent: false,
                MissingFields: new[]
                {
                    "title",
                    rawMetadataValue,
                    rawMusicBrainzLikeValue,
                    "musicBrainzId",
                }),
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.TagReader.Metadata.Should().NotBeNull();
        persisted.TagReader.Metadata!.MissingFields.Should().Equal("title", "artist", "musicBrainzId");

        var json = File.ReadAllText(StorePath);
        json.Should().Contain("musicBrainzId");
        json.Should().NotContain(rawMetadataValue);
        json.Should().NotContain(rawMusicBrainzLikeValue);
    }

    [Fact]
    public void SaveBatch_ShouldAllowListTagMetadataReasonCodes()
    {
        var store = CreateStore();
        var rawMetadataValue = "PrivateArtist";
        var rawMusicBrainzLikeValue = "550e8400-e29b-41d4-a716-446655440000";
        var finding = CreateFinding(
            id: "safe-finding-id",
            redactedPath: "track01.flac#abcdef123456",
            pathHash: "callerhash001",
            label: LibraryHealerLabel.TagMetadataIssue,
            internalReasonCodes: new[]
            {
                "TAG_READER_DURATION_POSITIVE",
                "TAG_METADATA_MISSING",
                "TAG_MISSING_TITLE",
                "TAG_MISSING_" + rawMetadataValue,
                "TAG_MISSING_" + rawMusicBrainzLikeValue,
                rawMusicBrainzLikeValue,
            },
            tagMetadata: new TagMetadataEvidence(
                TitlePresent: false,
                ArtistPresent: false,
                AlbumPresent: true,
                AnyMusicBrainzIdPresent: false,
                MissingFields: new[]
                {
                    "title",
                    rawMetadataValue,
                    rawMusicBrainzLikeValue,
                    "musicBrainzId",
                }),
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.InternalReasonCodes.Should().Equal(
            "TAG_READER_DURATION_POSITIVE",
            "TAG_METADATA_MISSING",
            "TAG_MISSING_TITLE",
            "TAG_MISSING_ARTIST",
            "TAG_MISSING_MUSICBRAINZID");

        var json = File.ReadAllText(StorePath);
        json.Should().Contain("TAG_MISSING_ARTIST");
        json.Should().Contain("TAG_MISSING_MUSICBRAINZID");
        json.Should().NotContain(rawMetadataValue);
        json.Should().NotContain(rawMusicBrainzLikeValue);
    }

    [Fact]
    public void SaveBatch_ShouldDropStaleTagMetadataReasonCodes_WhenMetadataBooleansAreComplete()
    {
        var store = CreateStore();
        var rawMetadataValue = "PrivateArtist";
        var finding = CreateFinding(
            id: "safe-finding-id",
            redactedPath: "track01.flac#abcdef123456",
            pathHash: "callerhash001",
            label: LibraryHealerLabel.TagMetadataIssue,
            internalReasonCodes: new[]
            {
                "TAG_READER_DURATION_POSITIVE",
                "TAG_METADATA_MISSING",
                "TAG_MISSING_TITLE",
                "TAG_MISSING_" + rawMetadataValue,
            },
            tagMetadata: new TagMetadataEvidence(
                TitlePresent: true,
                ArtistPresent: true,
                AlbumPresent: true,
                AnyMusicBrainzIdPresent: true,
                MissingFields: new[] { "title", rawMetadataValue }),
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.Label.Should().Be(LibraryHealerLabel.FalsePositive);
        persisted.InternalReasonCodes.Should().Equal("TAG_READER_DURATION_POSITIVE");

        var json = File.ReadAllText(StorePath);
        json.Should().Contain("\"label\": 0");
        json.Should().NotContain("\"label\": 5");
        json.Should().Contain("TAG_READER_DURATION_POSITIVE");
        json.Should().NotContain("TAG_METADATA_MISSING");
        json.Should().NotContain("TAG_MISSING_TITLE");
        json.Should().NotContain(rawMetadataValue);
    }

    [Fact]
    public void SaveBatch_ShouldRedactMetadataIdentifiersInErrorMessages()
    {
        var store = CreateStore();
        const string rawMetadataValue = "PrivateArtist";
        const string rawMusicBrainzLikeValue = "550e8400-e29b-41d4-a716-446655440000";
        var finding = CreateFinding(
            id: "safe-finding-id",
            redactedPath: "track01.flac#abcdef123456",
            pathHash: "callerhash001",
            internalReasonCodes: new[] { "TAG_READER_FAILED" },
            tagReaderErrorMessage: $"Artist tag {rawMetadataValue} has MusicBrainz id {rawMusicBrainzLikeValue}",
            label: LibraryHealerLabel.TagReaderSymptom,
            observedAtUtc: new DateTime(2026, 6, 30, 12, 0, 0, DateTimeKind.Utc));

        store.SaveBatch(new[] { finding });

        var persisted = CreateStore().GetRecent(1).Should().ContainSingle().Subject;
        persisted.TagReader.ErrorMessage.Should().NotContain(rawMetadataValue);
        persisted.TagReader.ErrorMessage.Should().NotContain(rawMusicBrainzLikeValue);
        persisted.TagReader.ErrorMessage.Should().NotContain("MusicBrainz id");

        var json = File.ReadAllText(StorePath);
        json.Should().Contain("TAG_READER_FAILED");
        json.Should().NotContain(rawMetadataValue);
        json.Should().NotContain(rawMusicBrainzLikeValue);
        json.Should().NotContain("MusicBrainz id");
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
    public void GetAllRecent_ShouldReturnAllFindingsNewestFirst()
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

        var persisted = store.GetAllRecent();

        persisted.Should().HaveCount(501);
        persisted.Select(f => f.Id).Take(3).Should().Equal("finding-500", "finding-499", "finding-498");
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

    private static void AssertNoGenericRedactionPrivatePathFragments(string? value)
    {
        value.Should().NotContain("Very Private Artist");
        value.Should().NotContain("Very");
        value.Should().NotContain("Artist & Friend");
        value.Should().NotContain("Artist &");
        value.Should().NotContain("Friend");
        value.Should().NotContain("música");
        value.Should().NotContain("Très Private Artist");
        value.Should().NotContain("../Private Artist");
        value.Should().NotContain(@"Very Private Artist\Album");
        value.Should().NotContain(@"Artist & Friend\Album");
        value.Should().NotContain("música/Très Private Artist");
    }

    private static LibraryHealerFinding CreateFinding(
        string id,
        string redactedPath,
        string pathHash,
        DateTime observedAtUtc,
        string? tagReaderErrorMessage = null,
        string? probeErrorMessage = null,
        IReadOnlyList<string>? internalReasonCodes = null,
        string? tagReaderErrorType = null,
        string? probeContainer = null,
        string? probeAudioCodec = null,
        string? probeErrorType = null,
        TagMetadataEvidence? tagMetadata = null,
        LibraryHealerLabel label = LibraryHealerLabel.ProbeEvidence,
        string evidenceFreshness = HealerTreatmentVocab.Freshness.Current,
        string identityFreshness = HealerTreatmentVocab.Freshness.Current)
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
            label,
            internalReasonCodes ?? new[] { "TAG_READER_ZERO_DURATION", "PROBE_DURATION_POSITIVE" },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: tagReaderErrorMessage is null && tagReaderErrorType is null,
                DurationSeconds: tagReaderErrorMessage is null ? 0 : null,
                ErrorType: tagReaderErrorType ?? (tagReaderErrorMessage is null ? null : "ReadError"),
                ErrorMessage: tagReaderErrorMessage,
                Metadata: tagMetadata),
            new ProbeEvidence(
                ProbeAttempted: true,
                ProbeSucceeded: probeErrorMessage is null && probeErrorType is null,
                DurationSeconds: probeErrorMessage is null ? 245.1 : null,
                Container: probeContainer ?? (probeErrorMessage is null ? "flac" : null),
                AudioCodec: probeAudioCodec ?? (probeErrorMessage is null ? "flac" : null),
                ErrorType: probeErrorType ?? (probeErrorMessage is null ? null : "ProbeError"),
                ErrorMessage: probeErrorMessage),
            observedAtUtc,
            EvidenceFreshness: evidenceFreshness,
            IdentityFreshness: identityFreshness);
    }
}
