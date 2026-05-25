using System;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using Xunit;

namespace Brainarr.Tests.DependencyInjection
{
    /// <summary>
    /// DI-wiring and ecosystem-parity tests for brainarr services.
    /// </summary>
    /// <remarks>
    /// Wave-23 relocation: previously lived under <c>Brainarr.Tests/TechDebt/</c> alongside
    /// <c>TechDebtRemediationServiceTests</c> (deleted in Wave-22 with the underlying service).
    /// Moved here once it became the sole survivor — the parent directory no longer reflects
    /// the scope of this file (DI wiring, not tech-debt remediation).
    /// </remarks>
    [Trait("Category", "Unit")]
    public class DIWiringAndParityTests
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        // ── DI Resolution ──────────────────────────────────────────

        [Fact]
        public void IRecommendationCache_resolves_from_DI()
        {
            var services = new ServiceCollection();
            services.AddSingleton(_logger);
            services.AddSingleton<IRecommendationCache>(sp => new RecommendationCache(sp.GetRequiredService<Logger>()));

            using var provider = services.BuildServiceProvider();
            var cache = provider.GetRequiredService<IRecommendationCache>();

            Assert.NotNull(cache);
            Assert.IsType<RecommendationCache>(cache);
        }

        // Note: ISecureLogger / SecureStructuredLogger / SensitiveDataMasker were removed
        // in Phase 4e (observability collapse). Sensitive data redaction is now handled by
        // Lidarr.Plugin.Common.Observability.LogRedactor at the call site, exercised by
        // LogRedactionContractTests.
    }
}
