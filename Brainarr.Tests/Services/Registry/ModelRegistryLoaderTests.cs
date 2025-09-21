using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using RegistryModelRegistryLoader = NzbDrone.Core.ImportLists.Brainarr.Services.Registry.ModelRegistryLoader;
using FluentAssertions;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using NzbDrone.Core.ImportLists.Brainarr.Models;

namespace Brainarr.Tests.Services.Registry
{
    [Collection("RegistryModelTests")]
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
                embeddedRegistryPath: ResolveExamplePath(),
                options: new ModelRegistryLoaderOptions { EnableSharedCache = false });

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
                embeddedRegistryPath: ResolveExamplePath(),
                options: new ModelRegistryLoaderOptions { EnableSharedCache = false });

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
                embeddedRegistryPath: ResolveExamplePath(),
                options: new ModelRegistryLoaderOptions { EnableSharedCache = false });

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
                embeddedRegistryPath: ResolveExamplePath(),
                options: new ModelRegistryLoaderOptions { EnableSharedCache = false });

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
            ModelRegistryLoader.InvalidateSharedCache();
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
        private sealed class CountingBackend
        {
            private int _loadCount;

            public int LoadCount => _loadCount;

            public Task<ModelRegistryLoadResult> LoadAsync(string? registryUrl, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _loadCount);

                var registry = new ModelRegistry
                {
                    Version = "1",
                    Providers = new List<ModelRegistry.ProviderDescriptor>
                    {
                        new()
                        {
                            Name = "Mock",
                            Slug = "mock",
                            Models = new List<ModelRegistry.ModelDescriptor>
                            {
                                new() { Id = "mock", ContextTokens = 1024 }
                            }
                        }
                    }
                };

                return Task.FromResult(new ModelRegistryLoadResult(registry, ModelRegistryLoadSource.Network, null));
            }
        }

        [Fact]
        public async Task Should_share_cache_across_instances_when_shared_cache_enabled()
        {
            ModelRegistryLoader.InvalidateSharedCache();

            var cachePath = Path.Combine(Path.GetTempPath(), "brainarr-tests", Guid.NewGuid().ToString("N"), "shared.json");
            var backend = new CountingBackend();
            var options = new ModelRegistryLoaderOptions { EnableSharedCache = true, SharedCacheTtl = TimeSpan.FromMinutes(5) };

            var loaderA = new RegistryModelRegistryLoader(
                httpClient: new HttpClient(new SequenceMessageHandler()),
                cacheFilePath: cachePath,
                embeddedRegistryPath: ResolveExamplePath(),
                options: options,
                customLoader: backend.LoadAsync);

            var loaderB = new RegistryModelRegistryLoader(
                httpClient: new HttpClient(new SequenceMessageHandler()),
                cacheFilePath: cachePath,
                embeddedRegistryPath: ResolveExamplePath(),
                options: options,
                customLoader: backend.LoadAsync);

            var resultA = await loaderA.LoadAsync("https://cache.test/models.json", default);
            var resultB = await loaderB.LoadAsync("https://cache.test/models.json", default);

            backend.LoadCount.Should().Be(1);
            resultB.Registry.Should().BeSameAs(resultA.Registry);
        }

        [Fact]
        public async Task Should_refresh_shared_cache_after_ttl_expiry()
        {
            ModelRegistryLoader.InvalidateSharedCache();

            var cachePath = Path.Combine(Path.GetTempPath(), "brainarr-tests", Guid.NewGuid().ToString("N"), "ttl.json");
            var backend = new CountingBackend();
            var options = new ModelRegistryLoaderOptions { EnableSharedCache = true, SharedCacheTtl = TimeSpan.FromMilliseconds(50) };

            var loader = new RegistryModelRegistryLoader(
                httpClient: new HttpClient(new SequenceMessageHandler()),
                cacheFilePath: cachePath,
                embeddedRegistryPath: ResolveExamplePath(),
                options: options,
                customLoader: backend.LoadAsync);

            await loader.LoadAsync("https://ttl.test/models.json", default);
            await Task.Delay(75);
            await loader.LoadAsync("https://ttl.test/models.json", default);

            backend.LoadCount.Should().Be(2);
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
