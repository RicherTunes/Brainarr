using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.Architecture
{
    /// <summary>
    /// Enforces architectural layering contracts via static analysis.
    /// These tests prevent regressions in layer dependencies.
    /// </summary>
    [Trait("Category", "Architecture")]
    [Trait("Category", "Contract")]
    public class LayeringContractTests
    {
        private static readonly string ProjectRoot = FindProjectRoot();
        private static readonly string CorePath = Path.Combine(ProjectRoot, "Brainarr.Plugin", "Services", "Core");

        private static string FindProjectRoot()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "Brainarr.sln")))
            {
                dir = Directory.GetParent(dir)?.FullName;
            }
            return dir ?? throw new InvalidOperationException("Could not find project root");
        }

        /// <summary>
        /// Core layer must not reference concrete provider implementations in ClaudeCode namespace.
        /// This prevents tight coupling and ensures Core depends only on abstractions.
        ///
        /// Regression guard for fix in commit 5ee501c.
        /// </summary>
        [Fact]
        public void Core_Should_Not_Reference_Providers_ClaudeCode_Namespace()
        {
            // Arrange
            var coreFiles = Directory.GetFiles(CorePath, "*.cs", SearchOption.AllDirectories);
            var violatingPattern = new Regex(
                @"using\s+NzbDrone\.Core\.ImportLists\.Brainarr\.Services\.Providers\.ClaudeCode",
                RegexOptions.Compiled);

            // Act
            var violations = coreFiles
                .SelectMany(file => File.ReadAllLines(file)
                    .Select((line, index) => new { File = file, Line = index + 1, Content = line })
                    .Where(x => violatingPattern.IsMatch(x.Content)))
                .ToList();

            // Assert
            violations.Should().BeEmpty(
                "Core layer must not depend on concrete ClaudeCode provider types. " +
                "Use ClaudeCodeProviderFactory in Providers namespace instead. " +
                $"Found {violations.Count} violation(s): " +
                string.Join(", ", violations.Select(v => $"{Path.GetFileName(v.File)}:{v.Line}")));
        }

        /// <summary>
        /// Core layer must not reference any concrete provider sub-namespaces.
        /// This enforces the general layering rule beyond just ClaudeCode.
        /// </summary>
        [Fact]
        public void Core_Should_Not_Reference_Any_Providers_SubNamespace()
        {
            // Arrange
            var coreFiles = Directory.GetFiles(CorePath, "*.cs", SearchOption.AllDirectories);

            // Match: Services.Providers.<SubNamespace> (anything after Providers. with another dot segment)
            var violatingPattern = new Regex(
                @"NzbDrone\.Core\.ImportLists\.Brainarr\.Services\.Providers\.[A-Z][a-zA-Z0-9]*\.",
                RegexOptions.Compiled);

            // Act
            var violations = coreFiles
                .SelectMany(file => File.ReadAllLines(file)
                    .Select((line, index) => new { File = file, Line = index + 1, Content = line })
                    .Where(x => violatingPattern.IsMatch(x.Content)))
                .ToList();

            // Assert
            violations.Should().BeEmpty(
                "Core layer must not depend on provider sub-namespaces (e.g., Providers.ClaudeCode, Providers.ZaiGlm). " +
                "Core should only reference the main Providers namespace for interfaces and factories. " +
                $"Found {violations.Count} violation(s): " +
                string.Join(", ", violations.Select(v => $"{Path.GetFileName(v.File)}:{v.Line}")));
        }

        /// <summary>
        /// Providers must not depend on Core services (reverse dependency).
        /// This ensures providers are self-contained units.
        /// </summary>
        [Fact]
        public void Providers_Should_Not_Reference_Services_Core()
        {
            // Arrange
            var providersPath = Path.Combine(ProjectRoot, "Brainarr.Plugin", "Services", "Providers");
            if (!Directory.Exists(providersPath))
            {
                return; // Skip if path doesn't exist
            }

            var providerFiles = Directory.GetFiles(providersPath, "*.cs", SearchOption.AllDirectories);
            var violatingPattern = new Regex(
                @"using\s+NzbDrone\.Core\.ImportLists\.Brainarr\.Services\.Core",
                RegexOptions.Compiled);

            // Act
            var violations = providerFiles
                .SelectMany(file => File.ReadAllLines(file)
                    .Select((line, index) => new { File = file, Line = index + 1, Content = line })
                    .Where(x => violatingPattern.IsMatch(x.Content)))
                .ToList();

            // Assert
            violations.Should().BeEmpty(
                "Providers must not depend on Services.Core (reverse dependency violation). " +
                $"Found {violations.Count} violation(s): " +
                string.Join(", ", violations.Select(v => $"{Path.GetFileName(v.File)}:{v.Line}")));
        }
    }
}
