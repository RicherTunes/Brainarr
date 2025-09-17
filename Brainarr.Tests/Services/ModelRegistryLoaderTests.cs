using System;
using System.IO;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services
{
    [Trait("Category", "Unit")]
    public class ModelRegistryLoaderTests : IDisposable
    {
        private readonly string _publishRoot;

        public ModelRegistryLoaderTests()
        {
            _publishRoot = Path.Combine(Path.GetTempPath(), "brainarr-publish-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_publishRoot);
        }

        [Fact]
        public void Load_Should_Fall_Back_To_Embedded_Resource_When_Publish_Folder_Lacks_Registry()
        {
            // Arrange - mimic publish output without the docs folder
            CopyPluginAssembly(_publishRoot);
            var loader = new ModelRegistryLoader(_publishRoot);

            // Act
            var registry = loader.Load();

            // Assert
            registry.Should().NotBeNull();
            registry.Providers.Should().ContainKey("ollama");
            registry.Providers["ollama"].Models.Should().NotBeEmpty();
        }

        [Fact]
        public void Load_Should_Use_On_Disk_File_When_Available()
        {
            // Arrange
            var docsDir = Path.Combine(_publishRoot, "docs");
            Directory.CreateDirectory(docsDir);
            var registryPath = Path.Combine(docsDir, "models.example.json");
            File.WriteAllText(registryPath, "{ \"providers\": { \"test\": { \"displayName\": \"Test\", \"models\": [{ \"id\": \"m1\", \"label\": \"Model 1\" }] } } }");
            var loader = new ModelRegistryLoader(_publishRoot);

            // Act
            var registry = loader.Load();

            // Assert
            registry.Providers.Should().ContainKey("test");
            registry.Providers["test"].Models.Should().ContainSingle(m => m.Id == "m1");
        }

        private static void CopyPluginAssembly(string destination)
        {
            var assemblyPath = typeof(ModelRegistryLoader).Assembly.Location;
            var targetPath = Path.Combine(destination, Path.GetFileName(assemblyPath));
            File.Copy(assemblyPath, targetPath, overwrite: true);
        }

        public void Dispose()
        {
            if (Directory.Exists(_publishRoot))
            {
                try
                {
                    Directory.Delete(_publishRoot, recursive: true);
                }
                catch
                {
                    // Ignore cleanup failures in tests
                }
            }
        }
    }
}
