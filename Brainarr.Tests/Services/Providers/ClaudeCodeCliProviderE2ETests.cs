// <copyright file="ClaudeCodeCliProviderE2ETests.cs" company="RicherTunes">
// Copyright (c) RicherTunes. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Providers.ClaudeCode;
using Lidarr.Plugin.Common.Subprocess;
using Xunit;
using Xunit.Abstractions;

namespace Brainarr.Tests.Services.Providers;

/// <summary>
/// PR-hermetic E2E tests for Claude Code CLI streaming.
/// Uses mock CLI script instead of real Claude CLI.
/// Proves partial output is observable before completion.
/// </summary>
public class ClaudeCodeCliProviderE2ETests
{
    private readonly ITestOutputHelper _output;

    public ClaudeCodeCliProviderE2ETests(ITestOutputHelper output)
    {
        _output = output;
    }

    private string GetMockScriptPath()
    {
        // Find mock script relative to test assembly
        var testDir = Path.GetDirectoryName(typeof(ClaudeCodeCliProviderE2ETests).Assembly.Location)!;

        // Navigate up from bin/Debug/net8.0 to project root, then into Fixtures
        var fixturesDir = Path.Combine(testDir, "..", "..", "..", "Fixtures");
        fixturesDir = Path.GetFullPath(fixturesDir);

        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(fixturesDir, "MockClaudeCliScript.ps1")
            : Path.Combine(fixturesDir, "MockClaudeCliScript.sh");
    }

    [Fact]
    [Trait("Category", "E2E")]
    [Trait("Category", "Hermetic")]
    public async Task StreamAsync_MockCli_ObservesPartialOutputBeforeCompletion()
    {
        // Arrange
        var mockScriptPath = GetMockScriptPath();
        _output.WriteLine($"Mock script path: {mockScriptPath}");

        if (!File.Exists(mockScriptPath))
        {
            throw new FileNotFoundException(
                $"Mock CLI script not found. Expected at: {mockScriptPath}. " +
                "Ensure Fixtures are copied to output directory.");
        }

        var cliRunner = new CliRunner();
        var timestamps = new List<DateTime>();
        var linesReceived = new List<string>();

        // Build command to run mock script
        var (executable, args) = BuildMockCliCommand(mockScriptPath);

        var options = new CliRunnerOptions
        {
            Timeout = TimeSpan.FromSeconds(30),
            ThrowOnNonZeroExitCode = false
        };

        // Act - Stream output from mock CLI
        await foreach (var evt in cliRunner.StreamAsync(executable, args, options, CancellationToken.None))
        {
            if (evt is CliStreamEvent.StandardOutput output)
            {
                timestamps.Add(DateTime.UtcNow);
                linesReceived.Add(output.Text);
                _output.WriteLine($"[{timestamps.Count}] {output.Text}");
            }
        }

        // Assert - Verify we received multiple NDJSON lines with timing gaps
        _output.WriteLine($"Total lines received: {linesReceived.Count}");
        Assert.True(linesReceived.Count >= 5, $"Expected at least 5 NDJSON lines, got {linesReceived.Count}");

        // Verify there were actual delays between chunks (proves streaming, not buffered)
        if (timestamps.Count >= 2)
        {
            var anyDelayObserved = false;
            for (int i = 1; i < timestamps.Count; i++)
            {
                var gap = timestamps[i] - timestamps[i - 1];
                _output.WriteLine($"Gap between line {i - 1} and {i}: {gap.TotalMilliseconds:F2}ms");
                if (gap.TotalMilliseconds > 10) // At least 10ms gap proves streaming
                {
                    anyDelayObserved = true;
                    _output.WriteLine($"  -> Streaming delay observed!");
                }
            }

            Assert.True(anyDelayObserved, "No inter-chunk delays observed - output may have been buffered");
        }
    }

