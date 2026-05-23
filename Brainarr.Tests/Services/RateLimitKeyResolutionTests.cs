using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Lidarr.Plugin.Common.Abstractions.Llm;
using Moq;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.ImportLists.Brainarr;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services;
using NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm;
using Xunit;

namespace Brainarr.Tests.Services
{
    /// <summary>
    /// Pins the "rate-limit key shape" invariant: every provider's resource key
    /// (as derived by AIService) MUST resolve to a bucket configured by
    /// RateLimiterConfiguration.ConfigureDefaults. Otherwise the limiter silently
    /// bypasses every per-vendor cap — exactly the class of bug memory flags as
    /// "AIService rate-limit key shape" (treated as upstream bug for 6mo, root-caused
    /// 2026-05-10).
    ///
    /// Pre-fix: ZaiGlm, ZaiCoding, Gemini, LM Studio, Claude Code (Subscription),
    /// OpenAI Codex (Subscription) all produced resource keys with dots / spaces /
    /// parens that no configured bucket matched. RED phase of TDD for the fix.
    /// </summary>
    public class RateLimitKeyResolutionTests
    {
        // Every DisplayName surfaced by an ILlmProvider in this codebase. If a new
        // provider is added with a fancy display name, this list catches it.
        public static IEnumerable<object[]> AllProviderDisplayNames() => new[]
        {
            new object[] { "Ollama" },
            new object[] { "LM Studio" },
            new object[] { "OpenAI" },
            new object[] { "Anthropic" },
            new object[] { "OpenRouter" },
            new object[] { "DeepSeek" },
            new object[] { "Google Gemini" },
            new object[] { "Groq" },
            new object[] { "Perplexity" },
            new object[] { "Z.AI GLM" },
            new object[] { "Z.AI Coding Subscription" },
            new object[] { "Claude Code (Subscription)" },
            new object[] { "OpenAI Codex (Subscription)" },
        };

        [Theory]
        [MemberData(nameof(AllProviderDisplayNames))]
        public void ResourceKey_ResolvesToConfiguredBucket(string displayName)
        {
            var resource = AIServiceResourceKeys.ToCanonicalKey(displayName);
            var configured = GetConfiguredBucketNames();

            configured.Should().Contain(resource,
                $"every provider DisplayName must canonicalize to a configured RateLimiter bucket. " +
                $"DisplayName='{displayName}' produced key='{resource}', which is missing from RateLimiterConfiguration. " +
                $"Without a matching bucket, the limiter falls through and that vendor's RPM cap is silently bypassed.");
        }

        [Fact]
        public void CanonicalKey_StripsPunctuationAndSpaces()
        {
            // Specific cases that broke pre-fix.
            AIServiceResourceKeys.ToCanonicalKey("Z.AI GLM").Should().Be("zaiglm");
            AIServiceResourceKeys.ToCanonicalKey("Z.AI Coding Subscription").Should().Be("zaicodingsubscription");
            AIServiceResourceKeys.ToCanonicalKey("Google Gemini").Should().Be("googlegemini");
            AIServiceResourceKeys.ToCanonicalKey("LM Studio").Should().Be("lmstudio");
            AIServiceResourceKeys.ToCanonicalKey("Claude Code (Subscription)").Should().Be("claudecodesubscription");
            AIServiceResourceKeys.ToCanonicalKey("OpenAI Codex (Subscription)").Should().Be("openaicodexsubscription");
        }

        [Fact]
        public void CanonicalKey_HandlesNullSafely()
        {
            // Defensive: AIService falls back to "unknown" for null providerName today.
            AIServiceResourceKeys.ToCanonicalKey(null).Should().Be("unknown");
            AIServiceResourceKeys.ToCanonicalKey("").Should().Be("unknown");
            AIServiceResourceKeys.ToCanonicalKey("   ").Should().Be("unknown");
        }

        private static HashSet<string> GetConfiguredBucketNames()
        {
            // Probe by configuring a real limiter and reading back its configured resources.
            // We can't reach the inner dictionary, so use the public side effect: a configured
            // bucket returns a non-null token count, an unconfigured one returns null.
            var limiter = new RateLimiter(Brainarr.Tests.Helpers.TestLogger.CreateNullLogger());
            RateLimiterConfiguration.ConfigureDefaults(limiter);

            // Candidate keys we expect to find configured. If a key isn't here, the test
            // intentionally has zero knowledge of it — but the production code declares
            // the bucket set, so this is the closed universe to probe.
            var candidates = new[]
            {
                "ollama", "lmstudio", "openai", "anthropic", "openrouter", "deepseek",
                "googlegemini", "groq", "perplexity", "zaiglm", "zaicodingsubscription",
                "claudecodesubscription", "openaicodexsubscription", "claudecodecli",
                // Legacy / alternate spellings present in current code — keep checking to
                // catch buckets configured under a non-canonical name.
                "gemini", "claudecode", "codexsubscription",
            };

            return new HashSet<string>(
                candidates.Where(c => limiter.GetAvailableTokens(c) != null),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
