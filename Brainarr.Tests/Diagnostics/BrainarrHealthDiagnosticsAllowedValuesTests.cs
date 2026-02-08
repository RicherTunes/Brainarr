using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using NzbDrone.Core.ImportLists.Brainarr.Diagnostics;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Diagnostics;

/// <summary>
/// Validates that all ProviderHealthResult instances produced by BrainarrHealthDiagnostics
/// use only well-known, registered error codes and auth methods.
/// Prevents "stringly-typed" drift over time.
/// </summary>
public class BrainarrHealthDiagnosticsAllowedValuesTests
{
    private static readonly HashSet<string> AllowedErrorCodes = new(StringComparer.Ordinal)
    {
        BrainarrHealthDiagnostics.ErrorCodes.AuthFailed,
        BrainarrHealthDiagnostics.ErrorCodes.ConnectionFailed,
        BrainarrHealthDiagnostics.ErrorCodes.ModelNotFound,
        BrainarrHealthDiagnostics.ErrorCodes.RateLimited,
        BrainarrHealthDiagnostics.ErrorCodes.Timeout,
        BrainarrHealthDiagnostics.ErrorCodes.ProviderInitFailed,
    };

    private static readonly HashSet<string> AllowedAuthMethods = new(StringComparer.Ordinal)
    {
        BrainarrHealthDiagnostics.AuthMethods.ApiKey,
        BrainarrHealthDiagnostics.AuthMethods.Local,
        BrainarrHealthDiagnostics.AuthMethods.Cli,
    };

    private static void AssertAllowedValues(ProviderHealthResult result, string context)
    {
        if (result.ErrorCode is not null)
        {
            AllowedErrorCodes.Should().Contain(result.ErrorCode,
                because: $"ErrorCode '{result.ErrorCode}' from {context} must be a registered value");
        }

        if (result.AuthMethod is not null)
        {
            AllowedAuthMethods.Should().Contain(result.AuthMethod,
                because: $"AuthMethod '{result.AuthMethod}' from {context} must be a registered value");
        }
    }

    // --- CheckProviderAsync ---

