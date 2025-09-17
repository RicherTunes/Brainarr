using System;
using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Tokens
{
    internal sealed class ApproximateTokenCounter : ITokenCounter
    {
        public Task<int> CountAsync(string providerSlug, string modelId, string text, CancellationToken cancellationToken)
        {
            if (text == null)
            {
                return Task.FromResult(0);
            }

            var length = Math.Min(text.Length, 2_000_000);
            var tokens = (length + 3) / 4;
            if (tokens <= 0)
            {
                tokens = length > 0 ? 1 : 0;
            }

            return Task.FromResult(tokens);
        }
    }
}
