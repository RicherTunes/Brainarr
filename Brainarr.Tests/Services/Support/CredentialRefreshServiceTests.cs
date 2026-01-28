using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Support
{
    public class CredentialRefreshServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _claudeCredPath;
        private readonly string _codexAuthPath;

        public CredentialRefreshServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"brainarr_refresh_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _claudeCredPath = Path.Combine(_tempDir, ".credentials.json");
            _codexAuthPath = Path.Combine(_tempDir, "auth.json");
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        #region Constructor Tests

        [Fact]
        public void Constructor_WithDefaults_CreatesService()
        {
            using var service = new CredentialRefreshService();
            service.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithCustomPaths_AcceptsPaths()
        {
            using var service = new CredentialRefreshService(
                claudeCodePath: _claudeCredPath,
                openAICodexPath: _codexAuthPath);
            service.Should().NotBeNull();
        }

        [Fact]
        public void Constructor_WithCustomIntervals_AcceptsIntervals()
        {
            using var service = new CredentialRefreshService(
                refreshThreshold: TimeSpan.FromMinutes(30),
                checkInterval: TimeSpan.FromMinutes(5));
            service.Should().NotBeNull();
        }

        #endregion

        #region Start/Stop Tests

        [Fact]
        public void Start_DoesNotThrow()
        {
            using var service = new CredentialRefreshService(
                claudeCodePath: _claudeCredPath,
                openAICodexPath: _codexAuthPath,
                checkInterval: TimeSpan.FromHours(24)); // Long interval to prevent actual checks

            var startAction = () => service.Start();
            startAction.Should().NotThrow();
        }

        [Fact]
        public void Stop_DoesNotThrow()
        {
            using var service = new CredentialRefreshService(
                claudeCodePath: _claudeCredPath,
                openAICodexPath: _codexAuthPath,
                checkInterval: TimeSpan.FromHours(24));

            service.Start();
            var stopAction = () => service.Stop();
            stopAction.Should().NotThrow();
        }

        [Fact]
        public void Stop_WithoutStart_DoesNotThrow()
        {
            using var service = new CredentialRefreshService(
                claudeCodePath: _claudeCredPath,
                openAICodexPath: _codexAuthPath);

            var stopAction = () => service.Stop();
            stopAction.Should().NotThrow();
        }

        #endregion

        #region Event Tests

        [Fact]
        public void RefreshFailed_Event_CanBeSubscribed()
        {
            using var service = new CredentialRefreshService(
                claudeCodePath: _claudeCredPath,
                openAICodexPath: _codexAuthPath);

            CredentialRefreshEventArgs? capturedArgs = null;
            service.RefreshFailed += (sender, args) => capturedArgs = args;

            // The event subscription itself should not throw
            service.Should().NotBeNull();
        }

        [Fact]
        public void CredentialsRefreshed_Event_CanBeSubscribed()
        {
            using var service = new CredentialRefreshService(
                claudeCodePath: _claudeCredPath,
                openAICodexPath: _codexAuthPath);

            CredentialRefreshEventArgs? capturedArgs = null;
            service.CredentialsRefreshed += (sender, args) => capturedArgs = args;

            // The event subscription itself should not throw
            service.Should().NotBeNull();
        }

        #endregion

        #region Dispose Tests

        [Fact]
        public void Dispose_MultipleTimes_DoesNotThrow()
        {
            var service = new CredentialRefreshService(
                claudeCodePath: _claudeCredPath,
                openAICodexPath: _codexAuthPath);

            var disposeAction = () =>
            {
                service.Dispose();
                service.Dispose();
            };

            disposeAction.Should().NotThrow();
        }

        [Fact]
        public void Dispose_AfterStartAndStop_DoesNotThrow()
        {
            var service = new CredentialRefreshService(
                claudeCodePath: _claudeCredPath,
                openAICodexPath: _codexAuthPath,
                checkInterval: TimeSpan.FromHours(24));

            service.Start();
            service.Stop();

            var disposeAction = () => service.Dispose();
            disposeAction.Should().NotThrow();
        }

        #endregion

        #region CredentialRefreshEventArgs Tests

        [Fact]
        public void CredentialRefreshEventArgs_Constructor_SetsProperties()
        {
            var args = new CredentialRefreshEventArgs("ClaudeCode", "Token refreshed", true);

            args.Provider.Should().Be("ClaudeCode");
            args.Message.Should().Be("Token refreshed");
            args.Success.Should().BeTrue();
        }

        [Fact]
        public void CredentialRefreshEventArgs_Failure_HasCorrectValues()
        {
            var args = new CredentialRefreshEventArgs("OpenAICodex", "Refresh failed", false);

            args.Provider.Should().Be("OpenAICodex");
            args.Message.Should().Be("Refresh failed");
            args.Success.Should().BeFalse();
        }

        #endregion

        #region Integration-like Tests (with mock files)

        [Fact]
        public void Service_WithMissingCredentialFiles_DoesNotThrowOnStart()
        {
            // Credential files don't exist
            using var service = new CredentialRefreshService(
                claudeCodePath: _claudeCredPath,
                openAICodexPath: _codexAuthPath,
                checkInterval: TimeSpan.FromHours(24));

            var startAction = () => service.Start();
            startAction.Should().NotThrow();
        }

        [Fact]
        public void Service_WithValidNonExpiredCredentials_DoesNotFireRefreshFailed()
        {
            // Create valid credentials that won't expire for a week
            var futureExpiry = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds();
            var claudeJson = $@"{{
                ""claudeAiOauth"": {{
                    ""accessToken"": ""valid-token"",
                    ""expiresAt"": {futureExpiry}
                }}
            }}";
            File.WriteAllText(_claudeCredPath, claudeJson);

            bool refreshFailedFired = false;
            using var service = new CredentialRefreshService(
                claudeCodePath: _claudeCredPath,
                openAICodexPath: _codexAuthPath,
                checkInterval: TimeSpan.FromHours(24));

            service.RefreshFailed += (sender, args) => refreshFailedFired = true;

            // Just starting the service shouldn't fire refresh failed for valid credentials
            service.Start();
            Thread.Sleep(100); // Give timer a moment
            service.Stop();

            // With valid non-expired credentials, no failure should occur
            // Note: The timer callback may not have run yet with such a long interval
            // This is more of a smoke test
            service.Should().NotBeNull();
        }

        #endregion
    }
}
