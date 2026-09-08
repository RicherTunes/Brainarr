using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Lidarr.Plugin.Common.Providers.OpenAi;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// Adapts Common's OpenAI-chat transport contract to Lidarr's buffered HTTP client
    /// and Brainarr's response-streaming executor.
    /// </summary>
    internal sealed class BrainarrOpenAiChatTransport : IOpenAiChatTransport
    {
        private readonly IHttpClient _httpClient;
        private readonly StreamingHttpExecutor _streamingExecutor;

        public BrainarrOpenAiChatTransport(
            IHttpClient httpClient,
            Logger logger,
            StreamingHttpExecutor? streamingExecutor = null)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _ = logger ?? throw new ArgumentNullException(nameof(logger));
            _streamingExecutor = streamingExecutor ?? StreamingHttpExecutor.Shared;
        }

        public async ValueTask<OpenAiChatResponse> SendAsync(
            OpenAiChatRequest request,
            CancellationToken cancellationToken)
        {
            var builder = new HttpRequestBuilder(request.Endpoint.ToString());
            foreach (var header in request.Headers)
            {
                builder.SetHeader(header.Key, header.Value);
            }

            var httpRequest = builder.Build();
            httpRequest.Method = HttpMethod.Post;
            var owningTimeout = TimeSpan.FromSeconds(
                TimeoutContext.GetSecondsOrDefault(BrainarrConstants.DefaultAITimeout));
            httpRequest.RequestTimeout = request.Timeout <= owningTimeout
                ? request.Timeout
                : owningTimeout;
            httpRequest.SetContent(request.JsonBody);

            try
            {
                var response = await HttpProviderClient.ExecuteWithCt(
                    _httpClient,
                    httpRequest,
                    cancellationToken).ConfigureAwait(false);
                return ToCommonResponse(response);
            }
            catch (HttpException exception) when (exception.Response != null)
            {
                return ToCommonResponse(exception.Response, exception);
            }
        }

        public async ValueTask<OpenAiChatStreamResponse> OpenStreamAsync(
            OpenAiChatRequest request,
            CancellationToken cancellationToken)
        {
            var stream = await _streamingExecutor.SendForStreamingAsync(
                providerId: request.ProviderId,
                method: HttpMethod.Post,
                url: request.Endpoint.ToString(),
                headers: request.Headers,
                jsonBody: request.JsonBody,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return new OpenAiChatStreamResponse(200, stream);
        }

        private static OpenAiChatResponse ToCommonResponse(HttpResponse response, Exception? transportException = null)
        {
            return new OpenAiChatResponse(
                (int)response.StatusCode,
                response.Content,
                BrainarrHttpResponseHelpers.ParseRetryAfter(response),
                transportException);
        }
    }
}
