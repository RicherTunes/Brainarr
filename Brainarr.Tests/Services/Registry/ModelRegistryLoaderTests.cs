using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using RegistryModelRegistryLoader = NzbDrone.Core.ImportLists.Brainarr.Services.Registry.ModelRegistryLoader;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;

namespace Brainarr.Tests.Services.Registry
{
    public class ModelRegistryLoaderTests : IDisposable
    {
        private readonly string _tempCachePath;

        public ModelRegistryLoaderTests()
        {
            _tempCachePath = Path.Combine(Path.GetTempPath(), "brainarr-tests", Guid.NewGuid().ToString("N"), "registry.json");
        }

        [Fact]
        public async Task Should_load_embedded_example_when_no_url_is_provided()
        {
            var loader = new RegistryModelRegistryLoader(
                httpClient: new HttpClient(new SequenceMessageHandler()),
                cacheFilePath: _tempCachePath,
                embeddedRegistryPath: ResolveExamplePath());

            var result = await loader.LoadAsync(null, default);

            result.Registry.Should().NotBeNull();
            result.Source.Should().Be(ModelRegistryLoadSource.Embedded);
            result.Registry!.Providers.Should().NotBeEmpty();
        }

        [Fact]
        public async Task Should_use_cache_when_etag_indicates_not_modified()
        {
            var handler = new SequenceMessageHandler();
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(File.ReadAllText(ResolveExamplePath())),
                Headers = { ETag = new EntityTagHeaderValue("\"v1\"") }
            });
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.NotModified));

            var loader = new RegistryModelRegistryLoader(
                httpClient: new HttpClient(handler),
                cacheFilePath: _tempCachePath,
                embeddedRegistryPath: ResolveExamplePath());

            var registryUrl = "https://example.com/models.json";

            var first = await loader.LoadAsync(registryUrl, default);
            first.Source.Should().Be(ModelRegistryLoadSource.Network);
            first.Registry.Should().NotBeNull();

            var second = await loader.LoadAsync(registryUrl, default);
            second.Source.Should().Be(ModelRegistryLoadSource.CacheNotModified);
            second.Registry.Should().NotBeNull();
        }

        [Fact]
        public async Task Should_fallback_to_cache_when_network_fails()
        {
            var handler = new SequenceMessageHandler();
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(File.ReadAllText(ResolveExamplePath())),
                Headers = { ETag = new EntityTagHeaderValue("\"v2\"") }
            });
            handler.Enqueue(new HttpRequestException("network down"));

            var loader = new RegistryModelRegistryLoader(
                httpClient: new HttpClient(handler),
                cacheFilePath: _tempCachePath,
                embeddedRegistryPath: ResolveExamplePath());

            var registryUrl = "https://example.com/models.json";

            var first = await loader.LoadAsync(registryUrl, default);
            first.Source.Should().Be(ModelRegistryLoadSource.Network);

            var second = await loader.LoadAsync(registryUrl, default);
            second.Source.Should().Be(ModelRegistryLoadSource.CacheFallback);
            second.Registry.Should().NotBeNull();
        }

        [Fact]
        public async Task Should_use_embedded_when_remote_registry_is_invalid()
        {
            var handler = new SequenceMessageHandler();
            var invalidJson = File.ReadAllText(ResolveExamplePath()).Replace("\"version\": \"1\"", "\"version\": \"2\"");
            handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(invalidJson),
                Headers = { ETag = new EntityTagHeaderValue("\"v3\"") }
            });

            var loader = new RegistryModelRegistryLoader(
                httpClient: new HttpClient(handler),
                cacheFilePath: _tempCachePath,
                embeddedRegistryPath: ResolveExamplePath());

            var result = await loader.LoadAsync("https://example.com/models.json", default);

            result.Registry.Should().NotBeNull();
            result.Source.Should().Be(ModelRegistryLoadSource.Embedded);
        }

        private static string ResolveExamplePath()
        {
            var baseDirectory = AppContext.BaseDirectory;
            for (var i = 0; i < 6; i++)
            {
                var segments = Enumerable.Repeat("..", i).Concat(new[] { "docs", "models.example.json" }).ToArray();
                var candidate = Path.GetFullPath(Path.Combine(new[] { baseDirectory }.Concat(segments).ToArray()));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("Could not locate models.example.json");
        }

        public void Dispose()
        {
            try
            {
                var directory = Path.GetDirectoryName(_tempCachePath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }

        private sealed class SequenceMessageHandler : HttpMessageHandler
        {
            private readonly Queue<object> _responses = new();

            public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

            public void Enqueue(Exception exception) => _responses.Enqueue(exception);

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (_responses.Count == 0)
                {
                    throw new InvalidOperationException("No queued responses available");
                }

                var next = _responses.Dequeue();
                if (next is Exception exception)
                {
                    throw exception;
                }

                var response = (HttpResponseMessage)next;
                response.RequestMessage = request;
                return Task.FromResult(response);
            }
        }

        [Fact]
        public async Task Should_load_dictionary_shape_registry()
        {
            var directory = Path.Combine(Path.GetTempPath(), "brainarr-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var cachePath = Path.Combine(directory, "registry.json");
            const string json = "{\n  \"version\": \"1\",\n  \"providers\": {\n    \"openai\": {\n      \"name\": \"OpenAI\",\n      \"slug\": \"openai\",\n      \"models\": [ { \"id\": \"gpt-test\", \"context_tokens\": 32000 } ]\n    }\n  }\n}";
            File.WriteAllText(cachePath, json);

            var loader = new RegistryModelRegistryLoader(
                httpClient: new HttpClient(new SequenceMessageHandler()),
                cacheFilePath: cachePath,
                embeddedRegistryPath: cachePath);

            var result = await loader.LoadAsync(null, default);

            result.Registry.Should().NotBeNull();
            result.Registry!.Providers.Should().ContainSingle();
            var provider = result.Registry.Providers[0];
            provider.Slug.Should().Be("openai");
            provider.Models.Should().ContainSingle(m => m.Id == "gpt-test" && m.ContextTokens == 32000);
        }
    }
}
