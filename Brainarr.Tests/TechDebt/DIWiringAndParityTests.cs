using System;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Logging;
using Xunit;

namespace Brainarr.Tests.TechDebt
{
    [Trait("Category", "TechDebt")]
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

        [Fact]
        public void ISecureLogger_resolves_from_DI()
        {
            var services = new ServiceCollection();
            services.AddSingleton(_logger);
            services.AddSingleton<ISecureLogger>(sp => new SecureStructuredLogger(sp.GetRequiredService<Logger>()));

            using var provider = services.BuildServiceProvider();
            var secureLogger = provider.GetRequiredService<ISecureLogger>();

            Assert.NotNull(secureLogger);
            Assert.IsType<SecureStructuredLogger>(secureLogger);
        }

        // ── SecureStructuredLogger: redaction ──────────────────────

        [Fact]
        public void SecureLogger_masks_api_key_patterns()
        {
            var masker = new SensitiveDataMasker();
            var input = "api_key=sk-1234567890abcdefghijklmnop";
            var masked = masker.MaskSensitiveData(input);

            Assert.DoesNotContain("sk-1234567890abcdefghijklmnop", masked);
            Assert.Contains("REDACTED", masked);
        }

        [Fact]
        public void SecureLogger_masks_jwt_tokens()
        {
            var masker = new SensitiveDataMasker();
            // JWT pattern: eyJ<header>.eyJ<payload>.<signature>
            var input = "token=eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abc123def456";
            var masked = masker.MaskSensitiveData(input);

            Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", masked);
            Assert.Contains("REDACTED", masked);
        }

        [Fact]
        public void SecureLogger_preserves_safe_text()
        {
            var masker = new SensitiveDataMasker();
            var input = "Processing album: Dark Side of the Moon by Pink Floyd";
            var masked = masker.MaskSensitiveData(input);

            Assert.Equal(input, masked);
        }

        [Fact]
        public void SecureLogger_instantiates_and_logs_without_error()
        {
            var logger = new SecureStructuredLogger(_logger);

            // Should not throw
            logger.LogInfo("Test message");
            logger.LogDebug("Debug", new { key = "value" });
            logger.LogWarning("Warning");
        }

        [Fact]
        public void SecureLogger_scope_creates_and_disposes()
        {
            var logger = new SecureStructuredLogger(_logger);

            using (var scope = logger.BeginScope("TestScope"))
            {
                logger.LogInfo("Inside scope");
                Assert.NotNull(scope);
            }
        }
    }
}
