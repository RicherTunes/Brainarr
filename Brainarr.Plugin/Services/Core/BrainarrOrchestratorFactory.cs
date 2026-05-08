using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Performance;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Enrichment;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Time;
using NzbDrone.Core.ImportLists.Brainarr.Services.Tokenization;
using NzbDrone.Core.Music;
using Lidarr.Plugin.Common.Services.Performance;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core;

internal static class BrainarrOrchestratorFactory
{
    private const int DefaultPlanCacheCapacity = 256;

    public static void ConfigureServices(IServiceCollection services)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddSingleton<PersistSettingsCallback>(new PersistSettingsCallback(null));
        services.TryAddSingleton<RegistryOptions>(ResolveRegistryOptions);

        services.TryAddSingleton<IClock>(_ => SystemClock.Instance);
        services.TryAddSingleton<IMetrics, NoOpMetrics>();

        services.TryAddSingleton<IProviderRegistry>(sp =>
        {
            return new ProviderRegistry(sp.GetRequiredService<IHttpResilience>());
        });
        services.TryAddSingleton<ModelRegistryLoader>();

        services.TryAddSingleton<IStyleCatalogService>(sp =>
        {
            var logger = sp.GetRequiredService<Logger>();
            var httpClient = sp.GetRequiredService<IHttpClient>();
            return new StyleCatalogService(logger, httpClient);
        });

        services.TryAddSingleton<IProviderFactory>(sp =>
        {
            var options = sp.GetRequiredService<RegistryOptions>();
            if (options.UseExternalRegistry)
            {
                var logger = sp.GetRequiredService<Logger>();
                var displayUrl = string.IsNullOrWhiteSpace(options.RegistryUrl) ? "<embedded/cache>" : options.RegistryUrl;
                logger.Info("Brainarr: External model registry enabled (url: {0})", displayUrl);
            }

            return new AIProviderFactory(
                sp.GetRequiredService<IProviderRegistry>(),
                sp.GetRequiredService<ModelRegistryLoader>(),
                options.RegistryUrl);
        });

        services.TryAddSingleton<ILibraryAnalyzer>(sp =>
            new LibraryAnalyzer(
                sp.GetRequiredService<IArtistService>(),
                sp.GetRequiredService<IAlbumService>(),
                sp.GetRequiredService<IStyleCatalogService>(),
                sp.GetRequiredService<Logger>()));

        services.TryAddSingleton<IRecommendationCache>(sp => new RecommendationCache(sp.GetRequiredService<Logger>()));
        services.TryAddSingleton<IProviderHealthMonitor>(sp => new ProviderHealthMonitor(sp.GetRequiredService<Logger>()));
        services.TryAddSingleton<IRecommendationValidator>(sp => new RecommendationValidator(sp.GetRequiredService<Logger>()));
        services.TryAddSingleton<IModelDetectionService>(sp => new ModelDetectionService(sp.GetRequiredService<IHttpClient>(), sp.GetRequiredService<Logger>()));
        services.TryAddSingleton<IDuplicationPrevention>(sp => new DuplicationPreventionService(sp.GetRequiredService<Logger>()));
        services.TryAddSingleton<IPlannerVersionProvider, DefaultPlannerVersionProvider>();
        services.TryAddSingleton<IRecommendationCacheKeyBuilder, RecommendationCacheKeyBuilder>();
        services.TryAddSingleton<ILibraryContextBuilder>(sp => new LibraryContextBuilder(sp.GetRequiredService<Logger>()));
        services.TryAddSingleton<ILibraryProfileService>(sp => new LibraryProfileService(
            sp.GetRequiredService<ILibraryContextBuilder>(),
            sp.GetRequiredService<Logger>(),
            sp.GetRequiredService<IArtistService>(),
            sp.GetRequiredService<IAlbumService>(),
            sp.GetRequiredService<LibraryProfileOptions>()));

        services.TryAddSingleton<IMusicBrainzResolver>(sp => new MusicBrainzResolver(sp.GetRequiredService<Logger>()));
        services.TryAddSingleton<IArtistMbidResolver>(sp => new ArtistMbidResolver(sp.GetRequiredService<Logger>()));

        services.TryAddSingleton<ReviewQueueService>(sp => new ReviewQueueService(sp.GetRequiredService<Logger>()));
        services.TryAddSingleton<RecommendationHistory>(sp => new RecommendationHistory(sp.GetRequiredService<Logger>()));
        services.TryAddSingleton<IPerformanceMetrics>(sp => new PerformanceMetrics(sp.GetRequiredService<Logger>()));

