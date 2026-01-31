using System;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Manages the lifecycle of AI providers including initialization, health monitoring,
    /// and connection testing. Provides a single point of control for provider state.
    /// </summary>
    public class ProviderLifecycleManager : IProviderLifecycleManager
    {
        private readonly Logger _logger;
        private readonly IProviderFactory _providerFactory;
        private readonly IProviderHealthMonitor _providerHealth;
        private readonly IHttpClient _httpClient;

        private IAIProvider _currentProvider;
        private AIProvider? _currentProviderType;

        public ProviderLifecycleManager(
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

        /// <summary>
        /// Gets the currently initialized provider instance.
        /// </summary>
        public IAIProvider CurrentProvider => _currentProvider;

        /// <summary>
        /// Gets the type of the currently initialized provider.
        /// </summary>
        public AIProvider? CurrentProviderType => _currentProviderType;

        /// <summary>
        /// Initializes the provider specified in the settings if not already initialized.
        /// Re-initialization only occurs if the provider type has changed.
        /// If the provider is not available (e.g., empty API key), gracefully disables instead of throwing.
        /// </summary>
        public void InitializeProvider(BrainarrSettings settings)
        {
            // Check if we already have the correct provider initialized
            if (_currentProvider != null && _currentProviderType.HasValue && _currentProviderType.Value == settings.Provider)
            {
                _logger.Debug($"Provider {settings.Provider} already initialized for current settings");
                return;
            }

            _logger.Info($"Initializing provider: {settings.Provider}");

            // Check availability first - gracefully disable if not configured
            if (!_providerFactory.IsProviderAvailable(settings.Provider, settings))
            {
                _logger.Warn($"Provider {settings.Provider} is not available (missing credentials or configuration). Provider disabled.");
                _currentProvider = null;
                _currentProviderType = settings.Provider;
                return;
            }

            try
            {
                _currentProvider = _providerFactory.CreateProvider(settings, _httpClient, _logger);
                if (_currentProvider == null)
                {
                    _logger.Warn($"Provider {settings.Provider} creation returned null. Provider disabled.");
                    _currentProviderType = settings.Provider;
                    return;
                }
                _currentProviderType = settings.Provider;

                try
                {
                    EventLogger.Log(_logger, BrainarrEvent.ProviderSelected, $"provider={_currentProviderType} model={settings.EffectiveModel}");
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Non-critical: Failed to log provider selected event");
                }

                _logger.Info($"Successfully initialized {settings.Provider} provider");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to initialize {settings.Provider} provider");
                _currentProvider = null;
                throw;
            }
        }

        /// <summary>
        /// Updates the provider configuration based on the current settings.
        /// Equivalent to InitializeProvider but makes intent clearer.
        /// </summary>
        public void UpdateProviderConfiguration(BrainarrSettings settings)
        {
            InitializeProvider(settings);
        }

        /// <summary>
        /// Tests the connection to the configured provider and records the result in health metrics.
        /// </summary>
        /// <returns>True if the connection test succeeds, false otherwise.</returns>
        public async Task<bool> TestProviderConnectionAsync(BrainarrSettings settings)
        {
            try
            {
                InitializeProvider(settings);

                if (_currentProvider == null)
                    return false;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var testResult = await _currentProvider.TestConnectionAsync();
                sw.Stop();

                if (testResult)
                {
                    _providerHealth.RecordSuccess(_currentProvider.ProviderName, sw.Elapsed.TotalMilliseconds);
                }
                else
                {
                    _providerHealth.RecordFailure(_currentProvider.ProviderName, "Connection test failed");
                }

                _logger.Debug($"Provider connection test result: {testResult}");
                return testResult;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Provider connection test failed");
                if (_currentProvider != null)
                {
                    _providerHealth.RecordFailure(_currentProvider.ProviderName, ex.Message);
                }
                return false;
            }
        }

        /// <summary>
        /// Checks if the currently initialized provider is healthy based on health monitoring metrics.
        /// </summary>
        /// <returns>True if the provider is healthy, false otherwise.</returns>
        public bool IsProviderHealthy()
        {
            if (_currentProvider == null)
                return false;

            return _providerHealth.IsHealthy(_currentProvider.ProviderName);
        }

        /// <summary>
        /// Gets a human-readable status string for the current provider.
        /// </summary>
        /// <returns>Status string in the format "ProviderName: Healthy/Unhealthy" or "Not Initialized".</returns>
        public string GetProviderStatus()
        {
            if (_currentProvider == null)
                return "Not Initialized";

            var providerName = _currentProvider.ProviderName;
            var isHealthy = _providerHealth.IsHealthy(providerName);

            return $"{providerName}: {(isHealthy ? "Healthy" : "Unhealthy")}";
        }

        /// <summary>
        /// Gets the provider name for the currently initialized provider.
        /// </summary>
        /// <returns>Provider name or null if no provider is initialized.</returns>
        public string GetProviderName()
        {
            return _currentProvider?.ProviderName;
        }
    }
}
