using System;
using System.Net.Http.Headers;
using Lidarr.Plugin.Common.Errors;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// Helpers for translating Lidarr's <see cref="HttpResponse"/> into the shape that common's
    /// <see cref="LlmErrorMapper"/> expects.
    ///
    /// <para>
    /// Phase 5f: common shipped <see cref="LlmErrorMapper.MapHttpError(string, System.Net.Http.HttpResponseMessage, string?, Exception?)"/>
    /// and the <see cref="LlmErrorMapper.MapHttpError(string, int, string?, TimeSpan?, Exception?)"/>
    /// overload that plumb <c>Retry-After</c> through to
    /// <see cref="LlmProviderException.RetryAfter"/>. Brainarr providers receive Lidarr's
    /// <see cref="HttpResponse"/> (NzbDrone.Common.Http) rather than
    /// <see cref="System.Net.Http.HttpResponseMessage"/>, so this helper bridges the gap by
    /// constructing a <see cref="RetryConditionHeaderValue"/> from the response header dictionary
    /// and reusing common's <see cref="LlmErrorMapper.ParseRetryAfterHeader"/> parser.
    /// </para>
    /// </summary>
    internal static class BrainarrHttpResponseHelpers
    {
        /// <summary>
        /// Extracts the <c>Retry-After</c> response header from a Lidarr <see cref="HttpResponse"/>
        /// and returns it as a <see cref="TimeSpan"/>. Returns <see langword="null"/> when the
        /// header is absent or unparseable.
        /// </summary>
        /// <remarks>
        /// The Lidarr <see cref="HttpResponse"/> exposes headers as a flat
        /// <c>HttpHeader</c>/string dictionary, so we manually try the <see cref="int"/>
        /// (delta seconds) and <see cref="DateTimeOffset"/> (HTTP-date) wire formats — same
        /// shape that <see cref="RetryConditionHeaderValue"/> would parse if we round-tripped
        /// through <see cref="System.Net.Http.HttpResponseMessage"/>. Falls back to common's
        /// <see cref="LlmErrorMapper.ParseRetryAfterHeader"/> for any header that
        /// <see cref="RetryConditionHeaderValue.TryParse(string?, out RetryConditionHeaderValue?)"/>
        /// can decode but our manual paths missed (e.g., quoted values, surrounding whitespace).
        /// </remarks>
        public static TimeSpan? ParseRetryAfter(HttpResponse? response)
        {
            try
            {
                if (response == null || response.Headers == null) return null;

                foreach (var header in response.Headers)
                {
                    if (!header.Key.Equals("Retry-After", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var raw = (header.Value ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(raw)) continue;

                    // Numeric delta (seconds) — most common shape from Anthropic, OpenAI, Gemini.
                    if (int.TryParse(raw, out var seconds) && seconds >= 0)
                    {
                        return TimeSpan.FromSeconds(seconds);
                    }

                    // HTTP-date — RFC 7231.
                    if (DateTimeOffset.TryParse(raw, out var when))
                    {
                        var delta = when - DateTimeOffset.UtcNow;
                        return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
                    }

                    // Final fallback: let common's parser try (handles edge formats).
                    if (RetryConditionHeaderValue.TryParse(raw, out var rch))
                    {
                        return LlmErrorMapper.ParseRetryAfterHeader(rch);
                    }
                }
            }
            catch
            {
                // Best-effort. Retry-After is advisory; absence must not break the error path.
            }

            return null;
        }
    }
}
