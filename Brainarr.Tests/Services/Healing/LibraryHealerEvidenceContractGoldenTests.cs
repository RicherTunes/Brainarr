using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit.Sdk;

namespace Brainarr.Tests.Services.Healing;

public sealed class LibraryHealerEvidenceContractGoldenTests
{
    private const string PrivateWindowsPath = @"D:\Music\Private Artist\Private Album\track01.flac";
    private const string PrivateUncPath = @"\\nas\Music\Private Artist\Private Album\track01.flac";
    private const string PrivateUnixPath = "/mnt/music/Private Artist/Private Album/track01.flac";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    [Fact]
    public void GetFindingsContract_ShouldMatchGoldenSnapshot()
    {
        var scanRunner = new Mock<ILibraryHealerScanRunner>(MockBehavior.Strict);
        var handler = new LibraryHealerActionHandler(
            scanRunner.Object,
            new FakeFindingStore(new[]
            {
                RepairCandidateFinding(),
                TagRepairCandidateFinding(),
                TaintedStoredFinding(),
                MalformedStoredFinding(),
            }));

        var result = handler.Handle("healer/getfindings", new Dictionary<string, string>());
        var actual = ToCanonicalJson(result);

        AssertNoSensitiveMaterial(actual);
        AssertMalformedFindingFailsClosed(actual);
        actual.ShouldMatchGoldenFixture("library_healer_getfindings_contract.json");
        scanRunner.VerifyNoOtherCalls();
    }

    [Fact]
    public void TreatmentVocabularyContract_ShouldMatchGoldenSnapshot()
    {
        var contract = new
        {
            schemaVersion = HealerTreatmentVocab.SchemaVersion,
            vocab = typeof(HealerTreatmentVocab)
                .GetNestedTypes(BindingFlags.Public)
                .OrderBy(type => type.Name, StringComparer.Ordinal)
                .ToDictionary(
                    type => type.Name,
                    type => type
                        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                        .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
                        .OrderBy(field => field.Name, StringComparer.Ordinal)
                        .ToDictionary(
                            field => field.Name,
                            field => (string)field.GetRawConstantValue()!,
                            StringComparer.Ordinal),
                    StringComparer.Ordinal),
        };

        AssertVocabularyHasNoStaticReadonlyStrings();
        AssertA2VocabularySafetyInvariants(contract.vocab);
        AssertActionFilterVocabularyIsAligned(contract.vocab);
        ToCanonicalJson(contract).ShouldMatchGoldenFixture("library_healer_treatment_vocab_contract.json");
    }

    private static LibraryHealerFinding RepairCandidateFinding()
    {
        return new LibraryHealerFinding(
            "repair-candidate",
            new LibraryHealerFileIdentity(
                TrackFileId: 101,
                ArtistId: 10,
                AlbumId: 20,
                RedactedPath: "repair.flac#111111111111",
                PathHash: "111111111111",
                Size: 123456,
                ModifiedUtc: new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_ZERO_DURATION" },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: true,
                DurationSeconds: 0,
                ErrorType: null,
                ErrorMessage: null),
            Probe: null,
            ObservedAtUtc: new DateTime(2026, 7, 1, 11, 0, 0, DateTimeKind.Utc));
    }

    private static LibraryHealerFinding TagRepairCandidateFinding()
    {
        return new LibraryHealerFinding(
            "tag-repair-candidate",
            new LibraryHealerFileIdentity(
                TrackFileId: 102,
                ArtistId: 10,
                AlbumId: 21,
                RedactedPath: "metadata.flac#222222222222",
                PathHash: "222222222222",
                Size: 223456,
                ModifiedUtc: new DateTime(2026, 7, 1, 10, 5, 0, DateTimeKind.Utc)),
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
            Probe: null,
            ObservedAtUtc: new DateTime(2026, 7, 1, 11, 5, 0, DateTimeKind.Utc));
    }

