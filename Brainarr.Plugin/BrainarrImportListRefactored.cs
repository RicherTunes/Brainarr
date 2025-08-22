using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Models;
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
        private readonly IHttpClient _httpClient;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly IServiceConfiguration _services;
        private readonly ISettingsActionHandler _actionHandler;
        private readonly IRecommendationFetcher _fetcher;
        private readonly IProviderValidator _validator;
        private IAIProvider _provider;

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
            _artistService = artistService;
            _albumService = albumService;
            
            _services = new ServiceConfiguration(httpClient, logger);
            _actionHandler = new SettingsActionHandler(_services.ModelDetection, logger);
            _fetcher = new RecommendationFetcher(_services, _artistService, _albumService, logger);
            _validator = new ProviderValidator(_services, logger);
        }

        public override IList<ImportListItemInfo> Fetch()
        {
            return _fetcher.FetchRecommendations(Settings, Definition.Id);
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            return _actionHandler.HandleAction(action, Settings, query);
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            _validator.ValidateProvider(Settings, failures);
        }
    }
}