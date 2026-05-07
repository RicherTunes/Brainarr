using System;
using System.Collections.Generic;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using Xunit;

namespace Brainarr.Tests
{
    /// <summary>
    /// Coverage tests for LibraryAwarePromptBuilder.
    /// Target: Brainarr.Plugin/Services/LibraryAwarePromptBuilder.cs
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "PromptBuilder")]
    public class PromptBuilderCovTests
    {
        private static readonly Logger NullLogger = LogManager.CreateNullLogger();

        // ─── Constructor ArgumentNullException paths ─────────────────────────────

        // Source line 68: _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        [Fact]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            var act = () => new LibraryAwarePromptBuilder(
                logger: null!,
                styleCatalog: new StyleCatalogService(NullLogger, null),
                modelRegistryLoader: new ModelRegistryLoader(),
                tokenizerRegistry: null);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("logger", "because null logger must be rejected at line 68");
        }

        // Source lines 69-72:
        //   if (styleCatalog == null)
        //   {
        //       throw new ArgumentNullException(nameof(styleCatalog));
        //   }
        [Fact]
        public void Constructor_WithNullStyleCatalog_ThrowsArgumentNullException()
        {
            var act = () => new LibraryAwarePromptBuilder(
                logger: NullLogger,
                styleCatalog: null!,
                modelRegistryLoader: new ModelRegistryLoader(),
                tokenizerRegistry: null);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("styleCatalog", "because null style catalog must be rejected at lines 69-72");
        }

        // Source line 74: if (modelRegistryLoader == null) throw new ArgumentNullException(nameof(modelRegistryLoader));
        [Fact]
        public void Constructor_WithNullModelRegistryLoader_ThrowsArgumentNullException()
        {
            var act = () => new LibraryAwarePromptBuilder(
                logger: NullLogger,
                styleCatalog: new StyleCatalogService(NullLogger, null),
                modelRegistryLoader: null!,
                tokenizerRegistry: null);

            act.Should().Throw<ArgumentNullException>()
                .Which.ParamName.Should().Be("modelRegistryLoader", "because null model registry loader must be rejected at line 74");
        }

        // ─── EstimateTokens edge cases ───────────────────────────────────────────
        // Source lines 329-331: if (string.IsNullOrEmpty(text)) { return 0; }

        [Fact]
        public void EstimateTokens_WithNullText_ReturnsZero()
        {
            var builder = new LibraryAwarePromptBuilder(NullLogger);
            var result = builder.EstimateTokens(null!);
            result.Should().Be(0, "because null text should return 0 tokens at lines 329-331");
        }

        [Fact]
        public void EstimateTokens_WithEmptyText_ReturnsZero()
        {
            var builder = new LibraryAwarePromptBuilder(NullLogger);
            var result = builder.EstimateTokens(string.Empty);
            result.Should().Be(0, "because empty string should return 0 tokens at lines 329-331");
        }

        [Fact]
        public void EstimateTokens_WithActualText_ReturnsPositiveCount()
        {
            var builder = new LibraryAwarePromptBuilder(NullLogger);
            var result = builder.EstimateTokens("This is a sample prompt text for token estimation");
            result.Should().BePositive("because non-empty text should produce positive token count at lines 334-335");
        }
    }
}
