using NLog;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Prompting.Policies;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Capabilities;
using NzbDrone.Core.ImportLists.Brainarr.Services.Registry;
using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// Model-capacity-aware budget tests. The prompt token budget must scale with the
    /// model's context window: a frontier 128K/200K provider (z.ai GLM) should get a
    /// large prompt budget bounded by the cloud ceiling, while a small-context provider
    /// must stay modest. See TokenBudgetResolver.DefaultContextTokens + CloudPromptCeiling.
    /// </summary>
    [Trait("Category", "Unit")]
    [Trait("Category", "PromptBuilder")]
    public class TokenBudgetResolverTests
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private static TokenBudgetResolver CreateResolver()
        {
            // registryUrl null -> ModelContextResolver resolves no per-model context
            // tokens from the network registry, so the provider-level DefaultContextTokens
            // fallback is exercised (the path this feature widens for z.ai GLM).
            var modelContextResolver = new ModelContextResolver(Logger, new ModelRegistryLoader(), registryUrl: null);
            return new TokenBudgetResolver(Logger, modelContextResolver, new DefaultTokenBudgetPolicy());
        }

        [Fact]
        public void ZaiGlm_Comprehensive_GetsLargePromptBudget()
        {
            var resolver = CreateResolver();
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.ZaiGlm,
                SamplingStrategy = SamplingStrategy.Comprehensive,
                MaxRecommendations = 20
            };

            var budget = resolver.ResolvePromptBudget(settings, ProviderCapabilities.Get(AIProvider.ZaiGlm));

            // Today (RED): ZaiGlm is absent from DefaultContextTokens -> 24000 fallback,
            // then the flat 20000 cloud ceiling clamps it to ~20000. After the fix the
            // 128K provider default + 96K cloud ceiling yield a much larger budget.
            Assert.True(
                budget.PromptTokens >= 60000,
                $"Expected ZaiGlm comprehensive prompt budget >= 60000 (capacity-aware), got {budget.PromptTokens}");
        }

        [Fact]
        public void SmallContextProvider_StaysBelowCloudCeiling()
        {
            var resolver = CreateResolver();
            // Gemini has DefaultContextTokens == 32000 (a small-context cloud provider).
            // Its prompt budget must remain well under the cloud ceiling, proving small
            // models are unchanged by the capacity-aware widening.
            var settings = new BrainarrSettings
            {
                Provider = AIProvider.Gemini,
                SamplingStrategy = SamplingStrategy.Comprehensive,
                MaxRecommendations = 20
            };

            var budget = resolver.ResolvePromptBudget(settings, ProviderCapabilities.Get(AIProvider.Gemini));

            Assert.True(
                budget.PromptTokens < TokenBudgetResolver.CloudPromptCeiling,
                $"Expected small-context Gemini prompt budget < cloud ceiling {TokenBudgetResolver.CloudPromptCeiling}, got {budget.PromptTokens}");
            // A 32K-context provider should land in a modest band, nowhere near the ceiling.
            Assert.True(
                budget.PromptTokens <= 32000,
                $"Expected small-context Gemini prompt budget <= 32000, got {budget.PromptTokens}");
        }
    }
}
