using System.Threading.Tasks;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Caching
{
    public interface IDistributedCache
    {
        Task<T> GetAsync<T>(string key);
        Task SetAsync<T>(string key, T value, CacheOptions options);
        Task RemoveAsync(string key);
        Task ClearAsync();
    }
}
