using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    /// <summary>
    /// DeepSeek provider implementation for music recommendations using reasoning models.
    /// Offers cost-effective alternatives to OpenAI models.
    /// </summary>
    public class DeepSeekProviderRefactored : OpenAICompatibleProvider
    {
        private const string API_URL = "https://api.deepseek.com/v1/chat/completions";

        public override string ProviderName => "DeepSeek";
        protected override string ApiUrl => API_URL;

        public DeepSeekProviderRefactored(IHttpClient httpClient, Logger logger, string apiKey, string model = "deepseek-chat")
            : base(httpClient, logger, apiKey, model)
        {
        }

        protected override string GetDefaultModel() => "deepseek-chat";
    }
}