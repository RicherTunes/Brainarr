using System;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core;

/// <summary>
/// Manages AI provider lifecycle: initialization, health checks, connection testing, and status reporting.
/// Extracted from <see cref="BrainarrOrchestrator"/> to isolate provider state management.
/// </summary>
internal sealed class ProviderLifecycleService
{
    private readonly Logger _logger;
    private readonly IProviderFactory _providerFactory;
    private readonly IProviderHealthMonitor _providerHealth;
    private readonly IHttpClient _httpClient;

    public ProviderLifecycleService(
        Logger logger,
        IProviderFactory providerFactory,
        IProviderHealthMonitor providerHealth,
        IHttpClient httpClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
        _providerHealth = providerHealth ?? throw new ArgumentNullException(nameof(providerHealth));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public IAIProvider CurrentProvider { get; private set; }
    public AIProvider? CurrentProviderType { get; private set; }

    public void InitializeProvider(BrainarrSettings settings)
    {
        if (CurrentProvider != null && CurrentProviderType.HasValue && CurrentProviderType.Value == settings.Provider)
        {
            _logger.Debug($"Provider {settings.Provider} already initialized for current settings");
            return;
        }

        _logger.Info($"Initializing provider: {settings.Provider}");

        try
        {
            CurrentProvider = _providerFactory.CreateProvider(settings, _httpClient, _logger);
            if (CurrentProvider == null)
            {
                throw new InvalidOperationException("ProviderFactory.CreateProvider returned null");
            }
            CurrentProviderType = settings.Provider;
            try { EventLogger.Log(_logger, BrainarrEvent.ProviderSelected, $"provider={CurrentProviderType} model={settings.EffectiveModel}"); }
            catch (Exception ex) { _logger.Debug(ex, "Non-critical: Failed to log provider selected event"); }
            _logger.Info($"Successfully initialized {settings.Provider} provider");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to initialize {settings.Provider} provider");
            CurrentProvider = null;
            throw;
        }
    }

    public void UpdateProviderConfiguration(BrainarrSettings settings)
    {
        InitializeProvider(settings);
    }

    public async Task<bool> TestProviderConnectionAsync(BrainarrSettings settings)
    {
        try
        {
            InitializeProvider(settings);

            if (CurrentProvider == null)
                return false;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var testResult = await CurrentProvider.TestConnectionAsync();
            sw.Stop();
            if (testResult)
            {
                _providerHealth.RecordSuccess(CurrentProvider.ProviderName, sw.Elapsed.TotalMilliseconds);
                _providerHealth.RecordAuthResult(CurrentProvider.ProviderName, true);
                if (CurrentProvider is BaseCloudProvider cloud && cloud.LastRateLimitInfo is { } rateLimit)
                {
                    _providerHealth.RecordRateLimitInfo(CurrentProvider.ProviderName, rateLimit.Remaining, rateLimit.ResetAt);
                }
            }
            else
            {
                _providerHealth.RecordFailure(CurrentProvider.ProviderName, "Connection test failed");
                _providerHealth.RecordAuthResult(CurrentProvider.ProviderName, false);
            }
            _logger.Debug($"Provider connection test result: {testResult}");
            return testResult;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Provider connection test failed");
            if (CurrentProvider != null)
            {
                _providerHealth.RecordFailure(CurrentProvider.ProviderName, ex.Message);
                _providerHealth.RecordAuthResult(CurrentProvider.ProviderName, false);
            }
            return false;
        }
    }

    public bool IsProviderHealthy()
    {
        if (CurrentProvider == null)
            return false;

        return _providerHealth.IsHealthy(CurrentProvider.ProviderName);
    }

    public string GetProviderStatus()
    {
        if (CurrentProvider == null)
            return "Not Initialized";

        var providerName = CurrentProvider.ProviderName;
        var isHealthy = _providerHealth.IsHealthy(providerName);

        return $"{providerName}: {(isHealthy ? "Healthy" : "Unhealthy")}";
    }
}
