using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.Services.Core;

public sealed class OrchestratorConstructionTripwireTests
{
    private static readonly Regex ConstructorRegex = new(
        @"new\s+BrainarrOrchestrator\s*\(",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void All_BrainarrOrchestrator_Constructions_In_Tests_Pass_BreakerRegistry()
    {
        var repoRoot = FindRepoRoot();
        var testsRoot = Path.Combine(repoRoot, "Brainarr.Tests");

        testsRoot.Should().NotBeNull();
        Directory.Exists(testsRoot).Should().BeTrue($"Expected tests folder at '{testsRoot}'");

        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            foreach (Match match in ConstructorRegex.Matches(content))
            {
                var snippet = content.Substring(match.Index, Math.Min(2000, content.Length - match.Index));
                if (snippet.Contains("breakerRegistry:"))
                {
                    continue;
                }

                var lineNumber = 1 + CountNewlines(content, match.Index);
                violations.Add($"{Path.GetRelativePath(repoRoot, file)}:{lineNumber} missing 'breakerRegistry:' named argument");
            }
        }

        violations.Should().BeEmpty("Release builds require injecting IBreakerRegistry into BrainarrOrchestrator. Add breakerRegistry: PassThroughBreakerRegistry.CreateMock().Object");
    }

    private static int CountNewlines(string content, int upToIndexExclusive)
    {
        var count = 0;
        for (var i = 0; i < upToIndexExclusive && i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Brainarr.Tests", "Brainarr.Tests.csproj")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing Brainarr.Tests/Brainarr.Tests.csproj");
    }
}

