using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Music;
using NzbDrone.Common.Http;
using FluentValidation.Results;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr
{
    public class BrainarrRefactored : ImportListBase<BrainarrSettings>
    {
        private readonly IBrainarrOrchestrator _orchestrator;
        private readonly IBrainarrActionHandler _actionHandler;
        private readonly IHttpClient _httpClient;

        public override string Name => "Brainarr AI Music Discovery";
        public override ImportListType ListType => ImportListType.Program;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);

        public BrainarrRefactored(
            IHttpClient httpClient,
            IImportListStatusService importListStatusService,
            IConfigService configService,
            IParsingService parsingService,
            IArtistService artistService,
            IAlbumService albumService,
            Logger logger) : base(importListStatusService, configService, parsingService, logger)
        {
            _httpClient = httpClient;
            
            // Initialize orchestrator with all dependencies
            _orchestrator = new BrainarrOrchestrator(
                httpClient,
                artistService,
                albumService,
                logger);
            
            // Initialize action handler for UI operations
            var modelDetection = new ModelDetectionService(httpClient, logger);
            _actionHandler = new BrainarrActionHandler(
                httpClient,
                modelDetection,
                logger);
        }

        public override IList<ImportListItemInfo> Fetch()
        {
            try
            {
                // Delegate all fetch logic to orchestrator
                return _orchestrator.FetchRecommendations(Settings);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to fetch recommendations");
                return new List<ImportListItemInfo>();
            }
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            try
            {
                // Enhance query with current settings context
                EnrichQueryWithSettings(query);
                
                // Delegate action handling to dedicated handler
                return _actionHandler.HandleAction(action, query);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to handle action: {action}");
                return new { options = new List<object>() };
            }
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                // Initialize and test provider through orchestrator
                _orchestrator.InitializeProvider(Settings);
                
                if (!_orchestrator.IsProviderHealthy())
                {
                    failures.Add(new ValidationFailure(
                        nameof(Settings.Provider), 
                        "AI provider not configured or connection failed"));
                    return;
                }

                var status = _orchestrator.GetProviderStatus();
                _logger.Info($"Connection test successful: {status}");
                
                // Additional validation for local providers
                if (RequiresModelDetection(Settings.Provider))
                {
                    ValidateLocalProviderModels(failures);
                }
            }
            catch (Exception ex)
            {
                failures.Add(new ValidationFailure(
                    string.Empty, 
                    $"Connection test failed: {ex.Message}"));
            }
        }

        private void EnrichQueryWithSettings(IDictionary<string, string> query)
        {
            // Add current settings to query for context-aware action handling
            query["provider"] = Settings.Provider.ToString();
            
            switch (Settings.Provider)
            {
                case AIProvider.Ollama:
                    query["baseUrl"] = Settings.OllamaUrl ?? "http://localhost:11434";
                    query["model"] = Settings.OllamaModel;
                    break;
                    
                case AIProvider.LMStudio:
                    query["baseUrl"] = Settings.LMStudioUrl ?? "http://localhost:1234";
                    query["model"] = Settings.LMStudioModel;
                    break;
                    
                default:
                    query["baseUrl"] = Settings.BaseUrl;
                    break;
            }
        }

        private bool RequiresModelDetection(AIProvider provider)
        {
            return provider == AIProvider.Ollama || provider == AIProvider.LMStudio;
        }

        private void ValidateLocalProviderModels(List<ValidationFailure> failures)
        {
            try
            {
                var query = new Dictionary<string, string>();
                EnrichQueryWithSettings(query);
                
                var result = _actionHandler.HandleAction($"get{Settings.Provider}Models", query);
                
                if (result is IDictionary<string, object> dict && 
                    dict.ContainsKey("options") && 
                    dict["options"] is List<object> options)
                {
                    if (!options.Any())
                    {
                        var installHint = Settings.Provider == AIProvider.Ollama
                            ? "Install models using: ollama pull llama3"
                            : "Load a model in LM Studio first";
                            
                        failures.Add(new ValidationFailure(
                            string.Empty, 
                            $"No models available. {installHint}"));
                    }
                    else
                    {
                        _logger.Info($"Found {options.Count} available models");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to validate local provider models");
            }
        }
    }
}