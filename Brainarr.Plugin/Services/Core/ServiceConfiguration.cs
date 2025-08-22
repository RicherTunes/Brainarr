using System;
using NzbDrone.Common.Http;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class ServiceConfiguration : IServiceConfiguration
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        
        private ModelDetectionService _modelDetection;
        private IRecommendationCache _cache;
        private IProviderHealthMonitor _healthMonitor;
        private IRetryPolicy _retryPolicy;
        private IRateLimiter _rateLimiter;
        private IProviderFactory _providerFactory;
        private LibraryAwarePromptBuilder _promptBuilder;
        private IterativeRecommendationStrategy _iterativeStrategy;

        public ServiceConfiguration(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public ModelDetectionService ModelDetection => 
            _modelDetection ??= new ModelDetectionService(_httpClient, _logger);

        public IRecommendationCache Cache => 
            _cache ??= new RecommendationCache(_logger);

        public IProviderHealthMonitor HealthMonitor => 
            _healthMonitor ??= new ProviderHealthMonitor(_logger);

        public IRetryPolicy RetryPolicy => 
            _retryPolicy ??= new ExponentialBackoffRetryPolicy(_logger);

        public IRateLimiter RateLimiter
        {
            get
            {
                if (_rateLimiter == null)
                {
                    _rateLimiter = new RateLimiter(_logger);
                    RateLimiterConfiguration.ConfigureDefaults(_rateLimiter);
                }
                return _rateLimiter;
            }
        }

        public IProviderFactory ProviderFactory => 
            _providerFactory ??= new AIProviderFactory();

        public LibraryAwarePromptBuilder PromptBuilder => 
            _promptBuilder ??= new LibraryAwarePromptBuilder(_logger);

        public IterativeRecommendationStrategy IterativeStrategy => 
            _iterativeStrategy ??= new IterativeRecommendationStrategy(_logger, PromptBuilder);

        public void ConfigureRateLimiter(IRateLimiter rateLimiter)
        {
            if (rateLimiter == null) return;
            
            RateLimiterConfiguration.ConfigureDefaults(rateLimiter);
        }

        public IAIProvider CreateProvider(BrainarrSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            try
            {
                return ProviderFactory.CreateProvider(settings, _httpClient, _logger);
            }
            catch (NotSupportedException ex)
            {
                _logger.Error(ex, $"Provider type {settings.Provider} is not supported");
                return null;
            }
            catch (ArgumentException ex)
            {
                _logger.Error(ex, "Invalid provider configuration");
                return null;
            }
        }
    }

    public interface IServiceConfiguration
    {
        ModelDetectionService ModelDetection { get; }
        IRecommendationCache Cache { get; }
        IProviderHealthMonitor HealthMonitor { get; }
        IRetryPolicy RetryPolicy { get; }
        IRateLimiter RateLimiter { get; }
        IProviderFactory ProviderFactory { get; }
        LibraryAwarePromptBuilder PromptBuilder { get; }
        IterativeRecommendationStrategy IterativeStrategy { get; }
        
        IAIProvider CreateProvider(BrainarrSettings settings);
        void ConfigureRateLimiter(IRateLimiter rateLimiter);
    }
}