        services.TryAddSingleton<IPlanCache>(sp =>
        {
            var cache = new PlanCache(DefaultPlanCacheCapacity, sp.GetRequiredService<IMetrics>(), sp.GetRequiredService<IClock>());
            cache.Configure(DefaultPlanCacheCapacity);
            return cache;
        });

        // Adaptive per-host rate limiter used by HTTP resilience pipeline
        services.TryAddSingleton<IUniversalAdaptiveRateLimiter, UniversalAdaptiveRateLimiter>();
        services.TryAddSingleton<IHttpResilience, HttpResilienceExecutor>();
        services.TryAddSingleton<LibraryProfileOptions>(sp => new LibraryProfileOptions());

        services.TryAddSingleton(sp =>
        {
            // Expose a simple service locator for helpers that can't be DI-constructed
            // (e.g., static helper paths). Keep internal-only.
            NzbDrone.Core.ImportLists.Brainarr.Services.Core.ServiceLocator.Initialize(sp);
            return sp;
        });

        services.TryAddSingleton<ITokenizerRegistry>(sp =>
            new ModelTokenizerRegistry(logger: sp.GetRequiredService<Logger>(), metrics: sp.GetRequiredService<IMetrics>()));

        services.TryAddSingleton<IStyleSelectionService>(sp =>
            new DefaultStyleSelectionService(sp.GetRequiredService<Logger>(), sp.GetRequiredService<IStyleCatalogService>()));

        services.TryAddSingleton<ITokenBudgetPolicy, DefaultTokenBudgetPolicy>();

        services.TryAddSingleton<IPromptPlanner>(sp =>
            new LibraryPromptPlanner(
                sp.GetRequiredService<Logger>(),
                sp.GetRequiredService<IStyleCatalogService>(),
                sp.GetRequiredService<IPlanCache>(),
                styleSelectionService: sp.GetRequiredService<IStyleSelectionService>()));

        services.TryAddSingleton<IPromptRenderer, LibraryPromptRenderer>();

        services.TryAddSingleton<ILibraryAwarePromptBuilder>(sp =>
        {
            var options = sp.GetRequiredService<RegistryOptions>();
            return new LibraryAwarePromptBuilder(
                sp.GetRequiredService<Logger>(),
                sp.GetRequiredService<IStyleCatalogService>(),
                sp.GetRequiredService<ModelRegistryLoader>(),
                sp.GetRequiredService<ITokenizerRegistry>(),
                options.RegistryUrl,
                sp.GetRequiredService<IPromptPlanner>(),
                sp.GetRequiredService<IPromptRenderer>(),
                sp.GetRequiredService<IPlanCache>(),
                sp.GetRequiredService<IMetrics>(),
                sp.GetRequiredService<ITokenBudgetPolicy>());
        });

        services.TryAddSingleton<IRecommendationSanitizer>(sp => new RecommendationSanitizer(sp.GetRequiredService<Logger>()));
        services.TryAddSingleton<IRecommendationSchemaValidator>(sp => new RecommendationSchemaValidator(sp.GetRequiredService<Logger>()));
        services.TryAddSingleton<IProviderInvoker, ProviderInvoker>();
        services.TryAddSingleton<ISafetyGateService, SafetyGateService>();
        services.TryAddSingleton<IDuplicateFilterService>(sp => new DuplicateFilterService(
                sp.GetRequiredService<NzbDrone.Core.Music.IArtistService>(),
                sp.GetRequiredService<NzbDrone.Core.Music.IAlbumService>(),
                sp.GetRequiredService<Logger>()));
        services.TryAddSingleton<ITopUpPlanner>(sp => new TopUpPlanner(
                sp.GetRequiredService<Logger>(),
                sp.GetRequiredService<IDuplicateFilterService>()));
        // WS4.2: Use CommonBreakerRegistry which delegates to Common's AdvancedCircuitBreaker
        services.TryAddSingleton<IBreakerRegistry, CommonBreakerRegistry>();

