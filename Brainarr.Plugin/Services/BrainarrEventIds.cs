using Microsoft.Extensions.Logging;

namespace NzbDrone.Core.ImportLists.Brainarr.Services
{
    /// <summary>
    /// Brainarr-local event IDs that don't have a direct equivalent in common's
    /// <see cref="Lidarr.Plugin.Common.Observability.LlmEventIds"/>. These are emitted
    /// via NLog using <see cref="LogEventInfo.Properties"/>["EventId"] so log filters/dashboards
    /// can route them.
    ///
    /// Numbering scheme:
    /// - 12000-12099: Configuration/wiring warnings (e.g., DI fallback paths)
    /// - 12100-12199: Tokenizer/prompt-budget events (reserved)
    ///
    /// LLM provider events (auth, request lifecycle, rate limits, health) should use
    /// <c>Lidarr.Plugin.Common.Observability.LlmEventIds</c> directly so they align across
    /// the plugin ecosystem.
    /// </summary>
    public static class BrainarrEventIds
    {
        /// <summary>
        /// Provider was constructed without an injected <c>IHttpResilience</c> and is using
        /// the static fallback resilience pipeline. Logged warn-once per provider type.
        /// </summary>
        public static readonly EventId ResilienceFallback = new(12001, "ResilienceFallback");
    }
}
