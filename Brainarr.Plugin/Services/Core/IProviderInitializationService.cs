using System.Threading.Tasks;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    public interface IProviderInitializationService
    {
        Task<IAIProvider> InitializeProviderAsync(BrainarrSettings settings);
        Task<bool> ValidateProviderAsync(IAIProvider provider, BrainarrSettings settings);
        Task DetectAndConfigureModelsAsync(BrainarrSettings settings);
    }
}