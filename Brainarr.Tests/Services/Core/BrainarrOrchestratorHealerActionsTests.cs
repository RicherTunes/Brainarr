using System.Text.Json;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using NzbDrone.Core.Parser.Model;

namespace Brainarr.Tests.Services.Core;

public sealed class BrainarrOrchestratorHealerActionsTests
{
    [Fact]
    public void HandleAction_ShouldRouteHealerGetFindingsWithoutProviderPipeline()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var store = new FakeFindingStore(new[]
        {
            Finding("track-1-redacted", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
        });
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction(
            "healer/getfindings",
            new Dictionary<string, string> { ["limit"] = "5" },
            new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("\"items\"");
        json.Should().Contain("track-1-redacted");
        json.Should().Contain("TagReaderSymptom");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldRouteHealerGetFieldCatalogWithoutProviderPipeline()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), new FakeFindingStore());
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction(
            "healer/getfieldcatalog",
            new Dictionary<string, string>(),
            new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("\"fields\"");
        json.Should().Contain("items[].path");
        json.Should().Contain("redacted_path_identifier");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldIncludeTreatmentPlan_ForEveryFinding()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var store = new FakeFindingStore(new[]
        {
            Finding("track-1-redacted", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
        });
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction(
            "healer/getfindings",
            new Dictionary<string, string>(),
            new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        var item = document.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        var plan = item.GetProperty("treatmentPlan");
        plan.GetProperty("schemaVersion").GetInt32().Should().Be(HealerTreatmentVocab.SchemaVersion);
        plan.GetProperty("candidateWorkflow").GetString().Should().Be(HealerTreatmentVocab.Workflow.RepairDryRunCandidate);
        plan.GetProperty("confidence").GetDouble().Should().Be(0.55);
        plan.GetProperty("risk").GetString().Should().Be(HealerTreatmentVocab.Risk.Medium);
        plan.GetProperty("safetyLevel").GetString().Should().Be(HealerTreatmentVocab.SafetyLevel.ReadOnly);
        plan.GetProperty("evidenceFreshness").GetString().Should().Be(HealerTreatmentVocab.Freshness.Current);
        plan.GetProperty("identityFreshness").GetString().Should().Be(HealerTreatmentVocab.Freshness.Current);
        var authorization = plan.GetProperty("executionAuthorization");
        authorization.GetProperty("authorized").GetBoolean().Should().BeFalse();
        authorization.GetProperty("authority").GetString().Should().Be(HealerTreatmentVocab.AuthorizationAuthority.None);
        authorization.GetProperty("reason").GetString().Should().Be(HealerTreatmentVocab.AuthorizationReason.A2ReadOnly);
        JsonStrings(plan.GetProperty("blockedReasons")).Should().Contain(HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented);
        JsonStrings(plan.GetProperty("requiredEvidence")).Should().Contain(HealerTreatmentVocab.RequiredEvidence.FullDecodeClean);
        JsonStrings(plan.GetProperty("requiredPolicyGates")).Should().Contain(HealerTreatmentVocab.RequiredPolicyGate.BackupPolicyApproved);
        JsonStrings(plan.GetProperty("rationaleCodes")).Should().Contain(HealerTreatmentVocab.Rationale.TagReaderDurationZero);

        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldReturnTriageSummary_ForReturnedFindings()
    {
        var store = new FakeFindingStore(new[]
        {
            Finding("repair", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
            Finding("review", trackFileId: 2, label: LibraryHealerLabel.NeedsHumanReview),
        });
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);

        var result = handler.Handle("healer/getfindings", new Dictionary<string, string>());
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        var summary = document.RootElement.GetProperty("summary");
        summary.GetProperty("total").GetInt32().Should().Be(2);
        summary.GetProperty("byWorkflow").GetProperty(HealerTreatmentVocab.Workflow.RepairDryRunCandidate).GetInt32().Should().Be(1);
        summary.GetProperty("byWorkflow").GetProperty(HealerTreatmentVocab.Workflow.Review).GetInt32().Should().Be(1);
        summary.GetProperty("byRisk").GetProperty(HealerTreatmentVocab.Risk.Medium).GetInt32().Should().Be(1);
        summary.GetProperty("byRisk").GetProperty(HealerTreatmentVocab.Risk.High).GetInt32().Should().Be(1);
        summary.GetProperty("byWorkflowByRisk")
            .GetProperty(HealerTreatmentVocab.Workflow.RepairDryRunCandidate)
            .GetProperty(HealerTreatmentVocab.Risk.Medium)
            .GetInt32()
            .Should()
            .Be(1);
        summary.GetProperty("authorization").GetProperty("authorized").GetInt32().Should().Be(0);
        summary.GetProperty("authorization").GetProperty("unauthorized").GetInt32().Should().Be(2);
        summary.GetProperty("blockedReasons").GetProperty(HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented).GetInt32().Should().Be(1);
        summary.GetProperty("blockedReasons").GetProperty(HealerTreatmentVocab.BlockedReason.HumanReviewRequired).GetInt32().Should().Be(1);
    }

    [Fact]
    public void HandleAction_ShouldFilterFindingsByTreatmentWorkflowAndSummarizeFilteredItems()
    {
        var store = new FakeFindingStore(new[]
        {
            Finding("repair", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
            Finding("review", trackFileId: 2, label: LibraryHealerLabel.NeedsHumanReview),
            MetadataFinding("tag-repair", trackFileId: 3),
        });
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);

        var result = handler.Handle(
            "healer/getfindings",
            new Dictionary<string, string> { ["workflow"] = HealerTreatmentVocab.Workflow.Review });
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        var item = document.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("id").GetString().Should().Be("review");
        item.GetProperty("treatmentPlan").GetProperty("candidateWorkflow").GetString().Should().Be(HealerTreatmentVocab.Workflow.Review);
        var summary = document.RootElement.GetProperty("summary");
        summary.GetProperty("total").GetInt32().Should().Be(1);
        summary.GetProperty("byWorkflow").GetProperty(HealerTreatmentVocab.Workflow.Review).GetInt32().Should().Be(1);
        summary.GetProperty("byWorkflow").TryGetProperty(HealerTreatmentVocab.Workflow.RepairDryRunCandidate, out _).Should().BeFalse();
        summary.GetProperty("byRisk").GetProperty(HealerTreatmentVocab.Risk.High).GetInt32().Should().Be(1);
        summary.GetProperty("authorization").GetProperty("unauthorized").GetInt32().Should().Be(1);
        summary.GetProperty("blockedReasons").GetProperty(HealerTreatmentVocab.BlockedReason.HumanReviewRequired).GetInt32().Should().Be(1);
    }

    [Fact]
    public void HandleAction_ShouldComposeRiskBlockedReasonAndAuthorizationFilters()
    {
        var store = new FakeFindingStore(new[]
        {
            Finding("repair", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
            Finding("review", trackFileId: 2, label: LibraryHealerLabel.NeedsHumanReview),
            MetadataFinding("tag-repair", trackFileId: 3),
        });
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);

        var result = handler.Handle(
            "healer/getfindings",
            new Dictionary<string, string>
            {
                ["risk"] = "MeDiUm",
                ["blockedReason"] = HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented.ToLowerInvariant(),
                ["authorized"] = "FALSE",
            });
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        var item = document.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("id").GetString().Should().Be("repair");
        var summary = document.RootElement.GetProperty("summary");
        summary.GetProperty("total").GetInt32().Should().Be(1);
        summary.GetProperty("byWorkflow").GetProperty(HealerTreatmentVocab.Workflow.RepairDryRunCandidate).GetInt32().Should().Be(1);
        summary.GetProperty("byRisk").GetProperty(HealerTreatmentVocab.Risk.Medium).GetInt32().Should().Be(1);
        summary.GetProperty("blockedReasons").GetProperty(HealerTreatmentVocab.BlockedReason.RepairDryRunNotImplemented).GetInt32().Should().Be(1);
        summary.GetProperty("authorization").GetProperty("authorized").GetInt32().Should().Be(0);
        summary.GetProperty("authorization").GetProperty("unauthorized").GetInt32().Should().Be(1);
    }

    [Fact]
    public void HandleAction_ShouldSupportCommaSeparatedWorkflowAndBlockedReasonFilters()
    {
        var store = new FakeFindingStore(new[]
        {
            Finding("repair", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
            Finding("review", trackFileId: 2, label: LibraryHealerLabel.NeedsHumanReview),
            MetadataFinding("tag-repair", trackFileId: 3),
        });
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);

        var result = handler.Handle(
            "healer/getfindings",
            new Dictionary<string, string>
            {
                ["workflow"] = string.Join(",", HealerTreatmentVocab.Workflow.Review, HealerTreatmentVocab.Workflow.TagRepairCandidate),
                ["blockedReason"] = string.Join(",", HealerTreatmentVocab.BlockedReason.HumanReviewRequired, HealerTreatmentVocab.BlockedReason.TagRepairNotImplemented),
            });
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        var ids = document.RootElement.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .ToArray();
        ids.Should().BeEquivalentTo(new[] { "review", "tag-repair" });
        document.RootElement.GetProperty("summary").GetProperty("total").GetInt32().Should().Be(2);
    }

    [Fact]
    public void HandleAction_ShouldApplyTreatmentFiltersBeforeLimit()
    {
        var store = new FakeFindingStore(new[]
        {
            Finding("repair", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
            Finding("review", trackFileId: 2, label: LibraryHealerLabel.NeedsHumanReview),
        });
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);

        var result = handler.Handle(
            "healer/getfindings",
            new Dictionary<string, string>
            {
                ["workflow"] = HealerTreatmentVocab.Workflow.Review,
                ["limit"] = "1",
            });
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        var item = document.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("id").GetString().Should().Be("review");
        document.RootElement.GetProperty("summary").GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public void HandleAction_ShouldIgnoreUnknownTreatmentFilterValuesWithoutHidingFindings()
    {
        var store = new FakeFindingStore(new[]
        {
            Finding("repair", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
            Finding("review", trackFileId: 2, label: LibraryHealerLabel.NeedsHumanReview),
        });
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);

        var result = handler.Handle(
            "healer/getfindings",
            new Dictionary<string, string>
            {
                ["workflow"] = "repair-dry-run-candidate",
                ["risk"] = "medum",
                ["blockedReason"] = "not-a-known-blocked-reason",
            });
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("items").EnumerateArray().Should().HaveCount(2);
        document.RootElement.GetProperty("summary").GetProperty("total").GetInt32().Should().Be(2);
    }

    [Fact]
    public void HandleAction_ShouldReturnEmptySummary_WhenAuthorizedFilterHasNoMatches()
    {
        var store = new FakeFindingStore(new[]
        {
            Finding("repair", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
            Finding("review", trackFileId: 2, label: LibraryHealerLabel.NeedsHumanReview),
        });
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);

        var result = handler.Handle(
            "healer/getfindings",
            new Dictionary<string, string> { ["authorized"] = "true" });
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("items").EnumerateArray().Should().BeEmpty();
        var summary = document.RootElement.GetProperty("summary");
        summary.GetProperty("total").GetInt32().Should().Be(0);
        summary.GetProperty("authorization").GetProperty("authorized").GetInt32().Should().Be(0);
        summary.GetProperty("authorization").GetProperty("unauthorized").GetInt32().Should().Be(0);
    }

    [Fact]
    public void HandleAction_ShouldIgnoreMalformedAuthorizedFilterWithoutHidingFindings()
    {
        var store = new FakeFindingStore(new[]
        {
            Finding("repair", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
            Finding("review", trackFileId: 2, label: LibraryHealerLabel.NeedsHumanReview),
        });
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);

        var result = handler.Handle(
            "healer/getfindings",
            new Dictionary<string, string> { ["authorized"] = "not-a-bool" });
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("items").EnumerateArray().Should().HaveCount(2);
        document.RootElement.GetProperty("summary").GetProperty("total").GetInt32().Should().Be(2);
    }

    [Fact]
    public void HandleAction_ShouldFailClosed_WhenStoredFindingShapeIsMalformed()
    {
        var malformed = new LibraryHealerFinding(
            "malformed",
            File: null!,
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_ZERO_DURATION" },
            TagReader: null!,
            Probe: null,
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(
            Mock.Of<ILibraryHealerScanRunner>(),
            new FakeFindingStore(new[] { malformed }));

        var result = handler.Handle("healer/getfindings", new Dictionary<string, string>());
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        var item = document.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("trackFileId").GetInt32().Should().Be(0);
        item.GetProperty("label").GetString().Should().Be(LibraryHealerLabel.NeedsHumanReview.ToString());
        JsonStrings(item.GetProperty("reasons")).Should().Contain(HealerTreatmentVocab.BlockedReason.MalformedFindingRecord);
        var plan = item.GetProperty("treatmentPlan");
        plan.GetProperty("candidateWorkflow").GetString().Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.GetProperty("risk").GetString().Should().Be(HealerTreatmentVocab.Risk.High);
        plan.GetProperty("executionAuthorization").GetProperty("authorized").GetBoolean().Should().BeFalse();
        JsonStrings(plan.GetProperty("blockedReasons")).Should().Contain(HealerTreatmentVocab.BlockedReason.MalformedFindingRecord);
        document.RootElement.GetProperty("summary")
            .GetProperty("blockedReasons")
            .GetProperty(HealerTreatmentVocab.BlockedReason.MalformedFindingRecord)
            .GetInt32()
            .Should()
            .Be(1);
    }

    [Fact]
    public void HandleAction_ShouldFailClosed_WhenStoredFindingIdentityShapeIsInvalid()
    {
        var malformed = new LibraryHealerFinding(
            "malformed",
            new LibraryHealerFileIdentity(
                TrackFileId: 0,
                ArtistId: 0,
                AlbumId: 0,
                RedactedPath: "track01.flac#abcdef123456",
                PathHash: "abcdef123456",
                Size: -1,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_ZERO_DURATION" },
            new TagReaderEvidence(true, true, 0, null, null),
            Probe: null,
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(
            Mock.Of<ILibraryHealerScanRunner>(),
            new FakeFindingStore(new[] { malformed }));

        var result = handler.Handle("healer/getfindings", new Dictionary<string, string>());
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        var item = document.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("trackFileId").GetInt32().Should().Be(0);
        item.GetProperty("label").GetString().Should().Be(LibraryHealerLabel.NeedsHumanReview.ToString());
        JsonStrings(item.GetProperty("reasons")).Should().Contain(HealerTreatmentVocab.BlockedReason.MalformedFindingRecord);
        var plan = item.GetProperty("treatmentPlan");
        plan.GetProperty("candidateWorkflow").GetString().Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.GetProperty("risk").GetString().Should().Be(HealerTreatmentVocab.Risk.High);
        JsonStrings(plan.GetProperty("blockedReasons")).Should().Contain(HealerTreatmentVocab.BlockedReason.MalformedFindingRecord);
    }

    [Fact]
    public void HandleAction_ShouldFailClosed_WhenTagMetadataIssueHasNullMetadata()
    {
        // A TagMetadataIssue finding whose TagReader.Metadata is null (tainted / hand-edited / a future
        // write path that omits it) must NOT be silently downgraded to FalsePositive. GetMissingFields(null)
        // returns empty, which NormalizeLabel would treat as "all tags present" and reclassify to
        // FalsePositive (confidence 1.0, workflow none) — a silent false-negative that defeats the
        // tainted-input guarantee in docs/library-healer.md. It must surface as NeedsHumanReview +
        // MALFORMED_FINDING_RECORD.
        var malformed = new LibraryHealerFinding(
            "null-metadata",
            new LibraryHealerFileIdentity(
                TrackFileId: 42,
                ArtistId: 7,
                AlbumId: 3,
                RedactedPath: "track01.flac#abcdef123456",
                PathHash: "abcdef123456",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagMetadataIssue,
            new[] { "TAG_METADATA_MISSING", "TAG_MISSING_TITLE" },
            new TagReaderEvidence(true, true, 1.0, null, null), // Metadata defaults to null
            Probe: null,
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(
            Mock.Of<ILibraryHealerScanRunner>(),
            new FakeFindingStore(new[] { malformed }));

        var result = handler.Handle("healer/getfindings", new Dictionary<string, string>());
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        var item = document.RootElement.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("label").GetString().Should().Be(LibraryHealerLabel.NeedsHumanReview.ToString());
        JsonStrings(item.GetProperty("reasons")).Should().Contain(HealerTreatmentVocab.BlockedReason.MalformedFindingRecord);
        var plan = item.GetProperty("treatmentPlan");
        plan.GetProperty("candidateWorkflow").GetString().Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.GetProperty("executionAuthorization").GetProperty("authorized").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void HandleAction_ShouldRedactSensitiveTokensAndGenericExceptionTextInTreatmentProjection()
    {
        const string rawMusicBrainzLikeValue = "550e8400-e29b-41d4-a716-446655440000";
        const string genericExceptionText = "bad header near private frame";
        var finding = new LibraryHealerFinding(
            "finding-" + rawMusicBrainzLikeValue,
            new LibraryHealerFileIdentity(
                TrackFileId: 99,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: "track01.flac#abcdef123456",
                PathHash: rawMusicBrainzLikeValue,
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_FAILED" },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: false,
                DurationSeconds: null,
                ErrorType: rawMusicBrainzLikeValue,
                ErrorMessage: genericExceptionText),
            new ProbeEvidence(
                ProbeAttempted: true,
                ProbeSucceeded: false,
                DurationSeconds: null,
                Container: rawMusicBrainzLikeValue,
                AudioCodec: rawMusicBrainzLikeValue,
                ErrorType: rawMusicBrainzLikeValue,
                ErrorMessage: genericExceptionText),
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(
            Mock.Of<ILibraryHealerScanRunner>(),
            new FakeFindingStore(new[] { finding }));

        var result = handler.Handle("healer/getfindings", new Dictionary<string, string>());
        var json = JsonSerializer.Serialize(result);

        json.Should().NotContain(rawMusicBrainzLikeValue);
        json.Should().NotContain(genericExceptionText);
        json.Should().NotContain("bad header");
        json.Should().Contain("TAG_READER_FAILED");
        json.Should().Contain(HealerTreatmentVocab.Rationale.TagReaderReadFailed);
    }

    [Fact]
    public void HandleAction_ShouldRedactSingleTokenMetadataAndCommandLikeValuesInStoredProjection()
    {
        const string rawMetadataValue = "PrivateArtist";
        const string rawCommandValue = "ffmpeg";
        var finding = new LibraryHealerFinding(
            "finding-" + rawMetadataValue,
            new LibraryHealerFileIdentity(
                TrackFileId: 99,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: rawMetadataValue,
                PathHash: rawCommandValue,
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_FAILED" },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: false,
                DurationSeconds: null,
                ErrorType: rawMetadataValue,
                ErrorMessage: "failed"),
            new ProbeEvidence(
                ProbeAttempted: true,
                ProbeSucceeded: false,
                DurationSeconds: null,
                Container: rawMetadataValue,
                AudioCodec: rawCommandValue,
                ErrorType: rawCommandValue,
                ErrorMessage: "failed"),
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(
            Mock.Of<ILibraryHealerScanRunner>(),
            new FakeFindingStore(new[] { finding }));

        var result = handler.Handle("healer/getfindings", new Dictionary<string, string>());
        var json = JsonSerializer.Serialize(result);

        json.Should().NotContain(rawMetadataValue);
        json.Should().NotContain(rawCommandValue);
        json.Should().Contain("TAG_READER_FAILED");
        json.Should().Contain(HealerTreatmentVocab.Rationale.TagReaderReadFailed);
    }

    [Fact]
    public void HandleAction_ShouldRouteHealerScanWithBoundedQueryWithoutProviderPipeline()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var scanRunner = new Mock<ILibraryHealerScanRunner>(MockBehavior.Strict);
        LibraryHealerScanRequest? captured = null;
        CancellationToken capturedToken = default;
        scanRunner.Setup(x => x.Scan(It.IsAny<LibraryHealerScanRequest?>(), It.IsAny<CancellationToken>()))
            .Callback<LibraryHealerScanRequest?, CancellationToken>((request, token) =>
            {
                captured = request;
                capturedToken = token;
            })
            .Returns(new LibraryHealerScanResult(
                LibraryHealerScanStatus.Completed,
                TotalArtists: 1,
                AvailableTrackFiles: 10,
                ScannedTrackFiles: 5,
                PersistedFindings: 2,
                Truncated: true,
                NextAfterTrackFileId: 42,
                ErrorMessage: null));
        var handler = new LibraryHealerActionHandler(scanRunner.Object, new FakeFindingStore());
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction(
            "healer/scan",
            new Dictionary<string, string>
            {
                ["artistId"] = "7",
                ["afterTrackFileId"] = "11",
                ["maxFiles"] = "999",
                ["maxSeconds"] = "45",
            },
            new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        captured.Should().NotBeNull();
        captured!.ArtistId.Should().Be(7);
        captured.AfterTrackFileId.Should().Be(11);
        captured.MaxFiles.Should().Be(500);
        captured.MaxSeconds.Should().Be(30);
        capturedToken.CanBeCanceled.Should().BeFalse("maxSeconds is a cooperative scan budget, not an action-level timeout");
        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"nextAfterTrackFileId\":42");
        scanRunner.VerifyAll();
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldIgnoreNonPositiveHealerScanIds()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var scanRunner = new Mock<ILibraryHealerScanRunner>(MockBehavior.Strict);
        LibraryHealerScanRequest? captured = null;
        scanRunner.Setup(x => x.Scan(It.IsAny<LibraryHealerScanRequest?>(), It.IsAny<CancellationToken>()))
            .Callback<LibraryHealerScanRequest?, CancellationToken>((request, _) => captured = request)
            .Returns(new LibraryHealerScanResult(LibraryHealerScanStatus.Completed, 0, 0, 0, 0, false, null, null));
        var handler = new LibraryHealerActionHandler(scanRunner.Object, new FakeFindingStore());
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        orchestrator.HandleAction(
            "healer/scan",
            new Dictionary<string, string>
            {
                ["artistId"] = "-7",
                ["afterTrackFileId"] = "0",
            },
            new BrainarrSettings());

        captured.Should().NotBeNull();
        captured!.ArtistId.Should().BeNull();
        captured.AfterTrackFileId.Should().BeNull();
    }

    [Fact]
    public void HandleAction_ShouldRedactHealerScanResultErrors()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var scanRunner = new Mock<ILibraryHealerScanRunner>(MockBehavior.Strict);
        scanRunner.Setup(x => x.Scan(It.IsAny<LibraryHealerScanRequest?>(), It.IsAny<CancellationToken>()))
            .Returns(new LibraryHealerScanResult(
                LibraryHealerScanStatus.Failed,
                0,
                0,
                0,
                0,
                false,
                null,
                @"failed D:\Music\Private Artist\secret.flac"));
        var handler = new LibraryHealerActionHandler(scanRunner.Object, new FakeFindingStore());
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/scan", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("error");
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"D:\Music");
    }

    [Fact]
    public void HandleAction_ShouldRedactRelativeAndUncHealerScanResultErrors()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var scanRunner = new Mock<ILibraryHealerScanRunner>(MockBehavior.Strict);
        scanRunner.Setup(x => x.Scan(It.IsAny<LibraryHealerScanRequest?>(), It.IsAny<CancellationToken>()))
            .Returns(new LibraryHealerScanResult(
                LibraryHealerScanStatus.Failed,
                0,
                0,
                0,
                0,
                false,
                null,
                @"failed \\server\share\Private Artist\secret.flac and Private Artist\Album\secret.flac"));
        var handler = new LibraryHealerActionHandler(scanRunner.Object, new FakeFindingStore());
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/scan", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("error");
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"\\server");
        json.Should().NotContain(@"Private Artist\Album");
    }

    [Fact]
    public void HandleAction_ShouldRouteHealerClearFindings()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var store = new FakeFindingStore();
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), store);
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/clearfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("\"ok\":true");
        store.Cleared.Should().BeTrue();
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldRedactHealerActionErrors()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var logger = TestLogger.Create("HealerActionError" + Guid.NewGuid().ToString("N"));
        var handler = new LibraryHealerActionHandler(
            Mock.Of<ILibraryHealerScanRunner>(),
            new FakeFindingStore(getRecentException: new InvalidOperationException(@"failed D:\Music\Private Artist\secret.flac")));
        TestLogger.ClearLoggedMessages();
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder, logger);

        var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);
        var logs = string.Join(Environment.NewLine, TestLogger.GetLoggedMessages());

        json.Should().Contain("error");
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"D:\Music");
        logs.Should().NotContain("Private Artist");
        logs.Should().NotContain(@"D:\Music");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldRedactRelativeAndUncHealerActionErrors()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var logger = TestLogger.Create("HealerRelativeActionError" + Guid.NewGuid().ToString("N"));
        var handler = new LibraryHealerActionHandler(
            Mock.Of<ILibraryHealerScanRunner>(),
            new FakeFindingStore(getRecentException: new InvalidOperationException(
                @"failed \\server\share\Private Artist\secret.flac and Private Artist\Album\secret.flac")));
        TestLogger.ClearLoggedMessages();
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder, logger);

        var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);
        var logs = string.Join(Environment.NewLine, TestLogger.GetLoggedMessages());

