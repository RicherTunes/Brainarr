using System;
using System.Text;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Canonicalizes provider display names into rate-limiter bucket keys.
    ///
    /// <para>
    /// AIService derives a per-vendor rate-limit "resource" from the provider's
    /// <c>DisplayName</c>. Lowercasing alone is insufficient because several display
    /// names contain dots, spaces, or parentheses ("Z.AI GLM", "LM Studio", "Claude
    /// Code (Subscription)") that no <see cref="RateLimiterConfiguration"/> bucket
    /// would match — silently bypassing every per-vendor cap. This was the
    /// "AIService rate-limit key shape" bug class (root-caused 2026-05-10).
    /// </para>
    ///
    /// <para>
    /// Canonicalization strips ALL non-alphanumeric characters and lowercases. Bucket
    /// names in <see cref="RateLimiterConfiguration"/> MUST be the canonical form so
    /// the lookup always hits.
    /// </para>
    /// </summary>
    public static class AIServiceResourceKeys
    {
        private const string FallbackKey = "unknown";

        /// <summary>
        /// Maps a provider <c>DisplayName</c> (or any free-form identifier) to the
        /// stable, dictionary-safe bucket key used by the rate limiter.
        /// </summary>
        /// <param name="displayName">Provider display name as exposed by <c>ILlmProvider.DisplayName</c>.</param>
        /// <returns>Lowercase alphanumeric key; <c>"unknown"</c> if input is null/empty/whitespace.</returns>
        public static string ToCanonicalKey(string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return FallbackKey;

            var sb = new StringBuilder(displayName.Length);
            foreach (var c in displayName)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }

            return sb.Length == 0 ? FallbackKey : sb.ToString();
        }
    }
}