    private static LibraryHealerFinding TaintedStoredFinding()
    {
        return new LibraryHealerFinding(
            PrivateWindowsPath,
            new LibraryHealerFileIdentity(
                TrackFileId: 103,
                ArtistId: 11,
                AlbumId: 22,
                RedactedPath: PrivateWindowsPath,
                PathHash: PrivateWindowsPath,
                Size: 323456,
                ModifiedUtc: new DateTime(2026, 7, 1, 10, 10, 0, DateTimeKind.Utc)),
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_FAILED", PrivateWindowsPath, PrivateUncPath, PrivateUnixPath },
            new TagReaderEvidence(
                ReadAttempted: true,
                ReadSucceeded: false,
                DurationSeconds: null,
                ErrorType: PrivateUncPath,
                ErrorMessage: "reader failed " + PrivateWindowsPath + " " + PrivateUncPath + " " + PrivateUnixPath),
            new ProbeEvidence(
                ProbeAttempted: true,
                ProbeSucceeded: false,
                DurationSeconds: null,
                Container: PrivateWindowsPath,
                AudioCodec: "ffmpeg -i " + PrivateUnixPath,
                ErrorType: PrivateUncPath,
                ErrorMessage: "probe failed " + PrivateWindowsPath + " " + PrivateUncPath + " " + PrivateUnixPath),
            ObservedAtUtc: new DateTime(2026, 7, 1, 11, 10, 0, DateTimeKind.Utc));
    }

    private static LibraryHealerFinding MalformedStoredFinding()
    {
        return new LibraryHealerFinding(
            "malformed-record",
            File: null!,
            LibraryHealerLabel.TagReaderSymptom,
            new[] { "TAG_READER_ZERO_DURATION" },
            TagReader: null!,
            Probe: null,
            ObservedAtUtc: new DateTime(2026, 7, 1, 11, 15, 0, DateTimeKind.Utc));
    }

