using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    /// <summary>
    /// Groq provider implementation for music recommendations using fast inference models.
    /// Supports Llama, Mixtral, and other high-performance models.
    /// </summary>
    public class GroqProviderRefactored : OpenAICompatibleProvider
    {
        private const string API_URL = "https://api.groq.com/openai/v1/chat/completions";

        public override string ProviderName => "Groq";
        protected override string ApiUrl => API_URL;

        public GroqProviderRefactored(IHttpClient httpClient, Logger logger, string apiKey, string model = "llama-3.3-70b-versatile")
            : base(httpClient, logger, apiKey, model)
        {
        }

        protected override string GetDefaultModel() => "llama-3.3-70b-versatile";
    }
}