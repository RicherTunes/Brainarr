using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.Services.Core
{
    public class ProviderCapabilityDetectorTests
    {
        private class FakeProvider : IAIProvider
        {
            public string ProviderName { get; }
            public FakeProvider(string name) { ProviderName = name; }
            public Task<List<NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation>> GetRecommendationsAsync(string prompt) => Task.FromResult(new List<NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation>());
            public Task<bool> TestConnectionAsync() => Task.FromResult(true);
            public void UpdateModel(string modelName) { }
        }

        [Fact]
        public async Task GetCapabilitiesAsync_uses_cache_and_detects_locals()
        {
            IProviderCapabilities det = new NzbDrone.Core.ImportLists.Brainarr.Services.ProviderCapabilityDetector(LogManager.GetCurrentClassLogger());
            var cap1 = await det.GetCapabilitiesAsync(new FakeProvider("Ollama"));
            cap1.IsLocalProvider.Should().BeTrue();
            cap1.MaxTokens.Should().Be(4096);

            // Should come from cache on second call
            var cap2 = await det.GetCapabilitiesAsync(new FakeProvider("Ollama"));
            ReferenceEquals(cap1, cap2).Should().BeTrue();
        }

        [Fact]
        public void SelectBestProvider_prefers_local_when_requested()
        {
            IProviderCapabilities det = new NzbDrone.Core.ImportLists.Brainarr.Services.ProviderCapabilityDetector(LogManager.GetCurrentClassLogger());
            var providers = new List<IAIProvider>
            {
                new FakeProvider("OpenAI"),
                new FakeProvider("LM Studio")
            };

            var req = new CapabilityRequirements
            {
                PreferLocalProvider = true,
                MinTokens = 512
            };

            var best = det.SelectBestProvider(providers, req);
            best.Should().NotBeNull();
            best!.ProviderName.Should().Contain("LM Studio");
        }
    }
}
