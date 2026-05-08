using System;
using FluentAssertions;
using Lidarr.Plugin.Common.Observability;
using NLog;
using Xunit;

#pragma warning disable CS0618 // Tests intentionally exercise obsolete SecureProviderBase
namespace Brainarr.Tests.Services
{
    /// <summary>
    /// M4-3 / Phase 4e: Contract tests verifying that sensitive data (API keys, tokens, emails,
    /// IP addresses) never appears in log output from <see cref="LogRedactor"/> (common) and
    /// SecureProviderBase.SanitizeForLogging.
    /// </summary>
    [Collection("ThreadSensitive")]
    public class LogRedactionContractTests
    {
        private static readonly SanitizeTestDouble Sanitizer = new SanitizeTestDouble();

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

        // ─── SecureProviderBase.SanitizeForLogging contract ──────────────────

        [Theory]
        [InlineData("API key: abcdefghijklmnopqrstuvwxyz12345678", "abcdefghijklmnopqrstuvwxyz12345678")]
        [InlineData("Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9xyz", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9xyz")]
        [InlineData("Contact admin@example.com for help", "admin@example.com")]
        [InlineData("Server at 192.168.1.100 is down", "192.168.1.100")]
        [InlineData("CC: 4111-1111-1111-1111 on file", "4111-1111-1111-1111")]
        // Provider-specific API key formats
        [InlineData("OpenAI key: sk-proj-abc123def456ghi789jkl012mno345pq", "sk-proj-abc123def456ghi789jkl012mno345pq")]
        [InlineData("Anthropic key: sk-ant-api03-abc123def456ghi789jkl012m", "sk-ant-api03-abc123def456ghi789jkl012m")]
        [InlineData("Groq key: gsk_abc123def456ghi789jkl012mno345pqr", "gsk_abc123def456ghi789jkl012mno345pqr")]
        public void SanitizeForLogging_RedactsSensitivePatterns(string input, string mustNotAppear)
        {
            var sanitized = InvokeSanitizeForLogging(input);
            sanitized.Should().NotContain(mustNotAppear, $"'{mustNotAppear}' must be redacted");
        }

        [Theory]
        [InlineData("password: myS3cretPass!")]
        [InlineData("token: abc123token")]
        [InlineData("secret: supersecretvalue")]
        [InlineData("auth: bearer_xyz")]
        public void SanitizeForLogging_RedactsSensitiveKeywords(string input)
        {
            var sanitized = InvokeSanitizeForLogging(input);
            sanitized.Should().Contain("[REDACTED-", "keyword-value pairs must be redacted");
        }

        // ─── Session ID and Authorization Header Redaction ─────────────────────

        // Session identifiers that ARE currently redacted (32+ alphanumeric pattern)
        [Theory]
        [InlineData("Session-ID: abc123def456ghi789jkl012mno345pqr", "abc123def456ghi789jkl012mno345pqr")]
        [InlineData("X-Session-Token: sess_a1b2c3d4e5f6g7h8i9j0kl1m2n3o4p5", "sess_a1b2c3d4e5f6g7h8i9j0kl1m2n3o4p5")]
        public void SanitizeForLogging_RedactsLongSessionIdentifiers(string input, string mustNotAppear)
        {
            var sanitized = InvokeSanitizeForLogging(input);
            sanitized.Should().NotContain(mustNotAppear, $"session identifier '{mustNotAppear}' must be redacted");
        }

        // Short session identifiers (< 32 chars) are NOT currently redacted - this documents the gap
        [Theory]
        [InlineData("Set-Cookie: JSESSIONID=1A2B3C4D5E6F7G8H9I0J; HttpOnly", "1A2B3C4D5E6F7G8H9I0J")]
        [InlineData("Cookie: session_id=xyz789abc123; path=/", "xyz789abc123")]
        public void SanitizeForLogging_ShortSessionIds_AreNotCurrentlyRedacted(string input, string patternThatRemains)
        {
            var sanitized = InvokeSanitizeForLogging(input);
            // This documents current behavior - short session IDs (< 32 chars) pass through
            sanitized.Should().Contain(patternThatRemains, "short session IDs are not currently redacted (gap documented for future improvement)");
        }

        // Authorization headers that ARE redacted (token keyword + value, or 32+ alphanumeric)
        [Theory]
        [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U")]
        [InlineData("X-Auth-Token: auth_token_abcdef12345678901234567890123456", "auth_token_abcdef12345678901234567890123456")]
        public void SanitizeForLogging_RedactsAuthorizationHeaders(string input, string mustNotAppear)
        {
            var sanitized = InvokeSanitizeForLogging(input);
            sanitized.Should().NotContain(mustNotAppear, $"auth token '{mustNotAppear}' must be redacted");
        }

        // Short auth values (< 32 chars without keyword patterns) pass through - documents the gap
        [Theory]
        [InlineData("Authorization: Basic dXNlcm5hbWU6cGFzc3dvcmQ=", "dXNlcm5hbWU6cGFzc3dvcmQ=")]
        public void SanitizeForLogging_ShortAuthValues_AreNotCurrentlyRedacted(string input, string patternThatRemains)
        {
            var sanitized = InvokeSanitizeForLogging(input);
            // This documents current behavior - base64 values without keyword patterns pass through
            sanitized.Should().Contain(patternThatRemains, "short auth values without keyword patterns are not currently redacted (gap documented)");
        }

        [Fact]
        public void SanitizeForLogging_PreservesNonSensitiveText()
        {
            var input = "Provider openai returned 5 recommendations in 250ms";
            var sanitized = InvokeSanitizeForLogging(input);
            sanitized.Should().Contain("Provider");
            sanitized.Should().Contain("5 recommendations");
            sanitized.Should().Contain("250ms");
        }

        [Fact]
        public void SanitizeForLogging_NullInput_ReturnsNull()
        {
            var sanitized = InvokeSanitizeForLogging(null);
            sanitized.Should().BeNull();
        }

        [Fact]
        public void SanitizeForLogging_EmptyInput_ReturnsEmpty()
        {
            var sanitized = InvokeSanitizeForLogging("");
            sanitized.Should().BeEmpty();
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        private static string InvokeSanitizeForLogging(string input)
        {
            // SanitizeForLogging is protected; expose it via a concrete test double.
            // Reuse a single instance to avoid per-test construction overhead and
            // contention on global provider initialization during full-suite runs.
            return Sanitizer.SanitizeForTest(input);
        }
    }

    /// <summary>
    /// Minimal concrete subclass of SecureProviderBase for testing protected methods.
    /// </summary>
    internal class SanitizeTestDouble : NzbDrone.Core.ImportLists.Brainarr.Services.Providers.SecureProviderBase
    {
        public SanitizeTestDouble()
            : base(LogManager.CreateNullLogger(), rateLimiter: null, sanitizer: null, maxConcurrency: 1)
        {
        }

        public string SanitizeForTest(string input) => SanitizeForLogging(input);

        public override string ProviderName => "test";
        public override bool RequiresApiKey => false;
        public override bool SupportsStreaming => false;
        public override int MaxRecommendations => 10;

        public override System.Threading.Tasks.Task<System.Collections.Generic.List<NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation>> GetRecommendationsAsync(string prompt)
            => System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.List<NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation>());

        public override System.Threading.Tasks.Task<bool> TestConnectionAsync()
            => System.Threading.Tasks.Task.FromResult(true);

        public override void UpdateModel(string modelName) { }

        protected override System.Threading.Tasks.Task<System.Collections.Generic.List<NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation>> GetRecommendationsInternalAsync(
            NzbDrone.Core.ImportLists.Brainarr.Models.LibraryProfile profile,
            int maxRecommendations,
            System.Threading.CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult(new System.Collections.Generic.List<NzbDrone.Core.ImportLists.Brainarr.Models.Recommendation>());

        protected override System.Threading.Tasks.Task<bool> TestConnectionInternalAsync(System.Threading.CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult(true);
    }
}
