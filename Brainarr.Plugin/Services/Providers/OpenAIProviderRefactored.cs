using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers
{
    /// <summary>
    /// OpenAI provider implementation for music recommendations using GPT models.
    /// Supports GPT-4, GPT-4 Turbo, GPT-3.5 Turbo, and other OpenAI models.
    /// </summary>
    /// <remarks>
    /// This provider requires an OpenAI API key from https://platform.openai.com/api-keys
    /// Pricing varies by model: GPT-4 is more expensive but higher quality,
    /// while GPT-3.5-turbo is more cost-effective for basic recommendations.
    /// </remarks>
    public class OpenAIProviderRefactored : OpenAICompatibleProvider
    {
        private const string API_URL = "https://api.openai.com/v1/chat/completions";

        /// <summary>
        /// Gets the display name of this provider.
        /// </summary>
        public override string ProviderName => "OpenAI";

        /// <summary>
        /// Gets the API endpoint URL for this provider.
        /// </summary>
        protected override string ApiUrl => API_URL;

        /// <summary>
        /// Initializes a new instance of the OpenAIProvider class.
        /// </summary>
        /// <param name="httpClient">HTTP client for API communication</param>
        /// <param name="logger">Logger for diagnostic information</param>
        /// <param name="apiKey">OpenAI API key (required)</param>
        /// <param name="model">Model to use (defaults to gpt-4o-mini for cost efficiency)</param>
        public OpenAIProviderRefactored(IHttpClient httpClient, Logger logger, string apiKey, string model = "gpt-4o-mini")
            : base(httpClient, logger, apiKey, model)
        {
        }

        /// <summary>
        /// Gets the default model for OpenAI when none is specified.
        /// </summary>
        protected override string GetDefaultModel() => "gpt-4o-mini";
    }
}