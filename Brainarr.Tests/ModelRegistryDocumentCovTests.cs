using System.Text.Json;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using Xunit;

namespace Brainarr.Tests
{
    public class ModelRegistryDocumentCovTests
    {
        #region Exception Paths

        [Fact]
        public void ProvidersFlexibleConverter_ShouldThrow_WhenLegacyEntryMissingAllIdentifiers()
        {
            // Line 187: throw new JsonException("Provider entry is missing 'slug', 'id', or 'name'.");
            const string json = @"{ ""providers"": [ { ""displayName"": ""NoIds"" } ] }";

            var act = () => JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            act.Should().Throw<JsonException>()
                .WithMessage("Provider entry is missing 'slug', 'id', or 'name'.*");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldThrow_WhenProvidersIsInvalidType()
        {
            // Line 212: throw new JsonException("Expected object or array for 'providers'.");
            const string json = @"{ ""providers"": ""invalid"" }";

            var act = () => JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            act.Should().Throw<JsonException>()
                .WithMessage("Expected object or array for 'providers'.*");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldThrow_WhenProviderHasNullValueInDictionary()
        {
            // Line 240 via AddOrMerge when key is null/empty after normalization
            const string json = @"{ ""providers"": { """": { ""name"": ""EmptyKey"" } } }";

            var act = () => JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            act.Should().Throw<JsonException>()
                .WithMessage("Provider entry is missing a valid identifier.*");
        }

        #endregion

        #region Legacy Array Parsing

        [Fact]
        public void ProvidersFlexibleConverter_ShouldUseId_WhenSlugIsMissing()
        {
            // Line 227-230: SelectLegacyKey fallback to Id
            const string json = @"{ ""providers"": [ { ""id"": ""provider-by-id"", ""name"": ""Test"" } ] }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers.Should().ContainKey("provider-by-id");
            document.Providers["provider-by-id"].Slug.Should().Be("provider-by-id");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldUseName_WhenSlugAndIdAreMissing()
        {
            // Line 232: SelectLegacyKey fallback to Name
            const string json = @"{ ""providers"": [ { ""name"": ""provider-by-name"" } ] }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers.Should().ContainKey("provider-by-name");
            document.Providers["provider-by-name"].Name.Should().Be("provider-by-name");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldCopyAdditionalProperties_FromLegacyDescriptor()
        {
            // Lines 198-204: Additional properties copying
            const string json = @"{ ""providers"": [ {
                ""slug"": ""test"",
                ""customField"": ""customValue"",
                ""models"": []
            } ] }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers["test"].AdditionalProperties.Should().ContainKey("customField");
        }

        #endregion

        #region Merge Operations

        [Fact]
        public void ProvidersFlexibleConverter_ShouldMergePricing_WhenDestinationIsNull()
        {
            // Lines 379-381: MergePricing creates new if null
            const string json = @"{
                ""providers"": [
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"" } ] },
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m2"", ""pricing"": { ""input_per_1k"": 0.5, ""output_per_1k"": 1.5 } } ] }
                ]
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers["test"].Models.Should().HaveCount(2);
            var modelWithPricing = document.Providers["test"].Models.First(m => m.Id == "m2");
            modelWithPricing.Pricing!.InputPer1k.Should().Be(0.5);
            modelWithPricing.Pricing.OutputPer1k.Should().Be(1.5);
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldMergeCapabilities_WhenDestinationIsNull()
        {
            // Lines 392-397: MergeCapabilities creates new if null
            const string json = @"{
                ""providers"": [
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"" } ] },
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"", ""capabilities"": { ""stream"": true, ""json_mode"": true, ""tools"": true, ""tool_choice"": ""auto"" } } ] }
                ]
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            var model = document!.Providers["test"].Models.First(m => m.Id == "m1");
            model.Capabilities!.Stream.Should().BeTrue();
            model.Capabilities.JsonMode.Should().BeTrue();
            model.Capabilities.Tools.Should().BeTrue();
            model.Capabilities.ToolChoice.Should().Be("auto");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldDeserializeAuth()
        {
            // Lines 407-409: MergeAuth creates new if null
            // Use object format since array format only maps limited fields via LegacyProviderDescriptor
            const string json = @"{""providers"":{""test"":{""slug"":""test"",""models"":[],""auth"":{""type"":""apikey"",""env"":""TEST_KEY""}}}}";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document.Should().NotBeNull();
            document!.Providers.Should().ContainKey("test");
            var provider = document.Providers["test"];
            provider.Auth.Should().NotBeNull();
            provider.Auth!.Type.Should().Be("apikey");
            provider.Auth.Env.Should().Be("TEST_KEY");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldDeserializeTimeouts()
        {
            // Lines 420-422: MergeTimeouts creates new if null
            // Use object format since array format only maps limited fields via LegacyProviderDescriptor
            const string json = @"{""providers"":{""test"":{""slug"":""test"",""models"":[],""timeouts"":{""connect_ms"":5000,""request_ms"":30000}}}}";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers["test"].Timeouts!.ConnectMs.Should().Be(5000);
            document.Providers["test"].Timeouts.RequestMs.Should().Be(30000);
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldDeserializeRetries()
        {
            // Lines 433-435: MergeRetries creates new if null
            // Use object format since array format only maps limited fields via LegacyProviderDescriptor
            const string json = @"{""providers"":{""test"":{""slug"":""test"",""models"":[],""retries"":{""max"":3,""backoff_ms"":1000}}}}";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers["test"].Retries!.Max.Should().Be(3);
            document.Providers["test"].Retries.BackoffMs.Should().Be(1000);
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldDeserializeIntegrity()
        {
            // Lines 446-447: MergeIntegrity creates new if null
            // Use object format since array format only maps limited fields via LegacyProviderDescriptor
            const string json = @"{""providers"":{""test"":{""slug"":""test"",""models"":[],""integrity"":{""sha256"":""abc123""}}}}";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers["test"].Integrity!.Sha256.Should().Be("abc123");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldMergeAliases_WithoutDuplicates()
        {
            // Lines 335-350: MergeModel aliases handling
            const string json = @"{
                ""providers"": [
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"", ""aliases"": [""alias1"", ""alias2""] } ] },
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"", ""aliases"": [""alias2"", ""alias3""] } ] }
                ]
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            var model = document!.Providers["test"].Models.First(m => m.Id == "m1");
            model.Aliases.Should().BeEquivalentTo(new[] { "alias1", "alias2", "alias3" });
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldMergeMetadata_FromSource()
        {
            // Lines 352-359: MergeModel metadata handling
            const string json = @"{
                ""providers"": [
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"", ""metadata"": { ""key1"": ""value1"" } } ] },
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"", ""metadata"": { ""key2"": ""value2"" } } ] }
                ]
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            var model = document!.Providers["test"].Models.First(m => m.Id == "m1");
            model.Metadata!["key1"].Should().Be("value1");
            model.Metadata["key2"].Should().Be("value2");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldUpdateContextTokens_WhenIncomingIsGreater()
        {
            // Lines 363-369: MergeContextTokens logic
            const string json = @"{
                ""providers"": [
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"", ""context_tokens"": 4000 } ] },
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"", ""context_tokens"": 8000 } ] }
                ]
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            var model = document!.Providers["test"].Models.First(m => m.Id == "m1");
            model.ContextTokens.Should().Be(8000);
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldPreserveContextTokens_WhenIncomingIsSmaller()
        {
            // Lines 363-369: MergeContextTokens - keep current if incoming is smaller
            const string json = @"{
                ""providers"": [
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"", ""context_tokens"": 8000 } ] },
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"", ""context_tokens"": 4000 } ] }
                ]
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            var model = document!.Providers["test"].Models.First(m => m.Id == "m1");
            model.ContextTokens.Should().Be(8000);
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldMergeAdditionalProperties_WithoutOverwriting()
        {
            // Lines 451-459: MergeAdditionalProperties - only add if not exists
            const string json = @"{
                ""providers"": [
                    { ""slug"": ""test"", ""extra1"": ""value1"", ""models"": [] },
                    { ""slug"": ""test"", ""extra1"": ""ignored"", ""extra2"": ""value2"", ""models"": [] }
                ]
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers["test"].AdditionalProperties["extra1"].ToString().Should().NotBe("ignored");
            document.Providers["test"].AdditionalProperties.Should().ContainKey("extra2");
        }

        #endregion

        #region EnsureProviderDefaults

        [Fact]
        public void ProvidersFlexibleConverter_ShouldUseKeyAsFallbackForNameAndDisplayName()
        {
            // Lines 256-272: EnsureProviderDefaults uses key as fallback for Name/DisplayName
            // When Name and DisplayName are missing, they default to the dictionary key
            const string json = @"{
                ""providers"": {
                    ""myProvider"": {
                        ""slug"": ""my-slug"",
                        ""models"": []
                    }
                }
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers["myProvider"].Slug.Should().Be("my-slug");
            document.Providers["myProvider"].Name.Should().Be("myProvider");
            document.Providers["myProvider"].DisplayName.Should().Be("myProvider");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldPreferDisplayNameForName_WhenNameIsNull()
        {
            // Line 266: Name = DisplayName ?? fallback
            const string json = @"{
                ""providers"": {
                    ""test"": {
                        ""displayName"": ""My Display Name"",
                        ""models"": []
                    }
                }
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers["test"].Name.Should().Be("My Display Name");
            document.Providers["test"].DisplayName.Should().Be("My Display Name");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldPreferNameForDisplayName_WhenDisplayNameIsNull()
        {
            // Line 270: DisplayName = Name ?? fallback
            const string json = @"{
                ""providers"": {
                    ""test"": {
                        ""name"": ""My Name"",
                        ""models"": []
                    }
                }
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers["test"].DisplayName.Should().Be("My Name");
            document.Providers["test"].Name.Should().Be("My Name");
        }

        #endregion

        #region Prefer Method Coverage

        [Fact]
        public void ProvidersFlexibleConverter_ShouldKeepCurrentLabel_WhenBothLabelsExist()
        {
            // Line 467-469: Prefer returns current if not whitespace
            const string json = @"{
                ""providers"": [
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"", ""label"": ""Original Label"" } ] },
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"", ""label"": ""Ignored Label"" } ] }
                ]
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            var model = document!.Providers["test"].Models.First(m => m.Id == "m1");
            model.Label.Should().Be("Original Label");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldUseIncomingLabel_WhenCurrentIsNull()
        {
            // Line 467-469: Prefer returns incoming when current is null/whitespace
            const string json = @"{
                ""providers"": [
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"" } ] },
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""m1"", ""label"": ""New Label"" } ] }
                ]
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            var model = document!.Providers["test"].Models.First(m => m.Id == "m1");
            model.Label.Should().Be("New Label");
        }

        #endregion

        #region Model Registry Entry Defaults

        [Fact]
        public void ModelRegistryEntry_ShouldHaveDefaultIdAsEmptyString()
        {
            // Line 74: Id defaults to string.Empty
            var entry = new ModelRegistryEntry();

            entry.Id.Should().BeEmpty();
        }

        [Fact]
        public void ModelRegistryDocument_ShouldHaveDefaultProvidersWithIgnoreCase()
        {
            // Lines 25-26: Providers defaults to new dictionary with OrdinalIgnoreCase
            var doc = new ModelRegistryDocument();

            doc.Providers.Should().NotBeNull();
            doc.Providers.Comparer.Should().Be(StringComparer.OrdinalIgnoreCase);
        }

        [Fact]
        public void ModelRegistryProvider_ShouldHaveDefaultModelsList()
        {
            // Line 62: Models defaults to new List
            var provider = new ModelRegistryProvider();

            provider.Models.Should().NotBeNull();
            provider.Models.Should().BeEmpty();
        }

        [Fact]
        public void ModelRegistryProvider_ShouldHaveDefaultAdditionalProperties()
        {
            // Line 65: AdditionalProperties defaults to new Dictionary
            var provider = new ModelRegistryProvider();

            provider.AdditionalProperties.Should().NotBeNull();
            provider.AdditionalProperties.Should().BeEmpty();
        }

        #endregion

        #region Skip Null Models in Dictionary

        [Fact]
        public void ProvidersFlexibleConverter_ShouldSkipNullProviderValues()
        {
            // Lines 166-169: Skip null values in dictionary
            const string json = @"{
                ""providers"": {
                    ""valid"": { ""slug"": ""valid"", ""models"": [] },
                    ""nullValue"": null
                }
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers.Should().ContainKey("valid");
            document.Providers.Should().HaveCount(1);
        }

        #endregion

        #region Skip Null/Empty Model Entries in Merge

        [Fact]
        public void ProvidersFlexibleConverter_ShouldSkipNullModelsInMerge()
        {
            // Lines 311-314: Skip null or empty-id models
            const string json = @"{
                ""providers"": [
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""valid"" } ] },
                    { ""slug"": ""test"", ""models"": [ null, { ""id"": ""another"" } ] }
                ]
            }";

            // Note: JSON serializer will throw on null in list, so test empty id instead
            const string json2 = @"{
                ""providers"": [
                    { ""slug"": ""test"", ""models"": [ { ""id"": ""valid"" } ] },
                    { ""slug"": ""test"", ""models"": [ { ""id"": """" }, { ""id"": ""another"" } ] }
                ]
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json2);

            document!.Providers["test"].Models.Should().HaveCount(2);
            document.Providers["test"].Models.Select(m => m.Id).Should().BeEquivalentTo(new[] { "valid", "another" });
        }