        services.TryAddSingleton<IRecommendationPipeline>(sp =>
            new RecommendationPipeline(
                sp.GetRequiredService<Logger>(),
                sp.GetRequiredService<ILibraryAnalyzer>(),
                sp.GetRequiredService<IDuplicateFilterService>(),
                sp.GetRequiredService<IRecommendationValidator>(),
                sp.GetRequiredService<ISafetyGateService>(),
                sp.GetRequiredService<ITopUpPlanner>(),
                sp.GetRequiredService<IMusicBrainzResolver>(),
                sp.GetRequiredService<IArtistMbidResolver>(),
                sp.GetRequiredService<IDuplicationPrevention>(),
                sp.GetRequiredService<IPerformanceMetrics>(),
                sp.GetRequiredService<RecommendationHistory>()));

        services.TryAddSingleton<IRecommendationCoordinator>(sp =>
            new RecommendationCoordinator(
                sp.GetRequiredService<Logger>(),
                sp.GetRequiredService<IRecommendationCache>(),
                sp.GetRequiredService<IRecommendationPipeline>(),
                sp.GetRequiredService<IRecommendationSanitizer>(),
                sp.GetRequiredService<IRecommendationSchemaValidator>(),
                sp.GetRequiredService<RecommendationHistory>(),
                sp.GetRequiredService<ILibraryProfileService>(),
                sp.GetRequiredService<IRecommendationCacheKeyBuilder>()));

        services.TryAddSingleton<IBrainarrOrchestrator>(sp =>
        {
            var persistCallback = sp.GetRequiredService<PersistSettingsCallback>().Callback;
            return new BrainarrOrchestrator(
                sp.GetRequiredService<Logger>(),
                sp.GetRequiredService<IProviderFactory>(),
                sp.GetRequiredService<ILibraryAnalyzer>(),
                sp.GetRequiredService<IRecommendationCache>(),
                sp.GetRequiredService<IProviderHealthMonitor>(),
                sp.GetRequiredService<IRecommendationValidator>(),
                sp.GetRequiredService<IModelDetectionService>(),
                sp.GetRequiredService<IHttpClient>(),
                sp.GetRequiredService<IDuplicationPrevention>(),
                sp.GetRequiredService<IMusicBrainzResolver>(),
                sp.GetRequiredService<IArtistMbidResolver>(),
                persistCallback,
                sp.GetRequiredService<IRecommendationSanitizer>(),
                sp.GetRequiredService<IRecommendationSchemaValidator>(),
                sp.GetRequiredService<IProviderInvoker>(),
                sp.GetRequiredService<ISafetyGateService>(),
                sp.GetRequiredService<ITopUpPlanner>(),
                sp.GetRequiredService<IRecommendationPipeline>(),
                sp.GetRequiredService<IRecommendationCoordinator>(),
                sp.GetRequiredService<ILibraryAwarePromptBuilder>(),
                sp.GetRequiredService<IStyleCatalogService>(),
                sp.GetRequiredService<IBreakerRegistry>());
        });
    }

    public static void ConfigurePersistSettings(IServiceCollection services, Action? callback)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.Replace(ServiceDescriptor.Singleton<PersistSettingsCallback>(new PersistSettingsCallback(callback)));
    }

    public static IBrainarrOrchestrator Create(
        Logger logger,
        IHttpClient httpClient,
        IArtistService artistService,
        IAlbumService albumService,
        Action? persistSettingsCallback = null)
    {
        if (logger is null) throw new ArgumentNullException(nameof(logger));
        if (httpClient is null) throw new ArgumentNullException(nameof(httpClient));
        if (artistService is null) throw new ArgumentNullException(nameof(artistService));
        if (albumService is null) throw new ArgumentNullException(nameof(albumService));

        var services = new ServiceCollection();
        services.AddSingleton(logger);
        services.AddSingleton(httpClient);
        services.AddSingleton(artistService);
        services.AddSingleton(albumService);

        ConfigureServices(services);

        if (persistSettingsCallback != null)
        {
            ConfigurePersistSettings(services, persistSettingsCallback);
        }

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IBrainarrOrchestrator>();
    }

    private static RegistryOptions ResolveRegistryOptions(IServiceProvider _)
    {
        if (!AIProviderFactory.UseExternalModelRegistry)
        {
            return new RegistryOptions(null, false);
        }

        var registryUrl = Environment.GetEnvironmentVariable("BRAINARR_MODEL_REGISTRY_URL");
        registryUrl = string.IsNullOrWhiteSpace(registryUrl) ? null : registryUrl.Trim();
        return new RegistryOptions(registryUrl, true);
    }

    private sealed record PersistSettingsCallback(Action? Callback);

    private sealed record RegistryOptions(string? RegistryUrl, bool UseExternalRegistry);
}
