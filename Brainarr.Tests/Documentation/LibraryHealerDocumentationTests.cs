using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.Documentation
{
    public class LibraryHealerDocumentationTests
    {
        [Fact]
        public void LibraryHealerDocumentation_Describes_A1_ReadOnly_Contract()
        {
            var root = FindRepositoryRoot();
            var docPath = Path.Combine(root, "docs", "library-healer.md");
            var indexPath = Path.Combine(root, "docs", "README.md");

            File.Exists(docPath).Should().BeTrue("A1 must include user-facing Library Healer documentation");

            var doc = File.ReadAllText(docPath);
            var index = File.ReadAllText(indexPath);

            index.Should().Contain("[Library Healer](library-healer.md)");

            doc.Should().Contain("# Brainarr Library Healer");
            doc.Should().Contain("Milestone A1");
            doc.Should().Contain("read-only diagnostic");
            doc.Should().Contain("healer/scan");
            doc.Should().Contain("healer/getfindings");
            doc.Should().Contain("healer/getfieldcatalog");
            doc.Should().Contain("healer/clearfindings");
            doc.Should().Contain("basename#hash");
            doc.Should().Contain("first 12 hex characters");
            doc.Should().Contain("field-sensitivity catalog");

            doc.Should().Contain("A1 cannot:");
            doc.Should().Contain("- repair files;");
            doc.Should().Contain("- import files;");
            doc.Should().Contain("- delete files;");
            doc.Should().Contain("- replace files;");
            doc.Should().Contain("- enqueue Lidarr rescans or searches;");
            doc.Should().Contain("- write tags;");
            doc.Should().Contain("- call AI providers.");
        }

        private static string FindRepositoryRoot()
        {
            var directory = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "Brainarr.sln")))
                {
                    return directory;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            throw new InvalidOperationException($"Unable to locate repository root from {AppContext.BaseDirectory}");
        }
    }
}
