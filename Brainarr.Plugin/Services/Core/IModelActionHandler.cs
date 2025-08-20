using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface IModelActionHandler
    {
        Task<string> HandleTestConnectionAsync(BrainarrSettings settings);
        Task<List<SelectOption>> HandleGetModelsAsync(BrainarrSettings settings);
        Task<string> HandleAnalyzeLibraryAsync(BrainarrSettings settings);
        object HandleProviderAction(string action, BrainarrSettings settings);
    }
    
    public class SelectOption
    {
        public string Value { get; set; }
        public string Name { get; set; }
    }
}