using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Services.Tokenization;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting;
using NzbDrone.Core.ImportLists.Brainarr.Models;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Music;
using NzbDrone.Common.Http;
using FluentValidation.Results;
using NLog;
using Microsoft.Extensions.DependencyInjection;
using NzbDrone.Core.ImportLists.Brainarr.Hosting;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    /// <summary>
    /// Main Lidarr import list implementation for AI-powered music discovery.
    /// Integrates multiple AI providers to generate intelligent music recommendations
    /// based on the user's existing library.
    /// </summary>
    /// <remarks>
    /// Key features:
    /// - Multi-provider support with automatic failover
    /// - Intelligent caching to reduce API calls
    /// - Library-aware prompts for personalized recommendations
    /// - Health monitoring and rate limiting
    /// - Iterative recommendation strategy for quality results
    ///
    /// The plugin follows Lidarr's import list pattern, fetching recommendations
    /// periodically and converting them to ImportListItemInfo objects that
    /// Lidarr can process for automatic album additions.
    /// </remarks>
    public class Brainarr : ImportListBase<BrainarrSettings>, IDisposable
    {
        private readonly IHttpClient _httpClient;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IBrainarrOrchestrator _orchestrator;
        private readonly IServiceProvider? _serviceProvider;
        private ImportListDefinition? _definition;

        public override string Name => "Brainarr AI Music Discovery";
        public override ImportListType ListType => ImportListType.Program;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(BrainarrConstants.MinRefreshIntervalHours);

        public override ProviderDefinition Definition
        {
            get => _definition ??= CreateDefaultDefinition();
            set => _definition = value as ImportListDefinition ?? CreateDefaultDefinition();
        }

        public Brainarr(
            IHttpClient httpClient,
            IImportListStatusService importListStatusService,
            IConfigService configService,
            IParsingService parsingService,
            IArtistService artistService,
            IAlbumService albumService,
            Logger logger)
            : this(httpClient, importListStatusService, configService, parsingService, artistService, albumService, logger, orchestratorOverride: null)
        {
        }

        internal Brainarr(
            IHttpClient httpClient,
            IImportListStatusService importListStatusService,
            IConfigService configService,
            IParsingService parsingService,
            IArtistService artistService,
            IAlbumService albumService,
            Logger logger,
            IBrainarrOrchestrator? orchestratorOverride)
            : base(importListStatusService, configService, parsingService, logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _artistService = artistService ?? throw new ArgumentNullException(nameof(artistService));
            _albumService = albumService ?? throw new ArgumentNullException(nameof(albumService));

            var module = new BrainarrModule();
            _serviceProvider = module.BuildServiceProvider(services =>
            {
                services.AddSingleton(logger);
                services.AddSingleton(_httpClient);
                services.AddSingleton(_artistService);
                services.AddSingleton(_albumService);
            });

            _orchestrator = orchestratorOverride ?? _serviceProvider.GetRequiredService<IBrainarrOrchestrator>();

            // Ensure we have a default definition available immediately for tests/runtime consumers
            _definition = CreateDefaultDefinition();
        }

        public void Dispose()
        {
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        public override IList<ImportListItemInfo> Fetch()
        {
            EnsureDefinitionInitialized();
            // Delegate to the advanced orchestrator which handles all sophisticated features:
            // - Correlation tracking, health monitoring, rate limiting
            // - Library-aware recommendations, iterative refinement
            // - Automatic model detection, fallback handling
            // - Comprehensive caching and error handling
            var items = _orchestrator.FetchRecommendations(Settings);
            // Ensure ImportListId/ImportList fields are populated for Lidarr processing
            return CleanupListItems(items);
        }

        /// <summary>
        /// Validates the plugin configuration by testing the connection to the AI provider.
        /// Delegates validation to the orchestrator which has comprehensive provider testing.
        /// </summary>
        protected override void Test(List<ValidationFailure> failures)
        {
            EnsureDefinitionInitialized();
            // Delegate to the orchestrator's validation logic
            _orchestrator.ValidateConfiguration(Settings, failures);
        }

        /// <summary>
        /// Public wrapper for testing configuration. This allows unit tests to validate configuration.
        /// </summary>
        /// <param name="failures">List to collect validation failures</param>
        public void TestConfiguration(List<ValidationFailure> failures)
        {
            Test(failures);
        }


        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            EnsureDefinitionInitialized();
            // Delegate all UI actions to the orchestrator's action handler
            return _orchestrator.HandleAction(action, query, Settings);
        }

        private void EnsureDefinitionInitialized()
        {
            if (_definition is ImportListDefinition existing && existing.Settings is BrainarrSettings)
            {
                return;
            }

            _definition = CreateDefaultDefinition();
        }

        private ImportListDefinition CreateDefaultDefinition()
        {
            var definition = DefaultDefinitions
                .OfType<ImportListDefinition>()
                .FirstOrDefault()
                ?? new ImportListDefinition
                {
                    EnableAutomaticAdd = false,
                    Implementation = GetType().Name,
                    Settings = new BrainarrSettings()
                };

            if (definition.Settings is not BrainarrSettings)
            {
                definition.Settings = new BrainarrSettings();
            }

            return definition;
        }

    }

}
