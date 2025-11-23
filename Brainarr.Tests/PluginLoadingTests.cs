using System;
using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NLog;
using Brainarr.Tests.Helpers;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Tests to verify the plugin loads correctly across different .NET runtime versions.
    /// These tests help prevent issues like #268 where plugins fail to load on .NET 8.
    /// </summary>
    [Trait("Category", "PluginLoading")]
    public class PluginLoadingTests
    {
        private readonly Mock<IHttpClient> _httpClientMock;
        private readonly Mock<IImportListStatusService> _importListStatusServiceMock;
        private readonly Mock<IConfigService> _configServiceMock;
        private readonly Mock<IParsingService> _parsingServiceMock;
        private readonly Mock<IArtistService> _artistServiceMock;
        private readonly Mock<IAlbumService> _albumServiceMock;
        private readonly Logger _logger;

        public PluginLoadingTests()
        {
            _httpClientMock = new Mock<IHttpClient>();
            _importListStatusServiceMock = new Mock<IImportListStatusService>();
            _configServiceMock = new Mock<IConfigService>();
            _parsingServiceMock = new Mock<IParsingService>();
            _artistServiceMock = new Mock<IArtistService>();
            _albumServiceMock = new Mock<IAlbumService>();
            _logger = TestLogger.CreateNullLogger();
        }

        [Fact]
        public void Plugin_Assembly_ShouldBeCompiledForCorrectTargetFramework()
        {
            // Arrange
            var assembly = typeof(BrainarrImportList).Assembly;
            var targetFrameworkAttribute = assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>();

            // Assert
            targetFrameworkAttribute.Should().NotBeNull("the assembly should have a TargetFramework attribute");

            var frameworkName = targetFrameworkAttribute!.FrameworkName;

            // The plugin should be compiled for either .NET 6.0 or .NET 8.0
            frameworkName.Should().Match(fw =>
                fw.Contains(".NETCoreApp,Version=v6.0") ||
                fw.Contains(".NETCoreApp,Version=v8.0"),
                "the plugin must target .NET 6.0 or .NET 8.0 to support all Lidarr instances");

            _logger.Info($"Plugin compiled for: {frameworkName}");
        }

        [Fact]
        public void Plugin_ShouldInstantiate_WithoutThrowingTypeLoadException()
        {
            // Act
            Action act = () =>
            {
                var plugin = new Brainarr(
                    _httpClientMock.Object,
                    _importListStatusServiceMock.Object,
                    _configServiceMock.Object,
                    _parsingServiceMock.Object,
                    _artistServiceMock.Object,
                    _albumServiceMock.Object,
                    _logger);
            };

            // Assert - This would throw TypeLoadException if interface signatures don't match
            act.Should().NotThrow<TypeLoadException>(
                "the plugin should load without TypeLoadException on the current .NET runtime");
        }

        [Fact]
        public void Plugin_ShouldImplementIImportList_Correctly()
        {
            // Arrange
            var plugin = new Brainarr(
                _httpClientMock.Object,
                _importListStatusServiceMock.Object,
                _configServiceMock.Object,
                _parsingServiceMock.Object,
                _artistServiceMock.Object,
                _albumServiceMock.Object,
                _logger);

            // Assert
            plugin.Should().BeAssignableTo<ImportListBase<BrainarrSettings>>(
                "Brainarr must properly inherit from ImportListBase");

            // Verify critical interface members are implemented
            typeof(BrainarrImportList).Should().HaveMethod("Fetch", new Type[0],
                "Fetch method must be implemented per IImportList contract");
        }

        [Fact]
        public void Plugin_TestMethod_ShouldBeAccessible()
        {
            // Arrange
            var plugin = new Brainarr(
                _httpClientMock.Object,
                _importListStatusServiceMock.Object,
                _configServiceMock.Object,
                _parsingServiceMock.Object,
                _artistServiceMock.Object,
                _albumServiceMock.Object,
                _logger);

            var failures = new List<ValidationFailure>();

            // Act - The TestConfiguration method wraps the protected Test method
            Action act = () => plugin.TestConfiguration(failures);

            // Assert - This verifies the Test method exists and is callable
            act.Should().NotThrow(
                "the Test method should be accessible and not throw TypeLoadException (issue #268)");
        }

        [Fact]
        public void Plugin_FetchMethod_ShouldBeAccessible()
        {
            // Arrange
            var plugin = new Brainarr(
                _httpClientMock.Object,
                _importListStatusServiceMock.Object,
                _configServiceMock.Object,
                _parsingServiceMock.Object,
                _artistServiceMock.Object,
                _albumServiceMock.Object,
                _logger);

            // Act
            Action act = () => plugin.Fetch();

            // Assert
            act.Should().NotThrow<TypeLoadException>(
                "Fetch should not throw TypeLoadException even if it throws other exceptions");
        }

        [Fact]
        public void Plugin_ShouldHaveCorrectAssemblyName()
        {
            // Arrange
            var assembly = typeof(BrainarrImportList).Assembly;

            // Assert
            assembly.GetName().Name.Should().Be("Lidarr.Plugin.Brainarr",
                "the assembly must have the correct name for Lidarr to load it");
        }

        [Fact]
        public void Plugin_ShouldExposeRequiredMetadata()
        {
            // Arrange
            var plugin = new Brainarr(
                _httpClientMock.Object,
                _importListStatusServiceMock.Object,
                _configServiceMock.Object,
                _parsingServiceMock.Object,
                _artistServiceMock.Object,
                _albumServiceMock.Object,
                _logger);

            // Assert
            plugin.Name.Should().NotBeNullOrWhiteSpace("plugin must have a name");
            plugin.ListType.Should().Be(ImportListType.Program,
                "Brainarr should be identified as a Program import list");
            plugin.MinRefreshInterval.Should().BeGreaterThan(TimeSpan.Zero,
                "plugin must specify a minimum refresh interval");
        }

        [Fact]
        public void CurrentRuntime_ShouldBeDetectable()
        {
            // Arrange & Act
            var runtimeVersion = Environment.Version;
            var frameworkDescription = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

            // Assert
            runtimeVersion.Should().NotBeNull("runtime version should be detectable");
            frameworkDescription.Should().NotBeNullOrWhiteSpace(
                "framework description should be available");

            _logger.Info($"Running on: {frameworkDescription} (Version: {runtimeVersion})");

            // Log this for debugging multi-targeting issues
            if (frameworkDescription.Contains(".NET 6"))
            {
                _logger.Info("Test is running on .NET 6 runtime");
            }
            else if (frameworkDescription.Contains(".NET 8"))
            {
                _logger.Info("Test is running on .NET 8 runtime");
            }
            else
            {
                _logger.Warn($"Test is running on unexpected runtime: {frameworkDescription}");
            }
        }

        [Fact]
        public void Plugin_Dependencies_ShouldLoadWithoutConflicts()
        {
            // Arrange
            Action act = () =>
            {
                var plugin = new Brainarr(
                    _httpClientMock.Object,
                    _importListStatusServiceMock.Object,
                    _configServiceMock.Object,
                    _parsingServiceMock.Object,
                    _artistServiceMock.Object,
                    _albumServiceMock.Object,
                    _logger);

                // Touch the orchestrator to ensure DI container loads
                _ = plugin.Name;
            };

            // Assert - Verifies Microsoft.Extensions.* packages load correctly
            act.Should().NotThrow<System.IO.FileLoadException>(
                "all dependencies should load without version conflicts");
            act.Should().NotThrow<System.IO.FileNotFoundException>(
                "all required assemblies should be present");
        }
    }
}
