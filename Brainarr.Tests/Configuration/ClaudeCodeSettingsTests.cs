using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Configuration
{
    /// <summary>
    /// Tests for Claude Code settings UX fields.
    /// </summary>
    [Trait("Category", "Configuration")]
    [Trait("Provider", "ClaudeCode")]
    public class ClaudeCodeSettingsTests
    {
        [Fact]
        public void ClaudeCodeModelKind_DefaultsToSonnet4()
        {
            // Arrange & Act
            var settings = new BrainarrSettings();

            // Assert
            settings.ClaudeCodeModel.Should().Be(ClaudeCodeModelKind.Sonnet4);
        }

        [Theory]
        [InlineData(ClaudeCodeModelKind.Sonnet4, "claude-sonnet-4-5-20250514")]
        [InlineData(ClaudeCodeModelKind.Opus4, "claude-opus-4-5-20250514")]
        [InlineData(ClaudeCodeModelKind.Haiku35, "claude-3-5-haiku-20241022")]
        public void GetClaudeCodeModelId_ReturnsCorrectModelId(ClaudeCodeModelKind model, string expectedId)
        {
            // Act
            var modelId = BrainarrConstants.GetClaudeCodeModelId(model);

            // Assert
            modelId.Should().Be(expectedId);
        }

        [Fact]
        public void ClaudeCodeCliPath_DefaultsToNull()
        {
            // Arrange & Act
            var settings = new BrainarrSettings();

            // Assert
            settings.ClaudeCodeCliPath.Should().BeNull();
        }

        [Fact]
        public void ClaudeCodeCliPath_EmptyStringBecomesNull()
        {
            // Arrange
            var settings = new BrainarrSettings();

            // Act
            settings.ClaudeCodeCliPath = "   ";

            // Assert
            settings.ClaudeCodeCliPath.Should().BeNull();
        }

        [Fact]
        public void ClaudeCodeCliPath_PreservesValidPath()
        {
            // Arrange
            var settings = new BrainarrSettings();
            var testPath = "/usr/local/bin/claude";

            // Act
            settings.ClaudeCodeCliPath = testPath;

            // Assert
            settings.ClaudeCodeCliPath.Should().Be(testPath);
        }

        [Fact]
        public void ClaudeCodeCredentialsPath_HasDefaultValue()
        {
            // Arrange & Act
            var settings = new BrainarrSettings();

            // Assert
            settings.ClaudeCodeCredentialsPath.Should().NotBeNullOrEmpty();
            settings.ClaudeCodeCredentialsPath.Should().Contain(".claude");
            settings.ClaudeCodeCredentialsPath.Should().Contain(".credentials.json");
        }

        [Fact]
        public void GetProviderSettings_ForClaudeCode_IncludesAllFields()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.ClaudeCodeSubscription,
                ClaudeCodeModel = ClaudeCodeModelKind.Opus4,
                ClaudeCodeCliPath = "/custom/path/claude"
            };

            // Act
            var providerSettings = settings.GetProviderSettings(AIProvider.ClaudeCodeSubscription);

            // Assert
            providerSettings.Should().ContainKey("credentialsPath");
            providerSettings.Should().ContainKey("cliPath");
            providerSettings.Should().ContainKey("model");
            providerSettings["cliPath"].Should().Be("/custom/path/claude");
            providerSettings["model"].Should().Be("claude-opus-4-5-20250514");
        }

        [Fact]
        public void ModelSelection_ForClaudeCode_UsesEnumWhenModelIdEmpty()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.ClaudeCodeSubscription,
                ClaudeCodeModel = ClaudeCodeModelKind.Haiku35,
                ClaudeCodeModelId = null
            };

            // Act
            var modelSelection = settings.ModelSelection;

            // Assert
            modelSelection.Should().Be("claude-3-5-haiku-20241022");
        }

        [Fact]
        public void ModelSelection_ForClaudeCode_UsesModelIdWhenSet()
        {
            // Arrange
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.ClaudeCodeSubscription,
                ClaudeCodeModel = ClaudeCodeModelKind.Sonnet4,
                ClaudeCodeModelId = "custom-model-override"
            };

            // Act
            var modelSelection = settings.ModelSelection;

            // Assert
            modelSelection.Should().Be("custom-model-override");
        }
    }
}
