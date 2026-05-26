using FluentAssertions;
using Lidarr.Plugin.Common.Observability;
using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// M4-3 / Phase 4e: Contract tests verifying that sensitive data (API keys, tokens, emails,
    /// IP addresses) never appears in log output from <see cref="LogRedactor"/> (common).
    /// Note: SecureProviderBase.SanitizeForLogging tests were removed when SecureProviderBase
    /// was deleted (Wave-22 Sprint D). Log-redaction coverage now lives exclusively in
    /// LogRedactor (Common library).
    /// </summary>
    [Collection("ThreadSensitive")]
    public class LogRedactionContractTests
    {
        // ─── LogRedactor (from common): API key redaction ─────────────────────

        [Theory]
        [InlineData("sk-abc123def456ghi789jkl012mno345pq")]
        [InlineData("api_key=sk-secret123abc456def789ghi012jkl")]
        public void LogRedactor_RedactsOpenAiAndApiKey(string raw)
        {
            var redacted = LogRedactor.Redact(raw);
            redacted.Should().NotContain(raw);
            redacted.Should().Contain("REDACTED");
        }

        [Fact]
        public void LogRedactor_RedactsBearerToken()
        {
            var input = "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.signature";
            var redacted = LogRedactor.Redact(input);
            redacted.Should().NotContain("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.signature");
            redacted.Should().Contain("REDACTED");
        }

        [Fact]
        public void LogRedactor_NullInput_ReturnsEmpty()
        {
            LogRedactor.Redact(null).Should().BeEmpty();
        }

    }
}
