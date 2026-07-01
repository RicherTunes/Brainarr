using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.Resilience
{
    public class RetryRetirementGuardTests
    {
        private static readonly Regex ObsoleteRetryInvocationPattern = new(
            @"\b(?:RunWithRetriesAsync|WithResilienceAsync)\s*(?:<[^>]+>)?\s*\(",
            RegexOptions.Compiled);

        [Fact]
        public void Production_code_does_not_call_obsolete_local_retry_apis()
        {
            var root = FindRepositoryRoot();
            var pluginRoot = Path.Combine(root, "Brainarr.Plugin");
            var offenders = Directory.EnumerateFiles(pluginRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsGeneratedOrBuildOutput(path))
                .SelectMany(path => FindForbiddenReferences(root, path))
                .ToArray();

            offenders.Should().BeEmpty(
                "production call sites must use Lidarr.Plugin.Common RetryPolicyFactory instead of Brainarr's retired local retry helpers");
        }

        private static IEnumerable<string> FindForbiddenReferences(string root, string path)
        {
            var inBlockComment = false;
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = StripComments(lines[i], ref inBlockComment);
                if (ObsoleteRetryInvocationPattern.IsMatch(code))
                {
                    yield return $"{Path.GetRelativePath(root, path)}:{i + 1}: {lines[i].Trim()}";
                }
            }
        }

        private static string StripComments(string line, ref bool inBlockComment)
        {
            var output = string.Empty;
            var index = 0;

            while (index < line.Length)
            {
                if (inBlockComment)
                {
                    var blockEnd = line.IndexOf("*/", index, StringComparison.Ordinal);
                    if (blockEnd < 0)
                    {
                        return output;
                    }

                    inBlockComment = false;
                    index = blockEnd + 2;
                    continue;
                }

                var lineComment = line.IndexOf("//", index, StringComparison.Ordinal);
                var blockStart = line.IndexOf("/*", index, StringComparison.Ordinal);
                if (lineComment >= 0 && (blockStart < 0 || lineComment < blockStart))
                {
                    output += line.Substring(index, lineComment - index);
                    return output;
                }

                if (blockStart >= 0)
                {
                    output += line.Substring(index, blockStart - index);
                    inBlockComment = true;
                    index = blockStart + 2;
                    continue;
                }

                output += line.Substring(index);
                return output;
            }

            return output;
        }

        private static bool IsGeneratedOrBuildOutput(string path)
        {
            var normalized = path.Replace('\\', '/');
            return normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Brainarr.Plugin")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root containing Brainarr.Plugin.");
        }
    }
}
