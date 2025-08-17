using System;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public class ServiceContainer
    {
        private readonly IServiceProvider _serviceProvider;

        public ServiceContainer(
            IHttpClient httpClient,
            IArtistService artistService,
            IAlbumService albumService,
            IConfigService configService,
            IParsingService parsingService,
            Logger logger)
        {
            var services = new ServiceCollection();

            // Register Lidarr services
            services.AddSingleton(httpClient);
            services.AddSingleton(artistService);
            services.AddSingleton(albumService);
            services.AddSingleton(configService);
            services.AddSingleton(parsingService);
            services.AddSingleton(logger);

            // Register core services
            services.AddSingleton<IModelDetectionService, ModelDetectionService>();
            services.AddSingleton<IRecommendationCache, RecommendationCache>();
            services.AddSingleton<IProviderHealthMonitor, ProviderHealthMonitor>();
            services.AddSingleton<IRetryPolicy, ExponentialBackoffRetryPolicy>();
            services.AddSingleton<IRateLimiter, RateLimiter>();
            services.AddSingleton<IProviderFactory, AIProviderFactory>();
            services.AddSingleton<ILibraryAnalyzer, LibraryAnalyzer>();
            services.AddSingleton<IRecommendationSanitizer, RecommendationSanitizer>();

            // Register new decomposed services
            services.AddSingleton<IProviderInitializationService, ProviderInitializationService>();
            services.AddSingleton<ILibraryProfileService, LibraryProfileService>();
            services.AddSingleton<IRecommendationService, RecommendationService>();

            // Register support services
            services.AddSingleton<LibraryAwarePromptBuilder>();
            services.AddSingleton<IterativeRecommendationStrategy>();
            services.AddSingleton<StructuredLogger>();

            // Configure rate limiter
            services.AddSingleton<IRateLimiter>(provider =>
            {
                var rateLimiter = new RateLimiter(provider.GetRequiredService<Logger>());
                RateLimiterConfiguration.ConfigureDefaults(rateLimiter);
                return rateLimiter;
            });

            _serviceProvider = services.BuildServiceProvider();
        }

        public T GetService<T>() where T : class
        {
            return _serviceProvider.GetService<T>();
        }

        public T GetRequiredService<T>() where T : class
        {
            return _serviceProvider.GetRequiredService<T>();
        }

        public void Dispose()
        {
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}