using System.Collections.Generic;
using Lidarr.Plugin.Common.Observability;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;

/// <summary>
/// Brainarr-local metrics injection point. Used by services that record domain
/// metrics (cache hit/miss, plan-cache events, prompt token counters, tokenizer fallbacks)
/// and want a swappable test double.
/// <para>
/// Phase 5f: now extends common's <see cref="IMetricsRecorder"/> so brainarr-side services
/// that take an <see cref="IMetrics"/> can also be wired with any
/// <see cref="IMetricsRecorder"/> implementation (e.g.,
/// <see cref="NullMetricsRecorder"/>, <see cref="ObservableMetricsRecorder"/>) — letting
/// adoption sites fan metrics out to Prometheus/StatsD/OpenTelemetry without taking a
/// metrics-library dependency at the common boundary.
/// </para>
/// <para>
/// <see cref="Record"/> is the legacy single-call surface; it is mapped onto
/// <see cref="IMetricsRecorder.Histogram"/> by default (one observation per call).
/// New call sites should prefer the dimensional surface
/// (<see cref="IMetricsRecorder.Increment"/>, <see cref="IMetricsRecorder.Gauge"/>,
/// <see cref="IMetricsRecorder.Histogram"/>) which carries explicit metric semantics.
/// </para>
/// <para>
/// The Prometheus aggregator (<see cref="Resilience.MetricsCollector"/>) keeps its own
/// static state for retention/exporter responsibilities and is not refactored in this
/// phase — see the Phase 5f commit message for the rationale.
/// </para>
/// </summary>
public interface IMetrics : IMetricsRecorder
{
    /// <summary>
    /// Records a single metric observation. Implementations must override; the default
    /// implementation routes through <see cref="IMetricsRecorder.Histogram"/> — Histogram is
    /// the safest one-to-one mapping for an arbitrary <c>(name, value, tags)</c> sample
    /// because <see cref="MetricKind.Histogram"/> is "one observation" by definition.
    /// </summary>
    void Record(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
        => Histogram(name, value, tags);

    // Default no-op implementations for the inherited IMetricsRecorder surface so that
    // legacy test doubles that only implement Record continue to compile. New code that
    // wants accurate counter/gauge/histogram semantics should override these explicitly.
    void IMetricsRecorder.Increment(string name, double value, IReadOnlyDictionary<string, string>? tags) { }
    void IMetricsRecorder.Gauge(string name, double value, IReadOnlyDictionary<string, string>? tags) { }
    void IMetricsRecorder.Histogram(string name, double value, IReadOnlyDictionary<string, string>? tags) { }
}

public sealed class NoOpMetrics : IMetrics
{
    public void Record(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
    {
    }

    public void Increment(string name, double value = 1.0, IReadOnlyDictionary<string, string>? tags = null)
    {
    }

    public void Gauge(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
    {
    }

    public void Histogram(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
    {
    }
}

/// <summary>
/// Adapter that lets brainarr services that already require <see cref="IMetrics"/> consume any
/// <see cref="IMetricsRecorder"/>. Constructs a brainarr <see cref="IMetrics"/> wrapper around an
/// arbitrary recorder so plugins can wire common's <see cref="ObservableMetricsRecorder"/> (or
/// any custom recorder) in DI without touching every <see cref="IMetrics"/>-typed parameter.
/// </summary>
/// <remarks>
/// Phase 5f: introduced when brainarr's <see cref="IMetrics"/> began extending common's
/// <see cref="IMetricsRecorder"/>. The adapter exists primarily for the case where the host
/// supplies an <see cref="IMetricsRecorder"/> (not an <see cref="IMetrics"/>) and brainarr
/// services still type their parameter as <see cref="IMetrics"/>.
/// </remarks>
public sealed class MetricsRecorderAdapter : IMetrics
{
    private readonly IMetricsRecorder _inner;

    public MetricsRecorderAdapter(IMetricsRecorder inner)
    {
        _inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
    }

    public void Record(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
        => _inner.Histogram(name, value, tags);

    public void Increment(string name, double value = 1.0, IReadOnlyDictionary<string, string>? tags = null)
        => _inner.Increment(name, value, tags);

    public void Gauge(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
        => _inner.Gauge(name, value, tags);

    public void Histogram(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
        => _inner.Histogram(name, value, tags);
}
