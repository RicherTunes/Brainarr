using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    /// <summary>
    /// OpenRouter provider implementation providing access to multiple AI models.
    /// Offers unified access to various models from different providers.
    /// </summary>
    public class OpenRouterProviderRefactored : OpenAICompatibleProvider
    {
        private const string API_URL = "https://openrouter.ai/api/v1/chat/completions";

        public override string ProviderName => "OpenRouter";
        protected override string ApiUrl => API_URL;

        public OpenRouterProviderRefactored(IHttpClient httpClient, Logger logger, string apiKey, string model = "anthropic/claude-3.5-haiku")
            : base(httpClient, logger, apiKey, model)
        {
        }

        protected override string GetDefaultModel() => "anthropic/claude-3.5-haiku";
    }
}