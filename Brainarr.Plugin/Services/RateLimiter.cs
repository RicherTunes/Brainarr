using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Logging;
using CommonRateLimiter = Lidarr.Plugin.Common.Services.Resilience.TokenBucketRateLimiter;
using CommonRateLimitPresets = Lidarr.Plugin.Common.Services.Resilience.RateLimitPresets;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Interface for rate limiting resource access.
    /// Provides token bucket rate limiting for API calls and other throttled resources.
    /// </summary>
    /// <remarks>
    /// This interface is maintained for backwards compatibility within Brainarr.
    /// The implementation delegates to Lidarr.Plugin.Common's TokenBucketRateLimiter.
    /// </remarks>
    public interface IRateLimiter
    {
        Task<T> ExecuteAsync<T>(string resource, Func<Task<T>> action);
        Task<T> ExecuteAsync<T>(string resource, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
        void Configure(string resource, int maxRequests, TimeSpan period);
    }

    /// <summary>
    /// Token bucket rate limiter implementation for Brainarr.
    /// Delegates to Lidarr.Plugin.Common's TokenBucketRateLimiter.
    /// </summary>
    /// <remarks>
    /// Token Bucket Algorithm:
    /// - Tokens are added at a fixed rate (refill rate = capacity / period)
    /// - Each request consumes one token
    /// - If no tokens available, request waits until tokens refill
    /// - Maximum tokens capped at capacity
    ///
    /// Example: 10 requests per minute
    /// - Capacity: 10 tokens
    /// - Refill rate: 10/60 = 0.167 tokens per second
    /// - Burst: Can handle 10 requests immediately
    /// - Sustained: ~0.167 requests per second
    ///
    /// Use cases in Brainarr:
    /// - AI provider API calls (OpenAI, Anthropic, etc.)
    /// - Local provider requests (Ollama, LM Studio)
    /// - MusicBrainz validation (strict 1 req/sec)
    /// </remarks>
    public class RateLimiter : IRateLimiter
    {
        private readonly CommonRateLimiter _innerLimiter;

        /// <summary>
        /// Initializes a new instance of the RateLimiter.
        /// </summary>
        /// <param name="logger">NLog Logger for diagnostic information</param>
        public RateLimiter(Logger logger)
        {
            // Adapt NLog Logger to ILogger for the common library
            var ilogger = logger != null ? NLogAdapterFactory.CreateILogger(logger) : null;
            _innerLimiter = new CommonRateLimiter(ilogger);
        }

        /// <summary>
        /// Initializes a new instance of the RateLimiter with an injectable TimeProvider.
        /// For production use the single-arg ctor; this overload is for deterministic tests.
        /// </summary>
        /// <param name="logger">NLog Logger for diagnostic information</param>
        /// <param name="timeProvider">Clock used by the token bucket for refill timing and Task.Delay waits.</param>
        public RateLimiter(Logger logger, TimeProvider timeProvider)
        {
            var ilogger = logger != null ? NLogAdapterFactory.CreateILogger(logger) : null;
            _innerLimiter = new CommonRateLimiter(ilogger, timeProvider);
        }

        /// <summary>
        /// Configures rate limiting for a specific resource.
        /// </summary>
        /// <param name="resource">Resource identifier (e.g., "openai", "musicbrainz")</param>
        /// <param name="maxRequests">Maximum requests allowed in the period</param>
        /// <param name="period">Time period for the rate limit</param>
        public void Configure(string resource, int maxRequests, TimeSpan period)
        {
            _innerLimiter.Configure(resource, maxRequests, period);
        }

        /// <summary>
        /// Executes an operation with rate limiting, waiting if necessary.
        /// </summary>
        /// <typeparam name="T">Return type of the operation</typeparam>
        /// <param name="resource">Resource identifier for rate limiting</param>
        /// <param name="action">The async operation to execute</param>
        /// <returns>Result of the operation</returns>
        public async Task<T> ExecuteAsync<T>(string resource, Func<Task<T>> action)
        {
            return await _innerLimiter.ExecuteAsync(resource, action).ConfigureAwait(false);
        }

        /// <summary>
        /// Executes an operation with rate limiting and cancellation support.
        /// </summary>
        /// <typeparam name="T">Return type of the operation</typeparam>
        /// <param name="resource">Resource identifier for rate limiting</param>
        /// <param name="action">The async operation to execute</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Result of the operation</returns>
        public async Task<T> ExecuteAsync<T>(string resource, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            return await _innerLimiter.ExecuteAsync(resource, action, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets the current available tokens for a resource.
        /// </summary>
        /// <param name="resource">Resource identifier</param>
        /// <returns>Number of available tokens, or null if resource not configured</returns>
        public int? GetAvailableTokens(string resource)
        {
            return _innerLimiter.GetAvailableTokens(resource);
        }

        /// <summary>
        /// Resets the rate limiter for a specific resource or all resources.
        /// </summary>
        /// <param name="resource">Resource to reset, or null to reset all</param>
        public void Reset(string resource = null)
        {
            _innerLimiter.Reset(resource);
        }
    }

    /// <summary>
    /// Provider-specific rate limiter configurations for Brainarr.
    /// </summary>
    public static class RateLimiterConfiguration
    {
        // Wave 53 leak fix: previously a static HashSet<IRateLimiter> retained every
        // limiter instance ever passed in, defeating GC and accumulating one
        // strong-ref per plugin reload. Switched to a ConditionalWeakTable so the
        // table auto-evicts entries once their IRateLimiter is otherwise unreachable.
        // The "configure once per instance" log-spam contract is preserved.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IRateLimiter, object> _configuredLimiters
            = new System.Runtime.CompilerServices.ConditionalWeakTable<IRateLimiter, object>();
        private static readonly object _sentinel = new object();

        /// <summary>
        /// Configures default rate limits for all Brainarr providers.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Idempotency contract: subsequent calls with the SAME limiter instance
        /// are silent no-ops. The first call wins. This is deliberate — repeated
        /// configuration log spam was a real problem on plugin-reload churn.
        /// </para>
        /// <para>
        /// One consequence: if <c>BRAINARR_RATELIMIT_*</c> env vars change between
        /// calls on the same instance, the second call will NOT pick up the change.
        /// Use <see cref="ReconfigureDefaults"/> when that's actually wanted (test
        /// suites that mutate env vars; settings-reload paths).
        /// </para>
        /// </remarks>
        public static void ConfigureDefaults(IRateLimiter rateLimiter)
        {
            if (rateLimiter is null) return;

            // TryAdd is atomic and idempotent: returns false if the limiter is
            // already registered, true on first registration. Using Add() here
            // would throw ArgumentException if two concurrent callers both passed
            // a prior TryGetValue check on the same instance.
            if (!_configuredLimiters.TryAdd(rateLimiter, _sentinel))
            {
                return;
            }

            void Cfg(string key, int defaultRpm) =>
                rateLimiter.Configure(key, ResolveRpm(key, defaultRpm), TimeSpan.FromMinutes(1));

            // Bucket keys are CANONICAL form (lowercase alphanumeric, no spaces/dots/parens)
            // because AIService derives the resource key by passing the provider's
            // DisplayName through AIServiceResourceKeys.ToCanonicalKey. Mismatch between
            // bucket name and resource key silently bypasses every per-vendor cap —
            // see the 2026-05-10 root-cause for the "AIService rate-limit key shape" bug.

            // Local providers — capped by the user's own hardware not by a vendor.
            // Pick a high cap so the bucket is effectively a sanity guard, not a throttle.
            Cfg("ollama", 120);
            Cfg("lmstudio", 120);  // DisplayName "LM Studio" → "lmstudio"

            // Cloud LLM provider RPM caps. Values target each vendor's free /
            // cheapest paid tier so users on those tiers don't hit 429s; users
            // on higher tiers can raise the cap via the BRAINARR_RATELIMIT_<KEY>_RPM
            // env var or by calling Configure(...) after this method. Each line
            // cites the source consulted; re-verify when the dated source is older
            // than ~6 months.
            // Anthropic: free tier 5 RPM (console.anthropic.com/settings/limits, 2026-01).
            Cfg("anthropic", 5);
            // Gemini: free tier 15 RPM for gemini-1.5-flash (ai.google.dev/gemini-api/docs/rate-limits, 2026-01).
            // DisplayName "Google Gemini" → "googlegemini".
            Cfg("googlegemini", 15);
            // Groq: free tier 30 RPM most models (console.groq.com/settings/limits, 2026-01).
            Cfg("groq", 30);
            // DeepSeek: documented no per-minute hard cap; conservative default 60 RPM (api-docs.deepseek.com/quick_start/rate_limit, 2026-01).
            Cfg("deepseek", 60);
            // OpenAI: free tier deprecated 2024; tier-1 paid is 500 RPM. Conservative 60 RPM avoids surprise burn on small accounts (platform.openai.com/docs/guides/rate-limits, 2026-01).
            Cfg("openai", 60);
            // OpenRouter: 20 RPM per model is the universal floor (openrouter.ai/docs/limits, 2026-01).
            Cfg("openrouter", 20);
            // Perplexity: paid sonar models ~20 RPM (docs.perplexity.ai/guides/rate-limits, 2026-01).
            Cfg("perplexity", 20);
            // ZaiGlm (Zhipu / 智谱): no published per-minute cap; conservative 30 RPM (open.bigmodel.cn docs, 2026-01).
            // DisplayName "Z.AI GLM" → "zaiglm".
            Cfg("zaiglm", 30);
            // ZaiCoding: Coding-Plan tier with per-key concurrency 5-10 depending on package
            // (z.ai/manage-apikey/rate-limits, 2026-05). Conservative 15 RPM ≈ 1/4-sec spacing.
            // DisplayName "Z.AI Coding Subscription" → "zaicodingsubscription".
            Cfg("zaicodingsubscription", 15);

            // Subscription paths — paid tier, more headroom than the metered tier above.
            // DisplayName "Claude Code (Subscription)" → "claudecodesubscription".
            Cfg("claudecodesubscription", 20);
            // DisplayName "OpenAI Codex (Subscription)" → "openaicodexsubscription".
            Cfg("openaicodexsubscription", 20);
            Cfg("claudecodecli", 20);

            // MusicBrainz - third-party metadata; their hard rule is 1 req/sec.
            rateLimiter.Configure("musicbrainz", 1, TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// Forces a re-application of defaults on the given limiter, bypassing the
        /// idempotency short-circuit. Use sparingly — replacing a bucket on a hot
        /// limiter drains its currently-available tokens.
        /// </summary>
        public static void ReconfigureDefaults(IRateLimiter rateLimiter)
        {
            if (rateLimiter is null) return;
            _configuredLimiters.Remove(rateLimiter);
            ConfigureDefaults(rateLimiter);
        }

        private static int ResolveRpm(string providerKey, int defaultRpm)
        {
            var envName = "BRAINARR_RATELIMIT_" + providerKey.ToUpperInvariant() + "_RPM";
            var raw = Environment.GetEnvironmentVariable(envName);
            if (string.IsNullOrWhiteSpace(raw)) return defaultRpm;
            return int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : defaultRpm;
        }
    }
}
