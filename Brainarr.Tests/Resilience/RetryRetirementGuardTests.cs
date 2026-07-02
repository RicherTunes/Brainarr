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

        private static readonly Regex LocalRetryWrapperCallPattern = new(
            @"(?:BrainarrRetryPolicyFactory\s*\.\s*Create\w*\s*\(|new\s+ExponentialBackoffRetryPolicy\s*\()",
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

        [Fact]
        public void Production_code_does_not_create_brainarr_retry_wrappers()
        {
            var root = FindRepositoryRoot();
            var pluginRoot = Path.Combine(root, "Brainarr.Plugin");
            var offenders = Directory.EnumerateFiles(pluginRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsGeneratedOrBuildOutput(path))
                .Where(path => !Path.GetRelativePath(root, path).Replace('\\', '/')
                    .Equals("Brainarr.Plugin/Services/RetryPolicy.cs", StringComparison.OrdinalIgnoreCase))
                .SelectMany(path => FindForbiddenReferences(root, path, LocalRetryWrapperCallPattern))
                .ToArray();

            offenders.Should().BeEmpty(
                "new production retry call sites must use Lidarr.Plugin.Common RetryPolicyFactory directly; Brainarr's retry wrapper is compatibility-only");
        }

        [Fact]
        public void Model_detection_uses_common_local_provider_retry_policy()
        {
            var root = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(root, "Brainarr.Plugin", "Services", "ModelDetectionService.cs"));

            Regex.Matches(source, @"RetryPolicyFactory\s*\.\s*CreateForLocalProviders\s*\(")
                .Count
                .Should().Be(2,
                    "Ollama and LM Studio model detection must both use Common's local-provider retry preset");
        }

        [Fact]
        public void Provider_invoker_uses_common_retry_policy_factory()
        {
            var root = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(root, "Brainarr.Plugin", "Services", "Core", "ProviderInvoker.cs"));

            source.Should().Contain("RetryPolicyFactory.Create(",
                "provider invocation should use the Common retry factory rather than Brainarr-local retry helpers");
            LocalRetryWrapperCallPattern.IsMatch(source).Should().BeFalse(
                "provider invocation must not construct Brainarr's compatibility retry wrappers");
        }

        [Fact]
        public void Provider_manager_does_not_keep_dead_retry_or_limiter_dependencies()
        {
            var root = FindRepositoryRoot();
            var source = File.ReadAllText(Path.Combine(root, "Brainarr.Plugin", "Services", "Core", "ProviderManager.cs"));

            source.Should().NotContain("IRetryPolicy",
                "ProviderManager should not retain the retired retry abstraction when it does not execute provider calls");
            source.Should().NotContain("IRateLimiter",
                "ProviderManager should not retain an unused limiter dependency");
        }

        private static IEnumerable<string> FindForbiddenReferences(string root, string path)
            => FindForbiddenReferences(root, path, ObsoleteRetryInvocationPattern);

        private static IEnumerable<string> FindForbiddenReferences(string root, string path, Regex pattern)
        {
            var inBlockComment = false;
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                var code = StripComments(lines[i], ref inBlockComment);
                if (pattern.IsMatch(code))
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