        json.Should().Contain("error");
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"\\server");
        logs.Should().NotContain("Private Artist");
        logs.Should().NotContain(@"\\server");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldRedactUnsupportedHealerActionNamesInLogs()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var logger = TestLogger.Create("HealerUnsupportedAction" + Guid.NewGuid().ToString("N"));
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), new FakeFindingStore());
        TestLogger.ClearLoggedMessages();
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder, logger);

        var result = orchestrator.HandleAction(
            @"healer/D:\Music\Private Artist\secret.flac",
            new Dictionary<string, string>(),
            new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);
        var logs = string.Join(Environment.NewLine, TestLogger.GetLoggedMessages());

        json.Should().Contain("error");
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"D:\Music");
        logs.Should().NotContain("Private Artist");
        logs.Should().NotContain(@"D:\Music");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldRedactUntrustedStoredFindingPayloads()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var finding = new LibraryHealerFinding(
            @"finding-D:\Music\Private Artist\secret.flac",
            new LibraryHealerFileIdentity(
                TrackFileId: 99,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: @"D:\Music\Private Artist\secret.flac",
                PathHash: @"D:\Music\Private Artist\secret.flac",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { @"TAG_FAILED_D:\Music\Private Artist\secret.flac" },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: false,
                DurationSeconds: null,
                ErrorType: @"ReadError-D:\Music\Private Artist\secret.flac",
                ErrorMessage: @"failed D:\Music\Private Artist\secret.flac"),
            new ProbeEvidence(
                ProbeAttempted: true,
                ProbeSucceeded: false,
                DurationSeconds: null,
                Container: @"container-D:\Music\Private Artist\secret.flac",
                AudioCodec: @"codec-D:\Music\Private Artist\secret.flac",
                ErrorType: @"ProbeError-D:\Music\Private Artist\secret.flac",
                ErrorMessage: @"probe failed D:\Music\Private Artist\secret.flac"),
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), new FakeFindingStore(new[] { finding }));
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("\"items\"");
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"D:\Music");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldTolerateStoredTagMetadataWithNullMissingFields()
    {
        var finding = new LibraryHealerFinding(
            "safe-metadata-finding",
            new LibraryHealerFileIdentity(
                TrackFileId: 99,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: "track01.flac#abcdef123456",
                PathHash: "abcdef123456",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagMetadataIssue,
            new[] { "TAG_METADATA_MISSING" },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: true,
                DurationSeconds: 245.2,
                ErrorType: null,
                ErrorMessage: null,
                Metadata: new TagMetadataEvidence(
                    TitlePresent: true,
                    ArtistPresent: true,
                    AlbumPresent: true,
                    AnyMusicBrainzIdPresent: true,
                    MissingFields: null!)),
            null,
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), new FakeFindingStore(new[] { finding }));

        var result = handler.Handle("healer/getfindings", new Dictionary<string, string>());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("FalsePositive");
        json.Should().NotContain("TagMetadataIssue");
        json.Should().Contain("safe-metadata-finding");
    }

    [Fact]
    public void HandleAction_ShouldAllowListStoredTagMetadataMissingFields()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        const string rawMetadataValue = "PrivateArtist";
        const string rawMusicBrainzLikeValue = "550e8400-e29b-41d4-a716-446655440000";
        var finding = new LibraryHealerFinding(
            "safe-metadata-finding",
            new LibraryHealerFileIdentity(
                TrackFileId: 99,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: "track01.flac#abcdef123456",
                PathHash: "abcdef123456",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagMetadataIssue,
            new[] { "TAG_METADATA_MISSING" },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: true,
                DurationSeconds: 245.2,
                ErrorType: null,
                ErrorMessage: null,
                Metadata: new TagMetadataEvidence(
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
                    })),
            null,
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), new FakeFindingStore(new[] { finding }));
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("TagMetadataIssue");
        json.Should().Contain("musicBrainzId");
        json.Should().NotContain(rawMetadataValue);
        json.Should().NotContain(rawMusicBrainzLikeValue);
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldAllowListStoredTagMetadataReasonCodes()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        const string rawMetadataValue = "PrivateArtist";
        const string rawMusicBrainzLikeValue = "550e8400-e29b-41d4-a716-446655440000";
        var finding = new LibraryHealerFinding(
            "safe-metadata-finding",
            new LibraryHealerFileIdentity(
                TrackFileId: 99,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: "track01.flac#abcdef123456",
                PathHash: "abcdef123456",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagMetadataIssue,
            new[]
            {
                "TAG_READER_DURATION_POSITIVE",
                "TAG_METADATA_MISSING",
                "TAG_MISSING_TITLE",
                "TAG_MISSING_" + rawMetadataValue,
                "TAG_MISSING_" + rawMusicBrainzLikeValue,
                rawMusicBrainzLikeValue,
            },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: true,
                DurationSeconds: 245.2,
                ErrorType: null,
                ErrorMessage: null,
                Metadata: new TagMetadataEvidence(
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
                    })),
            null,
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), new FakeFindingStore(new[] { finding }));
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("TAG_MISSING_ARTIST");
        json.Should().Contain("TAG_MISSING_MUSICBRAINZID");
        json.Should().NotContain(rawMetadataValue);
        json.Should().NotContain(rawMusicBrainzLikeValue);
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldDropStaleTagMetadataReasonCodes_WhenMetadataBooleansAreComplete()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        const string rawMetadataValue = "PrivateArtist";
        var finding = new LibraryHealerFinding(
            "safe-metadata-finding",
            new LibraryHealerFileIdentity(
                TrackFileId: 99,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: "track01.flac#abcdef123456",
                PathHash: "abcdef123456",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagMetadataIssue,
            new[]
            {
                "TAG_READER_DURATION_POSITIVE",
                "TAG_METADATA_MISSING",
                "TAG_MISSING_TITLE",
                "TAG_MISSING_" + rawMetadataValue,
            },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: true,
                DurationSeconds: 245.2,
                ErrorType: null,
                ErrorMessage: null,
                Metadata: new TagMetadataEvidence(
                    TitlePresent: true,
                    ArtistPresent: true,
                    AlbumPresent: true,
                    AnyMusicBrainzIdPresent: true,
                    MissingFields: new[] { "title", rawMetadataValue })),
            null,
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), new FakeFindingStore(new[] { finding }));
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("TAG_READER_DURATION_POSITIVE");
        json.Should().Contain("FalsePositive");
        json.Should().NotContain("TagMetadataIssue");
        json.Should().NotContain("TAG_METADATA_MISSING");
        json.Should().NotContain("TAG_MISSING_TITLE");
        json.Should().NotContain(rawMetadataValue);
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldRedactMetadataIdentifiersInStoredErrorMessages()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        const string rawMetadataValue = "PrivateArtist";
        const string rawMusicBrainzLikeValue = "550e8400-e29b-41d4-a716-446655440000";
        var finding = new LibraryHealerFinding(
            "safe-metadata-finding",
            new LibraryHealerFileIdentity(
                TrackFileId: 99,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: "track01.flac#abcdef123456",
                PathHash: "abcdef123456",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_FAILED" },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: false,
                DurationSeconds: null,
                ErrorType: "TagReadError",
                ErrorMessage: $"Artist tag {rawMetadataValue} has MusicBrainz id {rawMusicBrainzLikeValue}"),
            null,
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), new FakeFindingStore(new[] { finding }));
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("TAG_READER_FAILED");
        json.Should().NotContain(rawMetadataValue);
        json.Should().NotContain(rawMusicBrainzLikeValue);
        json.Should().NotContain("MusicBrainz id");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldRedactUntrustedStoredRelativeAndUncFindingPayloads()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        var finding = new LibraryHealerFinding(
            @"finding-\\server\share\Private Artist\secret.flac",
            new LibraryHealerFileIdentity(
                TrackFileId: 99,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: @"Private Artist\Album\secret.flac",
                PathHash: @"\\server\share\Private Artist\secret.flac",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { @"TAG_FAILED_Private Artist\Album\secret.flac" },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: false,
                DurationSeconds: null,
                ErrorType: @"ReadError-\\server\share\Private Artist\secret.flac",
                ErrorMessage: @"failed Private Artist\Album\secret.flac"),
            new ProbeEvidence(
                ProbeAttempted: true,
                ProbeSucceeded: false,
                DurationSeconds: null,
                Container: @"container-Private Artist\Album\secret.flac",
                AudioCodec: @"codec-\\server\share\Private Artist\secret.flac",
                ErrorType: @"ProbeError-Private Artist\Album\secret.flac",
                ErrorMessage: @"probe failed \\server\share\Private Artist\secret.flac"),
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), new FakeFindingStore(new[] { finding }));
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("\"items\"");
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"\\server");
        json.Should().NotContain(@"Private Artist\Album");
        json.Should().NotContain(@"Private Artist\\Album");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldRedactUntrustedStoredFilenameLikeFindingPayloads()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        const string safeRedactedPath = "track01.flac#abcdef123456";
        const string rawFilenameLikeValue = "Private Artist - Private Album - track01.flac";
        const string rawDriveLikeValue = "D:Private Artist track02.m4a";
        var finding = new LibraryHealerFinding(
            rawFilenameLikeValue,
            new LibraryHealerFileIdentity(
                TrackFileId: 99,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: safeRedactedPath,
                PathHash: rawFilenameLikeValue,
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_ZERO_DURATION", rawFilenameLikeValue },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: false,
                DurationSeconds: null,
                ErrorType: rawFilenameLikeValue,
                ErrorMessage: "failed reading " + rawFilenameLikeValue),
            new ProbeEvidence(
                ProbeAttempted: true,
                ProbeSucceeded: false,
                DurationSeconds: null,
                Container: rawDriveLikeValue,
                AudioCodec: rawFilenameLikeValue,
                ErrorType: rawDriveLikeValue,
                ErrorMessage: "probe failed " + rawDriveLikeValue),
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), new FakeFindingStore(new[] { finding }));
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain(safeRedactedPath);
        json.Should().NotContain(rawFilenameLikeValue);
        json.Should().NotContain("Private Artist");
        json.Should().NotContain("Private Album");
        json.Should().NotContain("D:Private");
        json.Should().NotContain("track02.m4a");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldRedactUntrustedStoredFilenameLikeRedactedPath()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        const string rawFilenameLikeValue = "Private Artist - Private Album - track01.flac";
        var finding = new LibraryHealerFinding(
            "safe-finding",
            new LibraryHealerFileIdentity(
                TrackFileId: 99,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: rawFilenameLikeValue,
                PathHash: "callerhash001",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_ZERO_DURATION" },
            new TagReaderEvidence(true, true, 0, null, null),
            null,
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc),
            EvidenceFreshness: HealerTreatmentVocab.Freshness.Current,
            IdentityFreshness: HealerTreatmentVocab.Freshness.Current);
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), new FakeFindingStore(new[] { finding }));
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("track01.flac#" + PathPrivacy.HashPath(rawFilenameLikeValue));
        json.Should().NotContain(rawFilenameLikeValue);
        json.Should().NotContain("Private Artist");
        json.Should().NotContain("Private Album");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldRedactPathMaterialBeforeExistingDisplayHash()
    {
        var providerFactory = new Mock<IProviderFactory>(MockBehavior.Strict);
        var providerInvoker = new Mock<IProviderInvoker>(MockBehavior.Strict);
        var promptBuilder = new Mock<ILibraryAwarePromptBuilder>(MockBehavior.Strict);
        const string rawDisplayPath = @"D:\Music\Private Artist\Album\track01.flac#abcdef123456";
        var finding = new LibraryHealerFinding(
            "safe-finding",
            new LibraryHealerFileIdentity(
                TrackFileId: 99,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: rawDisplayPath,
                PathHash: "callerhash001",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_ZERO_DURATION" },
            new TagReaderEvidence(true, true, 0, null, null),
            null,
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc));
        var handler = new LibraryHealerActionHandler(Mock.Of<ILibraryHealerScanRunner>(), new FakeFindingStore(new[] { finding }));
        var orchestrator = CreateOrchestrator(handler, providerFactory, providerInvoker, promptBuilder);

        var result = orchestrator.HandleAction("healer/getfindings", new Dictionary<string, string>(), new BrainarrSettings());
        var json = JsonSerializer.Serialize(result);

        json.Should().Contain("track01.flac#abcdef123456");
        json.Should().NotContain(rawDisplayPath);
        json.Should().NotContain("Private Artist");
        json.Should().NotContain(@"D:\Music");
        json.Should().NotContain(@"D:\\Music");
        providerFactory.VerifyNoOtherCalls();
        providerInvoker.VerifyNoOtherCalls();
        promptBuilder.VerifyNoOtherCalls();
    }

    [Fact]
    public void HandleAction_ShouldDefaultMalformedHealerScanQueryValues()
    {
        var scanRunner = new Mock<ILibraryHealerScanRunner>(MockBehavior.Strict);
        LibraryHealerScanRequest? captured = null;
        scanRunner.Setup(x => x.Scan(It.IsAny<LibraryHealerScanRequest?>(), It.IsAny<CancellationToken>()))
            .Callback<LibraryHealerScanRequest?, CancellationToken>((request, _) => captured = request)
            .Returns(new LibraryHealerScanResult(LibraryHealerScanStatus.Completed, 0, 0, 0, 0, false, null, null));
        var handler = new LibraryHealerActionHandler(scanRunner.Object, new FakeFindingStore());

        handler.Handle(
            "healer/scan",
            new Dictionary<string, string>
            {
                ["artistId"] = "artist",
                ["afterTrackFileId"] = "track",
                ["maxFiles"] = "files",
                ["maxSeconds"] = "seconds",
            });

        captured.Should().NotBeNull();
        captured!.ArtistId.Should().BeNull();
        captured.AfterTrackFileId.Should().BeNull();
        captured.MaxFiles.Should().Be(100);
        captured.MaxSeconds.Should().Be(10);
    }

    [Fact]
    public void HandleAction_ShouldClampHealerLowerBounds()
    {
        var scanRunner = new Mock<ILibraryHealerScanRunner>(MockBehavior.Strict);
        LibraryHealerScanRequest? captured = null;
        scanRunner.Setup(x => x.Scan(It.IsAny<LibraryHealerScanRequest?>(), It.IsAny<CancellationToken>()))
            .Callback<LibraryHealerScanRequest?, CancellationToken>((request, _) => captured = request)
            .Returns(new LibraryHealerScanResult(LibraryHealerScanStatus.Completed, 0, 0, 0, 0, false, null, null));
        var store = new FakeFindingStore(new[]
        {
            Finding("first", trackFileId: 1, label: LibraryHealerLabel.TagReaderSymptom),
            Finding("second", trackFileId: 2, label: LibraryHealerLabel.TagReaderSymptom),
        });
        var handler = new LibraryHealerActionHandler(scanRunner.Object, store);

        handler.Handle(
            "healer/scan",
            new Dictionary<string, string>
            {
                ["maxFiles"] = "0",
                ["maxSeconds"] = "-5",
            });
        var findings = handler.Handle("healer/getfindings", new Dictionary<string, string> { ["limit"] = "0" });
        var json = JsonSerializer.Serialize(findings);

        captured.Should().NotBeNull();
        captured!.MaxFiles.Should().Be(1);
        captured.MaxSeconds.Should().Be(1);
        json.Should().Contain("first");
        json.Should().NotContain("second");
    }

    [Fact]
    public void Handler_ShouldReleaseScanGate_WhenScanIsCanceled()
    {
        var scanRunner = new Mock<ILibraryHealerScanRunner>(MockBehavior.Strict);
        scanRunner.SetupSequence(x => x.Scan(It.IsAny<LibraryHealerScanRequest?>(), It.IsAny<CancellationToken>()))
            .Throws(new OperationCanceledException("first canceled"))
            .Returns(new LibraryHealerScanResult(LibraryHealerScanStatus.Completed, 0, 0, 0, 0, false, null, null));
        var handler = new LibraryHealerActionHandler(scanRunner.Object, new FakeFindingStore());

        var first = () => handler.Handle("healer/scan", new Dictionary<string, string>());
        first.Should().Throw<OperationCanceledException>();
        var second = handler.Handle("healer/scan", new Dictionary<string, string>());

        JsonSerializer.Serialize(second).Should().Contain("\"ok\":true");
    }

    [Fact]
    public void Handler_ShouldReleaseScanGate_WhenScanThrows()
    {
        var scanRunner = new Mock<ILibraryHealerScanRunner>(MockBehavior.Strict);
        scanRunner.SetupSequence(x => x.Scan(It.IsAny<LibraryHealerScanRequest?>(), It.IsAny<CancellationToken>()))
            .Throws(new InvalidOperationException("first failure"))
            .Returns(new LibraryHealerScanResult(LibraryHealerScanStatus.Completed, 0, 0, 0, 0, false, null, null));
        var handler = new LibraryHealerActionHandler(scanRunner.Object, new FakeFindingStore());

        var first = () => handler.Handle("healer/scan", new Dictionary<string, string>());
        first.Should().Throw<InvalidOperationException>();
        var second = handler.Handle("healer/scan", new Dictionary<string, string>());

        JsonSerializer.Serialize(second).Should().Contain("\"ok\":true");
    }

    [Fact]
    public void Handler_ShouldRejectClearFindings_WhenScanIsRunning()
    {
        var scanRunner = new Mock<ILibraryHealerScanRunner>(MockBehavior.Strict);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var store = new FakeFindingStore();
        scanRunner.Setup(x => x.Scan(It.IsAny<LibraryHealerScanRequest?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                return new LibraryHealerScanResult(LibraryHealerScanStatus.Completed, 0, 0, 0, 0, false, null, null);
            });
        var handler = new LibraryHealerActionHandler(scanRunner.Object, store);

        var scan = RunOnDedicatedThread(() => handler.Handle("healer/scan", new Dictionary<string, string>()));
        entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        var clear = handler.Handle("healer/clearfindings", new Dictionary<string, string>());
        release.Set();
        scan.GetAwaiter().GetResult();

        var json = JsonSerializer.Serialize(clear);
        json.Should().Contain("\"ok\":false");
        json.Should().Contain("scan is running");
        store.Cleared.Should().BeFalse();
    }

    [Fact]
    public void Handler_ShouldRejectConcurrentScan()
    {
        var scanRunner = new Mock<ILibraryHealerScanRunner>(MockBehavior.Strict);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        scanRunner.Setup(x => x.Scan(It.IsAny<LibraryHealerScanRequest?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
                return new LibraryHealerScanResult(LibraryHealerScanStatus.Completed, 0, 0, 0, 0, false, null, null);
            });
        var handler = new LibraryHealerActionHandler(scanRunner.Object, new FakeFindingStore());

        var first = RunOnDedicatedThread(() => handler.Handle("healer/scan", new Dictionary<string, string>()));
        entered.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
        var second = handler.Handle("healer/scan", new Dictionary<string, string>());
        release.Set();
        first.GetAwaiter().GetResult();

        var json = JsonSerializer.Serialize(second);
        json.Should().Contain("\"ok\":false");
        json.Should().Contain("already running");
    }

    private static BrainarrOrchestrator CreateOrchestrator(
        LibraryHealerActionHandler healerActionHandler,
        Mock<IProviderFactory> providerFactory,
        Mock<IProviderInvoker> providerInvoker,
        Mock<ILibraryAwarePromptBuilder> promptBuilder,
        Logger? logger = null)
    {
        var libraryAnalyzer = new Mock<ILibraryAnalyzer>(MockBehavior.Strict);
        libraryAnalyzer.Setup(x => x.AnalyzeLibrary()).Returns(new LibraryProfile());

        return new BrainarrOrchestrator(
            logger ?? TestLogger.CreateNullLogger(),
            providerFactory.Object,
            libraryAnalyzer.Object,
            Mock.Of<IRecommendationCache>(),
            Mock.Of<IProviderHealthMonitor>(),
            Mock.Of<IRecommendationValidator>(),
            Mock.Of<IModelDetectionService>(),
            Mock.Of<IHttpClient>(),
            duplicationPrevention: null,
            providerInvoker: providerInvoker.Object,
            promptBuilder: promptBuilder.Object,
            breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object,
            duplicateFilter: Mock.Of<IDuplicateFilterService>(),
            healerActionHandler: healerActionHandler);
    }

    private static LibraryHealerFinding Finding(string id, int trackFileId, LibraryHealerLabel label)
    {
        return new LibraryHealerFinding(
            id,
            new LibraryHealerFileIdentity(
                trackFileId,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: "redacted.flac#abc123",
                PathHash: "abc123",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            label,
            new[] { "TAG_READER_ZERO_DURATION" },
            new TagReaderEvidence(true, true, 0, null, null),
            null,
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc),
            EvidenceFreshness: HealerTreatmentVocab.Freshness.Current,
            IdentityFreshness: HealerTreatmentVocab.Freshness.Current);
    }

    private static LibraryHealerFinding MetadataFinding(string id, int trackFileId)
    {
        return new LibraryHealerFinding(
            id,
            new LibraryHealerFileIdentity(
                trackFileId,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: "metadata.flac#abc123",
                PathHash: "abc123",
                Size: 123,
                ModifiedUtc: new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagMetadataIssue,
            new[]
            {
                "TAG_READER_DURATION_POSITIVE",
                "TAG_METADATA_MISSING",
                "TAG_MISSING_TITLE",
                "TAG_MISSING_MUSICBRAINZID",
            },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: true,
                DurationSeconds: 245.2,
                ErrorType: null,
                ErrorMessage: null,
                Metadata: new TagMetadataEvidence(
                    TitlePresent: false,
                    ArtistPresent: true,
                    AlbumPresent: true,
                    AnyMusicBrainzIdPresent: false,
                    MissingFields: new[] { "title", "musicBrainzId" })),
            null,
            new DateTime(2026, 6, 30, 1, 2, 3, DateTimeKind.Utc),
            EvidenceFreshness: HealerTreatmentVocab.Freshness.Current,
            IdentityFreshness: HealerTreatmentVocab.Freshness.Current);
    }

    private static Task<object> RunOnDedicatedThread(Func<object> action)
    {
        var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "BrainarrHealerActionTest",
        };

        thread.Start();
        return completion.Task;
    }

    private static IReadOnlyList<string> JsonStrings(JsonElement array)
    {
        return array.EnumerateArray()
            .Select(item => item.GetString())
            .Where(value => value is not null)
            .Select(value => value!)
            .ToList();
    }

    private sealed class FakeFindingStore : ILibraryHealerFindingStore
    {
        private readonly IReadOnlyList<LibraryHealerFinding> _findings;
        private readonly Exception? _getRecentException;

        public FakeFindingStore(
            IReadOnlyList<LibraryHealerFinding>? findings = null,
            Exception? getRecentException = null)
        {
            _findings = findings ?? Array.Empty<LibraryHealerFinding>();
            _getRecentException = getRecentException;
        }

        public bool Cleared { get; private set; }

        public bool SaveBatch(IReadOnlyList<LibraryHealerFinding> findings)
        {
            return true;
        }

        public IReadOnlyList<LibraryHealerFinding> GetRecent(int limit)
        {
            if (_getRecentException != null)
            {
                throw _getRecentException;
            }

            return _findings.Take(limit).ToList();
        }

        public IReadOnlyList<LibraryHealerFinding> GetAllRecent()
        {
            if (_getRecentException != null)
            {
                throw _getRecentException;
            }

            return _findings.ToList();
        }

        public void Clear()
        {
            Cleared = true;
        }
    }
}
