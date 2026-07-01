using System.Text.Json;
using FluentAssertions;
using Moq;
using NzbDrone.Core.ImportLists.Brainarr.Services.Healing;
using Xunit.Sdk;

namespace Brainarr.Tests.Services.Healing;

public sealed class LibraryHealerFieldSensitivityCatalogTests
{
    private static readonly string[] DynamicSummaryMapPaths =
    {
        "summary.blockedReasons",
        "summary.byRisk",
        "summary.byWorkflow",
        "summary.byWorkflowByRisk",
    };

    [Fact]
    public void GetFieldCatalog_ShouldReturnReadOnlyFieldPolicyWithoutScanningOrTouchingStore()
    {
        var scanRunner = new Mock<ILibraryHealerScanRunner>(MockBehavior.Strict);
        var store = new NoopFindingStore();
        var handler = new LibraryHealerActionHandler(scanRunner.Object, store);

        var result = handler.Handle(
            "healer/getfieldcatalog",
            new Dictionary<string, string> { ["ignored"] = "true" });
        var json = JsonSerializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        var root = document.RootElement;
        root.GetProperty("schemaVersion").GetInt32().Should().Be(1);
        root.GetProperty("action").GetString().Should().Be("healer/getfindings");
        root.GetProperty("fields").EnumerateArray().Should().NotBeEmpty();
        store.Touched.Should().BeFalse("the sensitivity catalog is static read-only metadata");
        scanRunner.VerifyNoOtherCalls();
    }

    [Fact]
    public void GetFieldCatalog_ShouldCoverEveryCurrentGetFindingsField()
    {
        var json = GetCatalogJson();
        using var document = JsonDocument.Parse(json);
        var expectedFields = GetCurrentGetFindingsContractLeafPaths();

        var fields = document.RootElement
            .GetProperty("fields")
            .EnumerateArray()
            .Select(field => field.GetProperty("field").GetString())
            .Where(field => field is not null)
            .Select(field => field!)
            .ToArray();

        fields.Should().BeEquivalentTo(
            expectedFields,
            options => options.WithStrictOrdering(),
            "the sensitivity catalog must match the actual serialized healer/getfindings golden contract");
    }

    [Fact]
    public void GetFieldCatalog_ShouldBlockPrivateAndIdentifierFieldsFromSupportExportAndAiPrompt()
    {
        var json = GetCatalogJson();
        using var document = JsonDocument.Parse(json);

        var fields = FieldMap(document.RootElement);
        foreach (var entry in fields.Values.Where(entry => entry.GetProperty("sensitivity").GetString() != "public"))
        {
            entry.GetProperty("shareableSupportExport").GetBoolean().Should().BeFalse();
            entry.GetProperty("aiPrompt").GetBoolean().Should().BeFalse();
        }

        fields["items[].path"].GetProperty("sensitivity").GetString().Should().Be("redacted_path_identifier");
        fields["items[].pathHash"].GetProperty("sensitivity").GetString().Should().Be("redacted_path_identifier");
        fields["items[].id"].GetProperty("sensitivity").GetString().Should().Be("redacted_identifier");
        fields["items[].tagReader.ErrorMessage"].GetProperty("sensitivity").GetString().Should().Be("private_diagnostic");
        fields["items[].probe.ErrorMessage"].GetProperty("sensitivity").GetString().Should().Be("private_diagnostic");
    }

    [Fact]
    public void GetFieldCatalogContract_ShouldMatchGoldenSnapshot()
    {
        GetCatalogJson().ShouldMatchGoldenFixture("library_healer_field_catalog_contract.json");
    }

    private static string GetCatalogJson()
    {
        var handler = new LibraryHealerActionHandler(
            Mock.Of<ILibraryHealerScanRunner>(MockBehavior.Strict),
            new NoopFindingStore());
        var result = handler.Handle("healer/getfieldcatalog", new Dictionary<string, string>());
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private static IReadOnlyList<string> GetCurrentGetFindingsContractLeafPaths()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "LibraryHealer", "library_healer_getfindings_contract.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return ExtractLeafPaths(document.RootElement, string.Empty)
            .OrderBy(field => field, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> ExtractLeafPaths(JsonElement element, string path)
    {
        if (DynamicSummaryMapPaths.Contains(path, StringComparer.Ordinal))
        {
            yield return path;
            yield break;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var childPath = string.IsNullOrEmpty(path)
                        ? property.Name
                        : path + "." + property.Name;
                    foreach (var item in ExtractLeafPaths(property.Value, childPath))
                    {
                        yield return item;
                    }
                }

                break;
            case JsonValueKind.Array:
                var arrayPath = path + "[]";
                var leaves = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    leaves.AddRange(ExtractLeafPaths(item, arrayPath));
                }

                foreach (var leaf in leaves
                    .Distinct(StringComparer.Ordinal)
                    .Where(leaf => !leaves.Any(other =>
                        !string.Equals(leaf, other, StringComparison.Ordinal)
                        && other.StartsWith(leaf + ".", StringComparison.Ordinal))))
                {
                    yield return leaf;
                }

                break;
            case JsonValueKind.Null:
                yield return path;
                break;
            default:
                yield return path;
                break;
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> FieldMap(JsonElement root)
    {
        return root
            .GetProperty("fields")
            .EnumerateArray()
            .ToDictionary(
                field => field.GetProperty("field").GetString() ?? string.Empty,
                field => field.Clone(),
                StringComparer.Ordinal);
    }

    private sealed class NoopFindingStore : ILibraryHealerFindingStore
    {
        public bool Touched { get; private set; }

        public void SaveBatch(IReadOnlyList<LibraryHealerFinding> findings)
        {
            Touched = true;
            throw new XunitException("healer/getfieldcatalog must not persist findings");
        }

        public IReadOnlyList<LibraryHealerFinding> GetRecent(int limit)
        {
            Touched = true;
            throw new XunitException("healer/getfieldcatalog must not read findings");
        }

        public IReadOnlyList<LibraryHealerFinding> GetAllRecent()
        {
            Touched = true;
            throw new XunitException("healer/getfieldcatalog must not read findings");
        }

        public void Clear()
        {
            Touched = true;
            throw new XunitException("healer/getfieldcatalog must not clear findings");
        }
    }
}