        #endregion

        #region Document Properties

        [Fact]
        public void ModelRegistryDocument_ShouldDeserializeSchema()
        {
            // Line 14-15: Schema property
            const string json = @"{
                ""$schema"": ""https://example.com/schema.json"",
                ""providers"": {}
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Schema.Should().Be("https://example.com/schema.json");
        }

        [Fact]
        public void ModelRegistryDocument_ShouldDeserializeVersion()
        {
            // Line 17-18: Version property
            const string json = @"{
                ""version"": ""2.0"",
                ""providers"": {}
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Version.Should().Be("2.0");
        }

        [Fact]
        public void ModelRegistryDocument_ShouldDeserializeGeneratedAt()
        {
            // Line 20-21: GeneratedAt property
            const string json = @"{
                ""generatedAt"": ""2024-01-15T10:30:00Z"",
                ""providers"": {}
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.GeneratedAt.Should().NotBeNull();
            document.GeneratedAt!.Value.Year.Should().Be(2024);
        }

        #endregion

        #region Provider-Level Properties

        [Fact]
        public void ModelRegistryProvider_ShouldDeserializeEndpoint()
        {
            // Line 43-44: Endpoint property
            const string json = @"{
                ""providers"": {
                    ""test"": {
                        ""endpoint"": ""https://api.test.com/v1"",
                        ""models"": []
                    }
                }
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers["test"].Endpoint.Should().Be("https://api.test.com/v1");
        }

