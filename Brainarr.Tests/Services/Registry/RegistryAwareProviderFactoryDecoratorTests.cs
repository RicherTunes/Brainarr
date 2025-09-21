using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using HttpClient = System.Net.Http.HttpClient;
using RegistryModelRegistryLoader = NzbDrone.Core.ImportLists.Brainarr.Services.Registry.ModelRegistryLoader;

namespace Brainarr.Tests.Services.Registry
{
    public class RegistryAwareProviderFactoryDecoratorTests : IDisposable
    {
        private const string EnvVarName = "BRAINARR_TEST_OPENAI_KEY_DECORATOR";
        private readonly List<string> _tempDirectories = new();
        private readonly string? _originalEnvValue;

        public RegistryAwareProviderFactoryDecoratorTests()
        {
            RegistryAwareProviderFactoryDecorator.UseExternalModelRegistry = false;
            _originalEnvValue = Environment.GetEnvironmentVariable(EnvVarName);
        }

        [Fact]
        public void Should_delegate_to_inner_factory_when_feature_flag_disabled()
        {
            var inner = new Mock<IProviderFactory>();
            var loader = CreateLoaderWithRegistry("{\"version\":\"1\",\"providers\":[]}");
            var decorator = new RegistryAwareProviderFactoryDecorator(inner.Object, loader, null);
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            var httpClient = Mock.Of<IHttpClient>();
            var logger = LogManager.GetLogger(nameof(Should_delegate_to_inner_factory_when_feature_flag_disabled));
            var provider = Mock.Of<IAIProvider>();
            BrainarrSettings? observed = null;

            inner.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                .Callback<BrainarrSettings, IHttpClient, Logger>((s, _, _) => observed = s)
                .Returns(provider);

            var result = decorator.CreateProvider(settings, httpClient, logger);

            result.Should().Be(provider);
            observed.Should().BeSameAs(settings);
            settings.ManualModelId.Should().BeNull();
        }

        [Fact]
        public async Task Should_apply_registry_model_and_environment_api_key_when_enabled()
        {
            RegistryAwareProviderFactoryDecorator.UseExternalModelRegistry = true;
            var expectedKey = Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable(EnvVarName, expectedKey);

            var registryJson = @"{
              ""version"": ""1"",
              ""providers"": [
                {
                  ""name"": ""OpenAI"",
                  ""slug"": ""openai"",
                  ""endpoint"": ""https://example.com"",
                  ""auth"": { ""type"": ""bearer"", ""env"": ""BRAINARR_TEST_OPENAI_KEY_DECORATOR"" },
                  ""models"": [
                    {
                      ""id"": ""gpt-test"",
                      ""context_tokens"": 100000,
                      ""capabilities"": { ""stream"": true, ""json_mode"": true, ""tools"": true }
                    }
                  ],
                  ""timeouts"": { ""connect_ms"": 1000, ""request_ms"": 1000 },
                  ""retries"": { ""max"": 1, ""backoff_ms"": 1 }
                }
              ]
            }";

            var loader = CreateLoaderWithRegistry(registryJson);
            var preload = await loader.LoadAsync(null, default);
            preload.Registry.Should().NotBeNull();
            preload.Registry!.Providers.Should().NotBeEmpty();
            preload.Registry.Providers[0].Models.Should().NotBeEmpty();
            preload.Registry.Providers[0].Models[0].Id.Should().Be("gpt-test");
            var inner = new Mock<IProviderFactory>();
            var decorator = new RegistryAwareProviderFactoryDecorator(inner.Object, loader, null);
            var ensureMethod = typeof(RegistryAwareProviderFactoryDecorator)
                .GetMethod("EnsureRegistryLoaded", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            ensureMethod.Should().NotBeNull();
            var ensured = (ModelRegistry?)ensureMethod!.Invoke(decorator, new object?[] { default(CancellationToken) });
            ensured.Should().NotBeNull();
            ensured!.Providers.Should().NotBeEmpty();
            ensured.Providers[0].Models.Should().NotBeEmpty();
            ensured.Providers[0].Models[0].Id.Should().Be("gpt-test");
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };
            settings.ManualModelId.Should().BeNull();
            var httpClient = Mock.Of<IHttpClient>();
            var logger = LogManager.GetLogger(nameof(Should_apply_registry_model_and_environment_api_key_when_enabled));
            var provider = Mock.Of<IAIProvider>();
            BrainarrSettings? observed = null;
            string? manualModelDuringInvocation = null;
            string? apiKeyDuringInvocation = null;
            string? providerModelDuringInvocation = null;

