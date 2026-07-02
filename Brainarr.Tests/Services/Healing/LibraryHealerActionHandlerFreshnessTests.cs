using System.Text.Json;
using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit;

namespace Brainarr.Tests.Services.Healing;

public sealed class LibraryHealerActionHandlerFreshnessTests
{
    [Fact]
    public void GetFindings_ShouldProjectStoredFreshness_NotHardcodedCurrent()
    {
        var finding = StoredFinding(
            evidenceFreshness: HealerTreatmentVocab.Freshness.Current,
            identityFreshness: HealerTreatmentVocab.Freshness.Stale);

        var plan = ProjectSinglePlan(finding);

        plan.GetProperty("identityFreshness").GetString().Should().Be(HealerTreatmentVocab.Freshness.Stale);
        JsonStrings(plan.GetProperty("blockedReasons"))
            .Should().Contain(HealerTreatmentVocab.BlockedReason.IdentityFreshnessNotCurrent);
        plan.GetProperty("candidateWorkflow").GetString().Should().Be(HealerTreatmentVocab.Workflow.Review);
    }

    [Fact]
    public void GetFindings_ShouldKeepCurrentFreshness_WhenStoredFindingIsCurrent()
    {
        var finding = StoredFinding(
            evidenceFreshness: HealerTreatmentVocab.Freshness.Current,
            identityFreshness: HealerTreatmentVocab.Freshness.Current);

        var plan = ProjectSinglePlan(finding);

        plan.GetProperty("evidenceFreshness").GetString().Should().Be(HealerTreatmentVocab.Freshness.Current);
        plan.GetProperty("identityFreshness").GetString().Should().Be(HealerTreatmentVocab.Freshness.Current);
    }

    private static JsonElement ProjectSinglePlan(LibraryHealerFinding finding)
    {
        var handler = new LibraryHealerActionHandler(
            Mock.Of<ILibraryHealerScanRunner>(),
            new SingleFindingStore(finding));
        var json = JsonSerializer.Serialize(handler.Handle("healer/getfindings", new Dictionary<string, string>()));
        using var document = JsonDocument.Parse(json);
        return document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Single()
            .GetProperty("treatmentPlan")
            .Clone();
    }

    private static LibraryHealerFinding StoredFinding(string evidenceFreshness, string identityFreshness)
    {
        return new LibraryHealerFinding(
            "freshness-finding",
            new LibraryHealerFileIdentity(
                TrackFileId: 10,
                ArtistId: 20,
                AlbumId: 30,
                RedactedPath: "track01.flac#abcdef123456",
                PathHash: "abcdef123456",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagMetadataIssue,
            new[] { "TAG_METADATA_MISSING", "TAG_MISSING_TITLE" },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: true,
                DurationSeconds: 245.2,
                ErrorType: null,
                ErrorMessage: null,
                Metadata: new TagMetadataEvidence(false, true, true, false, new[] { "title" })),
            Probe: null,
            ObservedAtUtc: new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
            EvidenceFreshness: evidenceFreshness,
            IdentityFreshness: identityFreshness);
    }

    private static IReadOnlyList<string> JsonStrings(JsonElement element)
    {
        return element.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
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
