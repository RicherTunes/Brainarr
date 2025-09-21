using RegistryModelRegistryLoader = NzbDrone.Core.ImportLists.Brainarr.Services.Registry.ModelRegistryLoader;
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

namespace Brainarr.Tests.Services.Registry
{
    public class AIProviderFactoryRegistryTests : IDisposable
    {
        private const string EnvVarName = "BRAINARR_TEST_OPENAI_KEY_FACTORY";
        private readonly List<string> _tempDirectories = new();
        private readonly string? _originalEnvValue;

        public AIProviderFactoryRegistryTests()
        {
            AIProviderFactory.UseExternalModelRegistry = false;
            _originalEnvValue = Environment.GetEnvironmentVariable(EnvVarName);
        }

        [Fact]
        public void Should_delegate_to_registry_when_feature_flag_disabled()
        {
            AIProviderFactory.UseExternalModelRegistry = false;
            var registry = new Mock<IProviderRegistry>();
            var httpClient = Mock.Of<IHttpClient>();
            var logger = LogManager.GetLogger(nameof(Should_delegate_to_registry_when_feature_flag_disabled));
            var provider = Mock.Of<IAIProvider>();
            BrainarrSettings? observedSettings = null;

            registry.Setup(r => r.CreateProvider(AIProvider.OpenAI, It.IsAny<BrainarrSettings>(), httpClient, logger))
                    .Callback<AIProvider, BrainarrSettings, IHttpClient, Logger>((_, s, _, _) => observedSettings = s)
                    .Returns(provider);

            var factory = new AIProviderFactory(registry.Object, null, null);
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, OpenAIApiKey = "existing" };

            var result = factory.CreateProvider(settings, httpClient, logger);

            result.Should().Be(provider);
            registry.Verify(r => r.CreateProvider(AIProvider.OpenAI, It.IsAny<BrainarrSettings>(), httpClient, logger), Times.Once);
            observedSettings.Should().BeSameAs(settings);
        }

        [Fact]
        public void Should_apply_registry_model_and_environment_api_key_when_enabled()
        {
            AIProviderFactory.UseExternalModelRegistry = true;
            var expectedKey = Guid.NewGuid().ToString("N");
            Environment.SetEnvironmentVariable(EnvVarName, expectedKey);

            var registryJson = @"{
              ""version"": ""1"",
              ""providers"": [
                {
                  ""name"": ""OpenAI"",
                  ""slug"": ""openai"",
                  ""endpoint"": ""https://example.com"",
                  ""auth"": { ""type"": ""bearer"", ""env"": ""BRAINARR_TEST_OPENAI_KEY_FACTORY"" },
                  ""defaultModel"": ""gpt-test"",
                  ""models"": [
                    {
                      ""id"": ""gpt-test"",
                      ""context_tokens"": 100000,
                      ""aliases"": [""gpt-4o""],
                      ""metadata"": { ""tier"": ""default"" },
                      ""capabilities"": { ""stream"": true, ""json_mode"": true, ""tools"": true }
                    }
                  ],
                  ""timeouts"": { ""connect_ms"": 1000, ""request_ms"": 1000 },
                  ""retries"": { ""max"": 1, ""backoff_ms"": 1 }
                }
              ]
            }";

            var loader = CreateLoaderWithRegistry(registryJson);
            var registry = new Mock<IProviderRegistry>();
            var httpClient = Mock.Of<IHttpClient>();
            var logger = LogManager.GetLogger(nameof(Should_apply_registry_model_and_environment_api_key_when_enabled));
            var provider = Mock.Of<IAIProvider>();
            string? manualDuringCall = null;
            string? providerModelDuringCall = null;
            string? apiKeyDuringCall = null;

            registry.Setup(r => r.CreateProvider(AIProvider.OpenAI, It.IsAny<BrainarrSettings>(), httpClient, logger))
                    .Callback<AIProvider, BrainarrSettings, IHttpClient, Logger>((_, s, _, _) =>
                    {
                        manualDuringCall = s.ManualModelId;
                        providerModelDuringCall = s.OpenAIModelId;
                        apiKeyDuringCall = s.OpenAIApiKey;
                    })
                    .Returns(provider);

            var factory = new AIProviderFactory(registry.Object, loader, null);
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI };

            var result = factory.CreateProvider(settings, httpClient, logger);

            result.Should().Be(provider);
            registry.Verify(r => r.CreateProvider(AIProvider.OpenAI, It.IsAny<BrainarrSettings>(), httpClient, logger), Times.Once);
            manualDuringCall.Should().BeNull();
            providerModelDuringCall.Should().Be("gpt-test");
            apiKeyDuringCall.Should().Be(expectedKey);

            // Original settings should be restored after the factory call.
            settings.ManualModelId.Should().BeNull();
            settings.OpenAIModelId.Should().BeNull();
            settings.OpenAIApiKey.Should().BeNull();
        }

        [Fact]
        public void Should_report_unavailable_when_required_environment_variable_is_missing()
        {
            AIProviderFactory.UseExternalModelRegistry = true;
            Environment.SetEnvironmentVariable(EnvVarName, null);

            var registryJson = @"{
              ""version"": ""1"",
              ""providers"": [
                {
                  ""name"": ""OpenAI"",
                  ""slug"": ""openai"",
                  ""endpoint"": ""https://example.com"",
                  ""auth"": { ""type"": ""bearer"", ""env"": ""BRAINARR_TEST_OPENAI_KEY_FACTORY"" },
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
            var registry = Mock.Of<IProviderRegistry>();
            var factory = new AIProviderFactory(registry, loader, null);
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, OpenAIApiKey = "placeholder" };

            var available = factory.IsProviderAvailable(AIProvider.OpenAI, settings);

            available.Should().BeFalse();
            settings.OpenAIApiKey.Should().Be("placeholder");
        }

        [Fact]
        public void Should_report_unavailable_when_manual_model_not_present()
        {
            AIProviderFactory.UseExternalModelRegistry = true;

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
                      ""aliases"": [""gpt-4o""],
                      ""capabilities"": { ""stream"": true, ""json_mode"": true, ""tools"": true }
                    }
                  ],
                  ""timeouts"": { ""connect_ms"": 1000, ""request_ms"": 1000 },
                  ""retries"": { ""max"": 1, ""backoff_ms"": 1 }
                }
              ]
            }";

            var loader = CreateLoaderWithRegistry(registryJson);
            var registry = Mock.Of<IProviderRegistry>();
            var factory = new AIProviderFactory(registry, loader, null);
            var settings = new BrainarrSettings { Provider = AIProvider.OpenAI, ManualModelId = "non-existent" };

            var available = factory.IsProviderAvailable(AIProvider.OpenAI, settings);

            available.Should().BeFalse();
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
            AIProviderFactory.UseExternalModelRegistry = false;
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