            inner.Setup(f => f.CreateProvider(It.IsAny<BrainarrSettings>(), It.IsAny<IHttpClient>(), It.IsAny<Logger>()))
                .Callback<BrainarrSettings, IHttpClient, Logger>((s, _, _) =>
                {
                    observed = s;
                    manualModelDuringInvocation = s.ManualModelId;
                    apiKeyDuringInvocation = s.OpenAIApiKey;
                    providerModelDuringInvocation = s.OpenAIModelId;
                })
                .Returns(provider);

            var result = decorator.CreateProvider(settings, httpClient, logger);

            result.Should().Be(provider);
            observed.Should().NotBeNull();
            observed.Should().BeSameAs(settings);
            manualModelDuringInvocation.Should().Be("gpt-test");
            apiKeyDuringInvocation.Should().Be(expectedKey);
            providerModelDuringInvocation.Should().Be("gpt-test");
            settings.ManualModelId.Should().BeNull();
            settings.OpenAIApiKey.Should().BeNull();
            settings.OpenAIModelId.Should().BeNull();
        }

        [Fact]
        public void Should_report_unavailable_when_required_environment_variable_is_missing()
        {
            RegistryAwareProviderFactoryDecorator.UseExternalModelRegistry = true;
            Environment.SetEnvironmentVariable(EnvVarName, null);

            var registryJson = @"{
              ""version"": ""1"",
              ""providers"": [
                {
                  ""name"": ""OpenAI"",
                  ""slug"": ""openai"",
                  ""endpoint"": ""https://example.com"",
                  ""auth"": { ""type"": ""bearer"", ""env"": ""BRAINARR_TEST_OPENAI_KEY_DECORATOR"" },
                  ""models"": [
                    {
                      ""id"": ""gpt-test"",
                      ""context_tokens"": 100000,
                      ""capabilities"": { ""stream"": true, ""json_mode"": true, ""tools"": true }
                    }
                  ],
                  ""timeouts"": { ""connect_ms"": 1000, ""request_ms"": 1000 },
                  ""retries"": { ""max"": 1, ""backoff_ms"": 1 }
                }
              ]
            }";

            var loader = CreateLoaderWithRegistry(registryJson);
            var inner = new Mock<IProviderFactory>(MockBehavior.Strict);
            var decorator = new RegistryAwareProviderFactoryDecorator(inner.Object, loader, null);
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };

            var available = decorator.IsProviderAvailable(AIProvider.OpenAI, settings);

            available.Should().BeFalse();
            inner.Verify(f => f.IsProviderAvailable(It.IsAny<AIProvider>(), It.IsAny<BrainarrSettings>()), Times.Never);
        }

        [Fact]
        public void Should_report_unavailable_when_override_model_not_present_in_registry()
        {
            RegistryAwareProviderFactoryDecorator.UseExternalModelRegistry = true;
            var registryJson = @"{
              ""version"": ""1"",
              ""providers"": [
                {
                  ""name"": ""OpenAI"",
                  ""slug"": ""openai"",
                  ""endpoint"": ""https://example.com"",
                  ""auth"": { ""type"": ""none"" },
                  ""models"": [
                    {
                      ""id"": ""gpt-test"",
                      ""context_tokens"": 100000,
                      ""capabilities"": { ""stream"": true, ""json_mode"": true, ""tools"": true }
                    }
                  ],
                  ""timeouts"": { ""connect_ms"": 1000, ""request_ms"": 1000 },
                  ""retries"": { ""max"": 1, ""backoff_ms"": 1 }
                }
              ]
            }";

            var loader = CreateLoaderWithRegistry(registryJson);
            var inner = new Mock<IProviderFactory>(MockBehavior.Strict);
            var decorator = new RegistryAwareProviderFactoryDecorator(inner.Object, loader, null);
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, ManualModelId = "non-existent" };

            var available = decorator.IsProviderAvailable(AIProvider.OpenAI, settings);

            available.Should().BeFalse();
            inner.Verify(f => f.IsProviderAvailable(It.IsAny<AIProvider>(), It.IsAny<BrainarrSettings>()), Times.Never);
        }

        private RegistryModelRegistryLoader CreateLoaderWithRegistry(string json)
        {
            var directory = Path.Combine(Path.GetTempPath(), "brainarr-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            _tempDirectories.Add(directory);
            var path = Path.Combine(directory, "registry.json");
            File.WriteAllText(path, json);

            return new RegistryModelRegistryLoader(
                httpClient: new HttpClient(new ThrowingHandler()),
                cacheFilePath: path,
                embeddedRegistryPath: path);
        }

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("HTTP access should not be used during tests");
            }
        }

        public void Dispose()
        {
            RegistryAwareProviderFactoryDecorator.UseExternalModelRegistry = false;
            Environment.SetEnvironmentVariable(EnvVarName, _originalEnvValue);
            foreach (var directory in _tempDirectories)
            {
                try
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, recursive: true);
                    }
                }
                catch
                {
                    // Ignore cleanup errors in tests
                }
            }
        }
    }
}
