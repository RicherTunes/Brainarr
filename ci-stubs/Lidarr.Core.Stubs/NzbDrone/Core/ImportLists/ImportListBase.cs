using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists
{
    public enum ImportListType
    {
        Program = 0,
        Spotify = 1,
        LastFm = 2,
        Lidarr = 3,
        Music = 4,
        Other = 5
    }

    public abstract class ImportListBase<TSettings> : IImportList
        where TSettings : IImportListSettings, new()
    {
        protected readonly IImportListStatusService _importListStatusService;
        protected readonly IConfigService _configService;
        protected readonly IParsingService _parsingService;
        protected readonly Logger _logger;

        public abstract string Name { get; }
        public abstract ImportListType ListType { get; }
        public virtual TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);

        public Type ConfigContract => typeof(TSettings);
        public virtual ProviderMessage Message => null;

        public IImportListSettings Settings { get; set; }

        protected TSettings TypedSettings => (TSettings)Settings;

        protected ImportListBase(IImportListStatusService importListStatusService, IConfigService configService, IParsingService parsingService, Logger logger)
        {
            _importListStatusService = importListStatusService;
            _configService = configService;
            _parsingService = parsingService;
            _logger = logger;
        }

        public abstract Task<IList<ImportListItemInfo>> Fetch();
        public virtual object RequestAction(string action, IDictionary<string, string> query) { return new { }; }

        public virtual NzbDroneValidationResult Test()
        {
            var failures = new List<ValidationFailure>();
            
            try
            {
                var result = TestConnection();
                failures.AddRange(result.Errors);
                return new NzbDroneValidationResult(failures);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Test aborted due to exception");
                failures.Add(new ValidationFailure(string.Empty, "Test was aborted due to an error: " + ex.Message));
            }

            return new NzbDroneValidationResult(failures);
        }

        protected virtual ValidationResult TestConnection()
        {
            return new ValidationResult();
        }

        public override string ToString()
        {
            return GetType().Name;
        }

        public virtual ImportListDefinition Definition { get; set; }
    }

    public interface IImportList
    {
        string Name { get; }
        ImportListType ListType { get; }
        TimeSpan MinRefreshInterval { get; }
        Type ConfigContract { get; }
        ProviderMessage Message { get; }
        IImportListSettings Settings { get; set; }
        ImportListDefinition Definition { get; set; }

        Task<IList<ImportListItemInfo>> Fetch();
        object RequestAction(string action, IDictionary<string, string> query);
        NzbDroneValidationResult Test();
    }

    public interface IImportListSettings
    {
        NzbDroneValidationResult Validate();
    }

    public interface IImportListStatusService
    {
        // Stub interface
    }

    public class ImportListDefinition
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }
        public ImportListType ListType { get; set; }
        public TimeSpan MinRefreshInterval { get; set; }
        public string Implementation { get; set; }
        public string ConfigContract { get; set; }
        public IImportListSettings Settings { get; set; }
    }

    public class ProviderMessage
    {
        public string Message { get; set; }
        public ProviderMessageType Type { get; set; }
    }

    public enum ProviderMessageType
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }
}