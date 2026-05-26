using System;
using Lidarr.Plugin.Common.Services.Http;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Llm
{
    /// <summary>
    /// Helpers for translating Lidarr's <see cref="HttpResponse"/> into the shape that common's
    /// error-mapping stack expects.
    ///
    /// <para>
    /// Delegates to <see cref="HttpResponseHelpers.ParseRetryAfter"/> so the canonical
    /// <c>Retry-After</c> parsing logic lives in one place across the ecosystem.
    /// </para>
    /// </summary>
    internal static class BrainarrHttpResponseHelpers
    {
        /// <summary>
        /// Extracts the <c>Retry-After</c> response header from a Lidarr <see cref="HttpResponse"/>
        /// and returns it as a <see cref="TimeSpan"/>. Returns <see langword="null"/> when the
        /// header is absent or unparseable.
        /// </summary>
        public static TimeSpan? ParseRetryAfter(HttpResponse? response)
            => HttpResponseHelpers.ParseRetryAfter(response?.Headers);
    }
}