    private static string ToCanonicalJson(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, JsonOptions)
            ?? throw new InvalidOperationException("Unable to serialize contract value.");
        var canonical = Canonicalize(node);
        return canonical!.ToJsonString(JsonOptions) + Environment.NewLine;
    }

    private static JsonNode? Canonicalize(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => CanonicalizeObject(obj),
            JsonArray array => CanonicalizeArray(array),
            null => null,
            _ => node.DeepClone(),
        };
    }

    private static JsonObject CanonicalizeObject(JsonObject obj)
    {
        var result = new JsonObject();
        foreach (var item in obj.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            result.Add(item.Key, Canonicalize(item.Value));
        }

        return result;
    }

    private static JsonArray CanonicalizeArray(JsonArray array)
    {
        var result = new JsonArray();
        foreach (var item in array)
        {
            result.Add(Canonicalize(item));
        }

        return result;
    }

    private static void AssertNoSensitiveMaterial(string actual)
    {
        var forbiddenFragments = new[]
        {
            PrivateWindowsPath,
            PrivateWindowsPath.Replace(@"\", @"\\", StringComparison.Ordinal),
            PrivateUncPath,
            PrivateUncPath.Replace(@"\", @"\\", StringComparison.Ordinal),
            PrivateUnixPath,
            @"D:\",
            @"D:\\",
            @"\\nas",
            @"\\\\nas",
            "/mnt/music",
            "/home/",
            "/Users/",
            "Private Artist",
            "Private Album",
            "reader failed",
            "probe failed",
            "ffmpeg -i",
        };

        foreach (var fragment in forbiddenFragments)
        {
            actual.Should().NotContain(fragment);
        }
    }

    private static void AssertMalformedFindingFailsClosed(string actual)
    {
        using var document = JsonDocument.Parse(actual);
        var malformed = document.RootElement
            .GetProperty("items")
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == "malformed-record");

        malformed.GetProperty("label").GetString().Should().Be(LibraryHealerLabel.NeedsHumanReview.ToString());
        malformed.GetProperty("trackFileId").GetInt32().Should().Be(0);
        JsonStrings(malformed.GetProperty("reasons")).Should().Contain(HealerTreatmentVocab.BlockedReason.MalformedFindingRecord);

        var plan = malformed.GetProperty("treatmentPlan");
        plan.GetProperty("candidateWorkflow").GetString().Should().Be(HealerTreatmentVocab.Workflow.Review);
        plan.GetProperty("risk").GetString().Should().Be(HealerTreatmentVocab.Risk.High);
        plan.GetProperty("executionAuthorization").GetProperty("authorized").GetBoolean().Should().BeFalse();
        JsonStrings(plan.GetProperty("blockedReasons")).Should().Contain(HealerTreatmentVocab.BlockedReason.MalformedFindingRecord);
        JsonStrings(plan.GetProperty("rationaleCodes")).Should().Contain(HealerTreatmentVocab.Rationale.MalformedFindingRecord);
    }

    private static void AssertVocabularyHasNoStaticReadonlyStrings()
    {
        var nonConstantPublicStrings = typeof(HealerTreatmentVocab)
            .GetNestedTypes(BindingFlags.Public)
            .SelectMany(type => type
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(field => field.FieldType == typeof(string) && (!field.IsLiteral || field.IsInitOnly))
                .Select(field => type.Name + "." + field.Name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        nonConstantPublicStrings.Should().BeEmpty("treatment vocabulary values are part of the public contract and must snapshot as compile-time constants");
    }

    private static void AssertA2VocabularySafetyInvariants(IReadOnlyDictionary<string, Dictionary<string, string>> vocab)
    {
        vocab[nameof(HealerTreatmentVocab.SafetyLevel)]
            .Values
            .Should()
            .Equal(HealerTreatmentVocab.SafetyLevel.ReadOnly);
        vocab[nameof(HealerTreatmentVocab.AuthorizationAuthority)]
            .Values
            .Should()
            .Equal(HealerTreatmentVocab.AuthorizationAuthority.None);
        vocab[nameof(HealerTreatmentVocab.AuthorizationReason)]
            .Values
            .Should()
            .Equal(HealerTreatmentVocab.AuthorizationReason.A2ReadOnly);

        var disallowedAuthorityTerms = new[] { "write", "execute", "apply", "delete", "replace", "import" };
        foreach (var authority in vocab[nameof(HealerTreatmentVocab.AuthorizationAuthority)].Values)
        {
            foreach (var term in disallowedAuthorityTerms)
            {
                authority.Contains(term, StringComparison.OrdinalIgnoreCase)
                    .Should()
                    .BeFalse("A2/A2.5 authorization authorities must not describe mutating actions");
            }
        }
    }

    private static void AssertActionFilterVocabularyIsAligned(IReadOnlyDictionary<string, Dictionary<string, string>> vocab)
    {
        GetPrivateStaticStringArray("WorkflowFilterValues")
            .Should()
            .BeEquivalentTo(vocab[nameof(HealerTreatmentVocab.Workflow)].Values);
        GetPrivateStaticStringArray("RiskFilterValues")
            .Should()
            .BeEquivalentTo(vocab[nameof(HealerTreatmentVocab.Risk)].Values);
        GetPrivateStaticStringArray("BlockedReasonFilterValues")
            .Should()
            .BeEquivalentTo(vocab[nameof(HealerTreatmentVocab.BlockedReason)].Values);
    }

    private static IReadOnlyList<string> GetPrivateStaticStringArray(string fieldName)
    {
        var field = typeof(LibraryHealerActionHandler).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Missing filter vocabulary field: " + fieldName);
        return field.GetValue(null) as string[]
            ?? throw new InvalidOperationException("Filter vocabulary field is not a string array: " + fieldName);
    }

    private static IReadOnlyList<string> JsonStrings(JsonElement element)
    {
        return element.EnumerateArray()
            .Select(value => value.GetString() ?? string.Empty)
            .ToArray();
    }

    private sealed class FakeFindingStore : ILibraryHealerFindingStore
    {
        private readonly IReadOnlyList<LibraryHealerFinding> _findings;

        public FakeFindingStore(IReadOnlyList<LibraryHealerFinding> findings)
        {
            _findings = findings;
        }

        public bool SaveBatch(IReadOnlyList<LibraryHealerFinding> findings)
        {
            throw new XunitException("healer/getfindings must not persist findings");
        }

        public IReadOnlyList<LibraryHealerFinding> GetRecent(int limit)
        {
            return _findings.Take(limit).ToList();
        }

        public IReadOnlyList<LibraryHealerFinding> GetAllRecent()
        {
            return _findings.ToList();
        }

        public void Clear()
        {
            throw new XunitException("healer/getfindings must not clear findings");
        }
    }
}

internal static class LibraryHealerGoldenFixtureAssertions
{
    public static void ShouldMatchGoldenFixture(this string actual, string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "LibraryHealer", fileName);
        if (!File.Exists(path))
        {
            throw new XunitException($"Missing golden fixture: {path}{Environment.NewLine}{actual}");
        }

        var expected = File.ReadAllText(path);
        NormalizeLineEndings(actual).Should().Be(NormalizeLineEndings(expected));
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
