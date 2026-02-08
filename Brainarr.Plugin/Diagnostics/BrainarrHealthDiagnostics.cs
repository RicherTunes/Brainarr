using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Abstractions.Llm;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace NzbDrone.Core.ImportLists.Brainarr.Diagnostics;

/// <summary>
/// Produces structured <see cref="ProviderHealthResult"/> for Brainarr LLM provider health checks.
/// Follows the same static-utility pattern as the streaming plugins' *HealthDiagnostics classes
/// but uses <see cref="ProviderHealthResult"/> (LLM canonical shape with Model field)
/// instead of <see cref="Lidarr.Plugin.Common.Abstractions.Diagnostics.DiagnosticHealthResult"/>.
/// </summary>
internal static class BrainarrHealthDiagnostics
{
    /// <summary>
    /// Well-known error codes emitted by Brainarr diagnostics.
    /// </summary>
    public static class ErrorCodes
    {
        public const string AuthFailed = "AUTH_FAILED";
        public const string ConnectionFailed = "CONNECTION_FAILED";
        public const string ModelNotFound = "MODEL_NOT_FOUND";
        public const string RateLimited = "RATE_LIMITED";
        public const string Timeout = "TIMEOUT";
        public const string ProviderInitFailed = "PROVIDER_INIT_FAILED";
    }

    /// <summary>
    /// Well-known authentication methods for LLM providers.
    /// </summary>
    public static class AuthMethods
    {
        public const string ApiKey = "apiKey";
        public const string Local = "local";
        public const string Cli = "cli";
    }

    /// <summary>
    /// Tests an LLM provider connection and returns a structured health result.
    /// </summary>
    /// <param name="testConnection">A delegate that tests the provider connection and returns success/error tuple.</param>
    /// <param name="providerName">Provider identifier (e.g., "openai", "ollama").</param>
    /// <param name="authMethod">Authentication method used (e.g., "apiKey", "local").</param>
    /// <param name="model">Model identifier if known (e.g., "gpt-4", "llama3").</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A <see cref="ProviderHealthResult"/> indicating provider health.</returns>
    public static async Task<ProviderHealthResult> CheckProviderAsync(
        Func<Task<(bool Success, string? Error)>> testConnection,
        string providerName,
        string? authMethod = null,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var (success, error) = await testConnection().ConfigureAwait(false);
            sw.Stop();

            return success
                ? ProviderHealthResult.Healthy(
                    responseTime: sw.Elapsed,
                    provider: providerName,
                    authMethod: authMethod,
                    model: model)
                : ProviderHealthResult.Unhealthy(
                    error ?? "Connection test failed",
                    responseTime: sw.Elapsed,
                    provider: providerName,
                    authMethod: authMethod,
                    model: model,
                    errorCode: ErrorCodes.AuthFailed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            sw.Stop();
            return ProviderHealthResult.Unhealthy(
                ex.Message,
                responseTime: sw.Elapsed,
                provider: providerName,
                authMethod: authMethod,
                model: model,
                errorCode: ErrorCodes.Timeout);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return ProviderHealthResult.Unhealthy(
                ex.Message,
                responseTime: sw.Elapsed,
                provider: providerName,
                authMethod: authMethod,
                model: model,
                errorCode: ErrorCodes.ConnectionFailed);
        }
    }

    /// <summary>
    /// Checks whether a model is available from the provider.
    /// </summary>
    /// <param name="modelFound">Whether the requested model was found.</param>
    /// <param name="providerName">Provider identifier.</param>
    /// <param name="model">The model that was checked.</param>
    /// <param name="errorMessage">Optional error message when model not found.</param>
    /// <returns>A <see cref="ProviderHealthResult"/> indicating model availability.</returns>
    public static ProviderHealthResult CheckModelAvailability(
        bool modelFound,
        string providerName,
        string? model = null,
        string? errorMessage = null)
    {
        return modelFound
            ? ProviderHealthResult.Healthy(
                provider: providerName,
                model: model)
            : ProviderHealthResult.Unhealthy(
                errorMessage ?? $"Model '{model}' not found",
                provider: providerName,
                model: model,
                errorCode: ErrorCodes.ModelNotFound);
    }

    /// <summary>
    /// Converts internal <see cref="ProviderMetrics"/> to the canonical <see cref="ProviderHealthResult"/> shape.
    /// Bridges Brainarr's rich internal health monitoring to the Common diagnostics contract.
    /// </summary>
    /// <param name="metrics">The provider's current metrics.</param>
    /// <param name="providerName">Provider identifier.</param>
    /// <param name="authMethod">Authentication method used.</param>
    /// <param name="model">Model identifier if known.</param>
    /// <returns>A <see cref="ProviderHealthResult"/> derived from the metrics.</returns>
    public static ProviderHealthResult FromMetrics(
        ProviderMetrics metrics,
        string providerName,
        string? authMethod = null,
        string? model = null)
    {
        var status = metrics.GetHealthStatus();
        var responseTime = metrics.AverageResponseTimeMs > 0
            ? TimeSpan.FromMilliseconds(metrics.AverageResponseTimeMs)
            : (TimeSpan?)null;

        return status switch
        {
            HealthStatus.Healthy => ProviderHealthResult.Healthy(
                responseTime: responseTime,
                provider: providerName,
                authMethod: authMethod,
                model: model),

            HealthStatus.Degraded => ProviderHealthResult.Degraded(
                BuildDegradedReason(metrics),
                responseTime: responseTime,
                provider: providerName,
                authMethod: authMethod,
                model: model),

            HealthStatus.Unhealthy => ProviderHealthResult.Unhealthy(
                metrics.LastError ?? "Provider unhealthy",
                responseTime: responseTime,
                provider: providerName,
                authMethod: authMethod,
                model: model,
                errorCode: ResolveErrorCode(metrics)),

            // Unknown
            _ => ProviderHealthResult.Healthy(
                provider: providerName,
                authMethod: authMethod,
                model: model),
        };
    }

    private static string BuildDegradedReason(ProviderMetrics metrics)
    {
        if (metrics.ConsecutiveFailures >= 2)
            return $"{metrics.ConsecutiveFailures} consecutive failures";

        if (metrics.TotalRequests > 10 && metrics.SuccessRate < 50)
            return $"Low success rate ({metrics.SuccessRate:F0}%)";

        if (metrics.IsRateLimitNearExhaustion)
            return $"Rate limit near exhaustion ({metrics.RateLimitRemaining} remaining)";

        return "Provider degraded";
    }

    private static string ResolveErrorCode(ProviderMetrics metrics)
    {
        if (metrics.IsAuthValid == false)
            return ErrorCodes.AuthFailed;

        if (metrics.IsRateLimitNearExhaustion)
            return ErrorCodes.RateLimited;

        if (metrics.ConsecutiveFailures >= 5)
            return ErrorCodes.ConnectionFailed;

        return ErrorCodes.ConnectionFailed;
    }
}
