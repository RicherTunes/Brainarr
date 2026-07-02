using System.Text.Json;
using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public sealed class LibraryHealerRedactionParityTests : IDisposable
{
    // A bare MusicBrainz id: metadata material, but not a path (no slash/drive/media-ext/whitespace).
    // The API projection has always redacted it; the disk store historically did not -> a divergence.
    private const string RawMbid = "550e8400-e29b-41d4-a716-446655440000";

    private readonly string _tempRoot;

    public LibraryHealerRedactionParityTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "brainarr-healer-parity-" + Guid.NewGuid().ToString("N"));
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
            // best effort
        }
    }

    [Fact]
    public void TokenMaterial_RedactedByApiProjection_IsAlsoRedactedOnDisk()
    {
        var finding = FindingWithErrorTypeToken(RawMbid);

        // Disk surface.
        var store = new LibraryHealerFindingStore(_tempRoot);
        store.SaveBatch(new[] { finding });
        var persisted = new LibraryHealerFindingStore(_tempRoot).GetRecent(1).Should().ContainSingle().Subject;
        var diskJson = File.ReadAllText(Path.Combine(_tempRoot, "library_healer_findings.json"));

        // API surface.
        var handler = new LibraryHealerActionHandler(
            Mock.Of<ILibraryHealerScanRunner>(),
            new SingleFindingStore(finding));
        var apiJson = JsonSerializer.Serialize(handler.Handle("healer/getfindings", new Dictionary<string, string>()));

        // Parity: neither surface may leak the token material.
        persisted.TagReader.ErrorType.Should().NotContain(RawMbid);
        diskJson.Should().NotContain(RawMbid);
        apiJson.Should().NotContain(RawMbid);
    }

    private static LibraryHealerFinding FindingWithErrorTypeToken(string errorTypeToken)
    {
        return new LibraryHealerFinding(
            "parity-finding",
            new LibraryHealerFileIdentity(
                TrackFileId: 10,
                ArtistId: 20,
                AlbumId: 30,
                RedactedPath: "track01.flac#abcdef123456",
                PathHash: "abcdef123456",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_FAILED" },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: false,
                DurationSeconds: null,
                ErrorType: errorTypeToken,
                ErrorMessage: null),
            Probe: null,
            ObservedAtUtc: new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc));
    }

    private sealed class SingleFindingStore : ILibraryHealerFindingStore
    {
        private readonly LibraryHealerFinding _finding;

        public SingleFindingStore(LibraryHealerFinding finding)
        {
            _finding = finding;
        }

        public bool SaveBatch(IReadOnlyList<LibraryHealerFinding> findings) => true;

        public IReadOnlyList<LibraryHealerFinding> GetRecent(int limit) => new[] { _finding };

        public IReadOnlyList<LibraryHealerFinding> GetAllRecent() => new[] { _finding };

        public void Clear()
        {
        }
    }
}
