using System;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    internal static class BrainarrOpenAiChatPolicy
    {
        public static string RequireApiKey(string apiKey, string ownerName)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException($"{ownerName} API key is required", nameof(apiKey));
            }

            return apiKey;
        }
    }
}
