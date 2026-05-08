using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Lidarr.Plugin.Common.Providers.ClaudeCode;
using Lidarr.Plugin.Common.Subprocess;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using Xunit;

namespace Brainarr.Tests.Providers.Llm
{
    /// <summary>
    /// Integration coverage for the wave-4d <see cref="AIProvider.ClaudeCodeCli"/> registration.
    ///
    /// <para>
    /// Verifies that the registry wires up common's <see cref="ClaudeCodeProvider"/> through
    /// the brainarr <see cref="LlmProviderAdapter"/> seam, so existing
    /// <c>IAIProvider</c>-based callers see the CLI-backed Claude implementation transparently.
    /// </para>
    /// </summary>
    [Trait("Category", "Unit")]
    public class ClaudeCodeCliRegistrationTests
    {
        [Fact]
        public void Registry_Registers_ClaudeCodeCli_Enum()
        {
            var registry = new ProviderRegistry();
            registry.IsRegistered(AIProvider.ClaudeCodeCli).Should().BeTrue();
        }

        [Fact]
        public void Registry_Constructs_ClaudeCodeCli_AsAdapter_WrappingCommonProvider()
        {
            var registry = new ProviderRegistry();
            var settings = new BrainarrSettings { Provider = AIProvider.ClaudeCodeCli };
            var http = new Mock<IHttpClient>().Object;
            var logger = LogManager.GetCurrentClassLogger();

            var provider = registry.CreateProvider(AIProvider.ClaudeCodeCli, settings, http, logger);

            provider.Should().NotBeNull();
            // The registry must hand back the adapter, not the raw common provider — that's
            // how brainarr's existing IAIProvider-based callers stay working.
            provider.Should().BeOfType<LlmProviderAdapter>();
            ((LlmProviderAdapter)provider).Inner.Should().BeOfType<ClaudeCodeProvider>();
            // ProviderName should bubble up from the wrapped provider's DisplayName.
            provider.ProviderName.Should().Be("Claude Code CLI");
        }

        [Fact]
        public async Task Adapter_With_MockedCliRunner_RoutesEndToEnd()
        {
            // Manually wire the adapter with a stub CliRunner to verify the cliRunner →
            // ClaudeCodeProvider → LlmProviderAdapter path returns a usable IAIProvider.
            // This is the same wiring the registry produces, just with a controllable runner.
            var stubRunner = new StubCliRunner();
            var detector = new ClaudeCodeDetector(stubRunner);
            ILlmProvider llm = new ClaudeCodeProvider(stubRunner, detector, new ClaudeCodeSettings { Model = "sonnet" });
            var logger = LogManager.GetCurrentClassLogger();

            IAIProvider adapted = new LlmProviderAdapter(llm, logger);

            // CLI not present in test env → CheckHealthAsync via TestConnectionAsync should fail
            // without throwing, which is what the orchestrator depends on.
            var ok = await adapted.TestConnectionAsync(CancellationToken.None);
            ok.Should().BeFalse();
            adapted.ProviderName.Should().Be("Claude Code CLI");
        }

        /// <summary>
        /// Minimal stub: returns "command not found" so the detector's PATH lookup falls
        /// through and the filesystem lookup also misses. Lets us assert end-to-end wiring
        /// without depending on a real `claude` install.
        /// </summary>
        private sealed class StubCliRunner : ICliRunner
        {
            public Task<CliResult> ExecuteAsync(
                string command,
                IReadOnlyList<string> arguments,
                CliRunnerOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(new CliResult
                {
                    ExitCode = 1,
                    StandardOutput = string.Empty,
                    StandardError = "command not found",
                    Duration = TimeSpan.Zero,
                });
            }

            public IAsyncEnumerable<CliStreamEvent> StreamAsync(
                string command,
                IReadOnlyList<string> arguments,
                CliRunnerOptions? options = null,
                CancellationToken cancellationToken = default)
                => EmptyAsync();

            private static async IAsyncEnumerable<CliStreamEvent> EmptyAsync()
            {
                await Task.CompletedTask;
                yield break;
            }
        }
    }
}