    [Fact]
    [Trait("Category", "E2E")]
    [Trait("Category", "Hermetic")]
    public async Task StreamAsync_MockCli_ParsesContentDeltas()
    {
        // Arrange
        var mockScriptPath = GetMockScriptPath();
        _output.WriteLine($"Mock script path: {mockScriptPath}");

        if (!File.Exists(mockScriptPath))
        {
            throw new FileNotFoundException(
                $"Mock CLI script not found. Expected at: {mockScriptPath}. " +
                "Ensure Fixtures are copied to output directory.");
        }

        var cliRunner = new CliRunner();
        var parser = new ClaudeCodeStreamParser();
        var contentPieces = new List<string>();

        var (executable, args) = BuildMockCliCommand(mockScriptPath);

        var options = new CliRunnerOptions
        {
            Timeout = TimeSpan.FromSeconds(30),
            ThrowOnNonZeroExitCode = false
        };

        // Act - Parse streaming output using ClaudeCodeStreamParser
        var streamEvents = cliRunner.StreamAsync(executable, args, options, CancellationToken.None);

        await foreach (var chunk in parser.ParseAsync(streamEvents, CancellationToken.None))
        {
            if (!string.IsNullOrEmpty(chunk.ContentDelta))
            {
                contentPieces.Add(chunk.ContentDelta);
                _output.WriteLine($"Content delta: {chunk.ContentDelta}");
            }

            if (chunk.IsComplete)
            {
                _output.WriteLine("Stream complete");
                if (chunk.FinalUsage != null)
                {
                    _output.WriteLine($"Usage: output_tokens={chunk.FinalUsage.OutputTokens}");
                }
            }
        }

        // Assert - Verify content was received in pieces
        _output.WriteLine($"Content pieces count: {contentPieces.Count}");
        Assert.True(contentPieces.Count >= 2, $"Expected multiple content pieces, got {contentPieces.Count}");

        // Concatenated content should contain our test data
        var fullContent = string.Concat(contentPieces);
        _output.WriteLine($"Full content: {fullContent}");

        Assert.Contains("The Beatles", fullContent);
        Assert.Contains("Abbey Road", fullContent);
        Assert.Contains("artist", fullContent);
        Assert.Contains("album", fullContent);
    }

    [Fact]
    [Trait("Category", "E2E")]
    [Trait("Category", "Hermetic")]
    public async Task StreamAsync_MockCli_CompletesSuccessfully()
    {
        // Arrange
        var mockScriptPath = GetMockScriptPath();
        _output.WriteLine($"Mock script path: {mockScriptPath}");

        if (!File.Exists(mockScriptPath))
        {
            throw new FileNotFoundException(
                $"Mock CLI script not found. Expected at: {mockScriptPath}. " +
                "Ensure Fixtures are copied to output directory.");
        }

        var cliRunner = new CliRunner();
        var parser = new ClaudeCodeStreamParser();
        var completionReceived = false;

        var (executable, args) = BuildMockCliCommand(mockScriptPath);

        var options = new CliRunnerOptions
        {
            Timeout = TimeSpan.FromSeconds(30),
            ThrowOnNonZeroExitCode = false
        };

        // Act
        var streamEvents = cliRunner.StreamAsync(executable, args, options, CancellationToken.None);

        await foreach (var chunk in parser.ParseAsync(streamEvents, CancellationToken.None))
        {
            if (chunk.IsComplete)
            {
                completionReceived = true;
                _output.WriteLine("Completion chunk received");
            }
        }

        // Assert
        Assert.True(completionReceived, "Did not receive completion chunk");
    }

    private static (string executable, List<string> args) BuildMockCliCommand(string mockScriptPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return ("pwsh", new List<string>
            {
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", mockScriptPath,
                "-Prompt", "test"
            });
        }
        else
        {
            // Make script executable first (may be needed on fresh clone)
            // The test will run bash directly with the script
            return ("bash", new List<string> { mockScriptPath });
        }
    }
}
