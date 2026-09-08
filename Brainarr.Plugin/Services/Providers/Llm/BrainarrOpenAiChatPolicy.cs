using System;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

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

        public static TimeSpan ResolveCompletionTimeout()
        {
            return TimeSpan.FromSeconds(
                TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout));
        }
    }
}