        [Fact]
        public void ModelRegistryProvider_ShouldDeserializeDefaultModel()
        {
            // Line 49-50: DefaultModel property
            const string json = @"{
                ""providers"": {
                    ""test"": {
                        ""defaultModel"": ""gpt-4"",
                        ""models"": []
                    }
                }
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers["test"].DefaultModel.Should().Be("gpt-4");
        }

        #endregion

        #region Whitespace Handling in SelectLegacyKey

        [Fact]
        public void ProvidersFlexibleConverter_ShouldTrimSlugInLegacyKey()
        {
            // Lines 224-225: Trim slug
            const string json = @"{ ""providers"": [ { ""slug"": ""  trimmed-slug  "", ""models"": [] } ] }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers.Should().ContainKey("trimmed-slug");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldTrimIdInLegacyKey()
        {
            // Lines 228-229: Trim id
            const string json = @"{ ""providers"": [ { ""id"": ""  trimmed-id  "", ""name"": ""Test"" } ] }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers.Should().ContainKey("trimmed-id");
        }

        [Fact]
        public void ProvidersFlexibleConverter_ShouldTrimNameInLegacyKey()
        {
            // Lines 232: Trim name
            const string json = @"{ ""providers"": [ { ""name"": ""  trimmed-name  "" } ] }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers.Should().ContainKey("trimmed-name");
        }

        #endregion

        #region Case Insensitivity

        [Fact]
        public void ProvidersFlexibleConverter_ShouldMatchProviderCaseInsensitively()
        {
            // NormalizeKey uses ToLowerInvariant
            const string json = @"{
                ""providers"": [
                    { ""slug"": ""OpenAI"", ""models"": [ { ""id"": ""m1"" } ] },
                    { ""slug"": ""openai"", ""models"": [ { ""id"": ""m2"" } ] }
                ]
            }";

            var document = JsonSerializer.Deserialize<ModelRegistryDocument>(json);

            document!.Providers.Should().HaveCount(1);
            document.Providers["openai"].Models.Should().HaveCount(2);
        }

        #endregion
    }
}