    [Fact]
    public async Task CheckProviderAsync_Success_UsesOnlyRegisteredValues()
    {
        var result = await BrainarrHealthDiagnostics.CheckProviderAsync(
            () => Task.FromResult<(bool, string?)>((true, null)),
            providerName: "openai",
            authMethod: BrainarrHealthDiagnostics.AuthMethods.ApiKey,
            model: "gpt-4");

        AssertAllowedValues(result, "CheckProviderAsync(success)");
        result.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public async Task CheckProviderAsync_Failure_UsesOnlyRegisteredValues()
    {
        var result = await BrainarrHealthDiagnostics.CheckProviderAsync(
            () => Task.FromResult<(bool, string?)>((false, "invalid key")),
            providerName: "openai",
            authMethod: BrainarrHealthDiagnostics.AuthMethods.ApiKey);

        AssertAllowedValues(result, "CheckProviderAsync(failure)");
        result.IsHealthy.Should().BeFalse();
        result.ErrorCode.Should().Be(BrainarrHealthDiagnostics.ErrorCodes.AuthFailed);
    }

    [Fact]
    public async Task CheckProviderAsync_Exception_UsesOnlyRegisteredValues()
    {
        var result = await BrainarrHealthDiagnostics.CheckProviderAsync(
            () => throw new InvalidOperationException("test"),
            providerName: "anthropic",
            authMethod: BrainarrHealthDiagnostics.AuthMethods.ApiKey);

        AssertAllowedValues(result, "CheckProviderAsync(exception)");
        result.IsHealthy.Should().BeFalse();
        result.ErrorCode.Should().Be(BrainarrHealthDiagnostics.ErrorCodes.ConnectionFailed);
    }

    [Fact]
    public async Task CheckProviderAsync_Timeout_UsesOnlyRegisteredValues()
    {
        var result = await BrainarrHealthDiagnostics.CheckProviderAsync(
            () => throw new TimeoutException("timed out"),
            providerName: "ollama",
            authMethod: BrainarrHealthDiagnostics.AuthMethods.Local);

        AssertAllowedValues(result, "CheckProviderAsync(timeout)");
        result.IsHealthy.Should().BeFalse();
        result.ErrorCode.Should().Be(BrainarrHealthDiagnostics.ErrorCodes.Timeout);
    }

    // --- CheckModelAvailability ---

    [Fact]
    public void CheckModelAvailability_Found_UsesOnlyRegisteredValues()
    {
        var result = BrainarrHealthDiagnostics.CheckModelAvailability(
            modelFound: true,
            providerName: "ollama",
            model: "llama3");

        AssertAllowedValues(result, "CheckModelAvailability(found)");
        result.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public void CheckModelAvailability_NotFound_UsesOnlyRegisteredValues()
    {
        var result = BrainarrHealthDiagnostics.CheckModelAvailability(
            modelFound: false,
            providerName: "ollama",
            model: "nonexistent");

        AssertAllowedValues(result, "CheckModelAvailability(notFound)");
        result.IsHealthy.Should().BeFalse();
        result.ErrorCode.Should().Be(BrainarrHealthDiagnostics.ErrorCodes.ModelNotFound);
    }

    // --- FromMetrics ---

    [Fact]
    public void FromMetrics_Healthy_UsesOnlyRegisteredValues()
    {
        var metrics = new ProviderMetrics
        {
            TotalRequests = 20,
            SuccessfulRequests = 19,
            FailedRequests = 1,
            ConsecutiveFailures = 0,
            AverageResponseTimeMs = 450,
            IsAuthValid = true,
        };

        var result = BrainarrHealthDiagnostics.FromMetrics(
            metrics, "openai",
            authMethod: BrainarrHealthDiagnostics.AuthMethods.ApiKey,
            model: "gpt-4");

        AssertAllowedValues(result, "FromMetrics(healthy)");
        result.IsHealthy.Should().BeTrue();
    }

    [Fact]
    public void FromMetrics_Degraded_ConsecutiveFailures_UsesOnlyRegisteredValues()
    {
        var metrics = new ProviderMetrics
        {
            TotalRequests = 10,
            SuccessfulRequests = 8,
            FailedRequests = 2,
            ConsecutiveFailures = 2,
            AverageResponseTimeMs = 800,
        };

        var result = BrainarrHealthDiagnostics.FromMetrics(
            metrics, "anthropic",
            authMethod: BrainarrHealthDiagnostics.AuthMethods.ApiKey);

        AssertAllowedValues(result, "FromMetrics(degraded-failures)");
        result.IsHealthy.Should().BeTrue();
        result.StatusMessage.Should().Contain("Degraded");
    }

    [Fact]
    public void FromMetrics_Degraded_LowSuccessRate_UsesOnlyRegisteredValues()
    {
        var metrics = new ProviderMetrics
        {
            TotalRequests = 20,
            SuccessfulRequests = 8,
            FailedRequests = 12,
            ConsecutiveFailures = 1,
            AverageResponseTimeMs = 200,
        };

        var result = BrainarrHealthDiagnostics.FromMetrics(
            metrics, "deepseek",
            authMethod: BrainarrHealthDiagnostics.AuthMethods.ApiKey);

        AssertAllowedValues(result, "FromMetrics(degraded-lowRate)");
        result.IsHealthy.Should().BeTrue();
        result.StatusMessage.Should().Contain("Degraded");
    }

    [Fact]
    public void FromMetrics_Degraded_RateLimitNear_UsesOnlyRegisteredValues()
    {
        var metrics = new ProviderMetrics
        {
            TotalRequests = 50,
            SuccessfulRequests = 48,
            FailedRequests = 2,
            ConsecutiveFailures = 0,
            RateLimitRemaining = 3,
            RateLimitResetAt = DateTime.UtcNow.AddMinutes(5),
        };

        var result = BrainarrHealthDiagnostics.FromMetrics(
            metrics, "openai",
            authMethod: BrainarrHealthDiagnostics.AuthMethods.ApiKey);

        AssertAllowedValues(result, "FromMetrics(degraded-rateLimit)");
        result.IsHealthy.Should().BeTrue();
        result.StatusMessage.Should().Contain("Degraded");
    }

    [Fact]
    public void FromMetrics_Unhealthy_AuthInvalid_UsesOnlyRegisteredValues()
    {
        var metrics = new ProviderMetrics
        {
            TotalRequests = 5,
            SuccessfulRequests = 2,
            FailedRequests = 3,
            ConsecutiveFailures = 3,
            IsAuthValid = false,
            LastError = "Invalid API key",
        };

        var result = BrainarrHealthDiagnostics.FromMetrics(
            metrics, "gemini",
            authMethod: BrainarrHealthDiagnostics.AuthMethods.ApiKey);

        AssertAllowedValues(result, "FromMetrics(unhealthy-auth)");
        result.IsHealthy.Should().BeFalse();
        result.ErrorCode.Should().Be(BrainarrHealthDiagnostics.ErrorCodes.AuthFailed);
    }

    [Fact]
    public void FromMetrics_Unhealthy_ConsecutiveFailures_UsesOnlyRegisteredValues()
    {
        var metrics = new ProviderMetrics
        {
            TotalRequests = 10,
            SuccessfulRequests = 3,
            FailedRequests = 7,
            ConsecutiveFailures = 5,
            LastError = "Server unavailable",
        };

        var result = BrainarrHealthDiagnostics.FromMetrics(
            metrics, "groq",
            authMethod: BrainarrHealthDiagnostics.AuthMethods.ApiKey);

        AssertAllowedValues(result, "FromMetrics(unhealthy-failures)");
        result.IsHealthy.Should().BeFalse();
        result.ErrorCode.Should().Be(BrainarrHealthDiagnostics.ErrorCodes.ConnectionFailed);
    }

    [Fact]
    public void FromMetrics_Unknown_UsesOnlyRegisteredValues()
    {
        var metrics = new ProviderMetrics();

        var result = BrainarrHealthDiagnostics.FromMetrics(
            metrics, "lmstudio",
            authMethod: BrainarrHealthDiagnostics.AuthMethods.Local);

        AssertAllowedValues(result, "FromMetrics(unknown)");
        result.IsHealthy.Should().BeTrue();
    }

    // --- Constants non-empty ---

    [Fact]
    public void ErrorCodes_AreNotEmpty()
    {
        BrainarrHealthDiagnostics.ErrorCodes.AuthFailed.Should().NotBeNullOrWhiteSpace();
        BrainarrHealthDiagnostics.ErrorCodes.ConnectionFailed.Should().NotBeNullOrWhiteSpace();
        BrainarrHealthDiagnostics.ErrorCodes.ModelNotFound.Should().NotBeNullOrWhiteSpace();
        BrainarrHealthDiagnostics.ErrorCodes.RateLimited.Should().NotBeNullOrWhiteSpace();
        BrainarrHealthDiagnostics.ErrorCodes.Timeout.Should().NotBeNullOrWhiteSpace();
        BrainarrHealthDiagnostics.ErrorCodes.ProviderInitFailed.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AuthMethods_AreNotEmpty()
    {
        BrainarrHealthDiagnostics.AuthMethods.ApiKey.Should().NotBeNullOrWhiteSpace();
        BrainarrHealthDiagnostics.AuthMethods.Local.Should().NotBeNullOrWhiteSpace();
        BrainarrHealthDiagnostics.AuthMethods.Cli.Should().NotBeNullOrWhiteSpace();
    }
}
