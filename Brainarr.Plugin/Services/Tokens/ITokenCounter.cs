using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Tokens
{
    internal interface ITokenCounter
    {
        Task<int> CountAsync(string providerSlug, string modelId, string text, CancellationToken cancellationToken);
    }
}
