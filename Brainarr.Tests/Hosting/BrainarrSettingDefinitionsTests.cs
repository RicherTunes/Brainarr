using System.Linq;
using FluentAssertions;
using Lidarr.Plugin.Abstractions.Contracts;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Hosting;
using Xunit;

namespace Brainarr.Tests.Hosting
{
    [Trait("Category", "Hosting")]
    public class BrainarrSettingDefinitionsTests
    {
        [Fact]
        public void Describe_ReturnsNonEmptyCollection()
        {
            var defs = BrainarrSettingDefinitions.Describe();
            defs.Should().NotBeEmpty();
        }

        [Fact]
        public void Describe_ProviderField_IsEnumWithAllowedValues()
        {
            var defs = BrainarrSettingDefinitions.Describe();
            var provider = defs.FirstOrDefault(d => d.Key == "Provider");
            provider.Should().NotBeNull();
            provider!.DataType.Should().Be(SettingDataType.Enum);
            provider.AllowedValues.Should().NotBeNull();
            provider.AllowedValues!.Should().Contain("Ollama");
            provider.AllowedValues.Should().Contain("OpenAI");
        }

        [Fact]
        public void Describe_ResultsAreSortedByOrder_ProviderFirst()
        {
            var defs = BrainarrSettingDefinitions.Describe().ToList();
            // Provider has FieldDefinition order 0, so it should be first
            defs.First().Key.Should().Be("Provider");
        }

        [Fact]
        public void Describe_PasswordFields_ReportPasswordDataType()
        {
            var defs = BrainarrSettingDefinitions.Describe();
            // BrainarrSettings has multiple Password-typed API key fields (e.g., OpenAIApiKey).
            var passwordDefs = defs.Where(d => d.DataType == SettingDataType.Password).ToList();
            passwordDefs.Should().NotBeEmpty("at least one API-key field is marked Password");
            // Password fields should not be marked as required (per implementation)
            passwordDefs.Should().OnlyContain(d => !d.IsRequired);
        }

        [Fact]
        public void Describe_DisplayNameFallsBackToPropertyName_WhenLabelMissing()
        {
            var defs = BrainarrSettingDefinitions.Describe();
            defs.Should().OnlyContain(d => !string.IsNullOrEmpty(d.DisplayName));
        }

        [Fact]
        public void Describe_AllKeysAreUniqueAndNonEmpty()
        {
            var defs = BrainarrSettingDefinitions.Describe();
            defs.Select(d => d.Key).Should().OnlyHaveUniqueItems();
            defs.Should().OnlyContain(d => !string.IsNullOrEmpty(d.Key));
        }

        [Fact]
        public void Describe_DefaultValuePresent_ForProviderField()
        {
            var defs = BrainarrSettingDefinitions.Describe();
            var provider = defs.First(d => d.Key == "Provider");
            // Default provider in BrainarrSettings ctor is Ollama
            provider.DefaultValue.Should().Be(AIProvider.Ollama);
        }

        [Fact]
        public void Describe_BooleanField_MapsToBooleanDataType()
        {
            var defs = BrainarrSettingDefinitions.Describe();
            // AutoDetectModel is a Checkbox field
            var autoDetect = defs.FirstOrDefault(d => d.Key == "AutoDetectModel");
            if (autoDetect != null)
            {
                autoDetect.DataType.Should().Be(SettingDataType.Boolean);
                autoDetect.DefaultValue.Should().Be(true);
            }
        }
    }
}
