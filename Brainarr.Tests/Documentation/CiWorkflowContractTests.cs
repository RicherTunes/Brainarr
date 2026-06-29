using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Brainarr.Tests.Documentation
{
    public class CiWorkflowContractTests
    {
        [Fact]
        public void GiteaCiWorkflow_RunsSecretScan()
        {
            var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(), ".gitea", "workflows", "ci.yml"));

            workflow.Should().MatchRegex(@"(?m)^  secret-scan:\s*$");
            workflow.Should().MatchRegex(@"(?ms)^  verify:\s*\r?\n(?:    .*\r?\n)*?    needs:\s*\[lint,\s*secret-scan\]\s*$");
            workflow.Should().Contain("sha256sum -c -");
            workflow.Should().Contain("/tmp/gitleaks detect --source . --no-banner --redact --exit-code 1");
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, ".gitea", "workflows", "ci.yml")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException($"Unable to locate repository root from {AppContext.BaseDirectory}");
        }
    }
}
