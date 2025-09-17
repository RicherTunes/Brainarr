using System;
using System.IO;
using System.Threading.Tasks;
using Brainarr.Tests.Helpers;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    [Trait("Category", "Unit")]
    public class ModelRegistryLoaderTests
    {
        private static readonly Logger Logger = TestLogger.CreateNullLogger();
        private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        [Fact]
        public async Task LoadAsync_WithExplicitPath_LoadsRegistry()
        {
            var loader = new ModelRegistryLoader(Logger);
            var samplePath = Path.Combine(RepoRoot, "docs", "models.example.json");

            var document = await loader.LoadAsync(samplePath);

            document.Should().NotBeNull();
            document.Providers.Should().NotBeNull();
            document.Providers!.Should().ContainKey("openai");
            document.Providers!["openai"].Models.Should().ContainKey("GPT41_Mini");
        }

        [Fact]
        public async Task LoadAsync_WithoutExplicitPath_FallsBackToPublishLayout()
        {
            var loader = new ModelRegistryLoader(Logger);
            var samplePath = Path.Combine(RepoRoot, "docs", "models.example.json");
            var publishRoot = CreateTemporaryPublishLayout(samplePath);

            try
            {
                var document = await loader.LoadAsync(null, new[] { publishRoot });

                document.Providers.Should().NotBeNull();
                document.Providers!.Should().ContainKey("anthropic");
                document.Providers!["anthropic"].DefaultModel.Should().Be("ClaudeSonnet4");
            }
            finally
            {
                TryDeleteDirectory(publishRoot);
            }
        }

        private static string CreateTemporaryPublishLayout(string source)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "brainarr-model-registry-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            var docsDir = Path.Combine(tempRoot, "docs");
            Directory.CreateDirectory(docsDir);

            File.Copy(source, Path.Combine(docsDir, "models.example.json"), overwrite: true);

            var schemaSource = Path.Combine(Path.GetDirectoryName(source) ?? RepoRoot, "models.schema.json");
            if (File.Exists(schemaSource))
            {
                File.Copy(schemaSource, Path.Combine(docsDir, "models.schema.json"), overwrite: true);
            }

            return tempRoot;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
