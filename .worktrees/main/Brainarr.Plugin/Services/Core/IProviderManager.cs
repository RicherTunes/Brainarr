using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface IProviderManager
    {
        IAIProvider GetCurrentProvider();
        void InitializeProvider(BrainarrSettings settings);
        void UpdateProvider(BrainarrSettings settings);
        Task<List<string>> DetectAvailableModels(BrainarrSettings settings);
        string? SelectBestModel(List<string> availableModels);
        bool IsProviderReady();
        void Dispose();
    }
}
