using System;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Providers
{
    public class ProviderParsingSmokeTests
    {
        private class StubHttpClient : IHttpClient
        {
            private readonly string _content;
            private readonly System.Net.HttpStatusCode _code;
            public StubHttpClient(string content, System.Net.HttpStatusCode code = System.Net.HttpStatusCode.OK)
            { _content = content; _code = code; }
            public Task<HttpResponse> ExecuteAsync(HttpRequest request) => Task.FromResult(new HttpResponse(request, new HttpHeader(), _content, _code));
            public HttpResponse Execute(HttpRequest request) => new HttpResponse(request, new HttpHeader(), _content, _code);
            public void DownloadFile(string url, string fileName) => throw new NotImplementedException();
            public Task DownloadFileAsync(string url, string fileName) => throw new NotImplementedException();
            public HttpResponse Get(HttpRequest request) => Execute(request);
            public Task<HttpResponse> GetAsync(HttpRequest request) => ExecuteAsync(request);
            public HttpResponse<T> Get<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> GetAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public HttpResponse Head(HttpRequest request) => Execute(request);
            public Task<HttpResponse> HeadAsync(HttpRequest request) => ExecuteAsync(request);
            public HttpResponse Post(HttpRequest request) => Execute(request);
            public Task<HttpResponse> PostAsync(HttpRequest request) => ExecuteAsync(request);
            public HttpResponse<T> Post<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
            public Task<HttpResponse<T>> PostAsync<T>(HttpRequest request) where T : new() => throw new NotImplementedException();
        }

        private static Logger L => LogManager.GetCurrentClassLogger();

        [Fact]
        public async Task OpenAI_parses_message_content_with_json()
        {
            var message = "{\"recommendations\":[{\"artist\":\"A\",\"album\":\"B\"}]}";
            var payload = $"{{\"choices\":[{{\"message\":{{\"content\":\"{message.Replace("\"", "\\\"")}\"}}}}]}}";
            var client = new StubHttpClient(payload);
            var p = new OpenAIProvider(client, L, apiKey: "k", model: "gpt-4o-mini");
            var list = await p.GetRecommendationsAsync("prompt");
            list.Should().NotBeNull();
        }

        [Fact]
        public async Task Anthropic_parses_content_text_with_json()
        {
            var message = "{\"recommendations\":[{\"artist\":\"A\"}]}";
            var payload = $"{{\"content\":[{{\"type\":\"text\",\"text\":\"{message.Replace("\"", "\\\"")}\"}}]}}";
            var client = new StubHttpClient(payload);
            var p = new AnthropicProvider(client, L, apiKey: "k", model: "claude-3.5-haiku");
            var list = await p.GetRecommendationsAsync("prompt");
            list.Should().NotBeNull();
        }

        [Fact]
        public async Task OpenRouter_parses_message_content_with_json()
        {
            var message = "{\"recommendations\":[{\"artist\":\"A\"}]}";
            var payload = $"{{\"choices\":[{{\"message\":{{\"content\":\"{message.Replace("\"", "\\\"")}\"}}}}]}}";
            var client = new StubHttpClient(payload);
            var p = new OpenRouterProvider(client, L, apiKey: "k", model: "anthropic/claude-3.5-haiku");
            var list = await p.GetRecommendationsAsync("prompt");
            list.Should().NotBeNull();
        }

        [Fact]
        public async Task Groq_parses_message_content_with_json()
        {
            var message = "{\"recommendations\":[{\"artist\":\"A\"}]}";
            var payload = $"{{\"choices\":[{{\"message\":{{\"content\":\"{message.Replace("\"", "\\\"")}\"}}}}]}}";
            var client = new StubHttpClient(payload);
            var p = new GroqProvider(client, L, apiKey: "k", model: "llama-3.3-70b-versatile");
            var list = await p.GetRecommendationsAsync("prompt");
            list.Should().NotBeNull();
        }

        [Fact]
        public async Task Gemini_parses_content_text_with_json()
        {
            var message = "{\"recommendations\":[{\"artist\":\"A\"}]}";
            var payload = $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":\"{message.Replace("\"", "\\\"")}\"}}]}}}}]}}";
            var client = new StubHttpClient(payload);
            var p = new GeminiProvider(client, L, apiKey: "k", model: "gemini-1.5-flash");
            var list = await p.GetRecommendationsAsync("prompt");
            list.Should().NotBeNull();
        }

        [Fact]
        public async Task DeepSeek_parses_content_with_json()
        {
            var message = "{\"recommendations\":[{\"artist\":\"A\"}]}";
            var payload = $"{{\"choices\":[{{\"message\":{{\"content\":\"{message.Replace("\"", "\\\"")}\"}}}}]}}";
            var client = new StubHttpClient(payload);
            var p = new DeepSeekProvider(client, L, apiKey: "k", model: "deepseek-chat");
            var list = await p.GetRecommendationsAsync("prompt");
            list.Should().NotBeNull();
        }
    }
}
