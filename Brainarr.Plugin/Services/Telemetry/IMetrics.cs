using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;

/// <summary>
/// Brainarr-local metrics injection point. Used by services that record domain
/// metrics (cache hit/miss, plan-cache events, prompt token counters, tokenizer fallbacks)
/// and want a swappable test double.
/// <para>
/// This is NOT a duplicate of <c>Lidarr.Plugin.Common.Observability.Metrics</c> — common's
/// surface is a static no-op counter set with no dimensions. Brainarr requires
/// <c>name + value + tags</c> shape so the per-provider/per-model metric aggregator
/// (<see cref="Resilience.MetricsCollector"/>) can publish them via Prometheus.
/// </para>
/// <para>
/// Promotion candidate: a richer <c>IMetrics</c> shape with named metrics and tag dimensions
/// would be a useful addition to <c>Lidarr.Plugin.Common.Observability</c>.
/// </para>
/// </summary>
public interface IMetrics
{
    void Record(string name, double value, IReadOnlyDictionary<string, string>? tags = null);
}

public sealed class NoOpMetrics : IMetrics
{
    public void Record(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
    {
    }
}
