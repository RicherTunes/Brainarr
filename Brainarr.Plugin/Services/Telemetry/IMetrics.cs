using System;
using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry;

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

public sealed class MetricsCollectorAdapter : IMetrics
{
    public void Record(string name, double value, IReadOnlyDictionary<string, string>? tags = null)
    {
        Dictionary<string, string>? tagDictionary = null;

        if (tags != null && tags.Count > 0)
        {
            tagDictionary = new Dictionary<string, string>(tags, StringComparer.Ordinal);
        }

        Services.Resilience.MetricsCollector.RecordMetric(name, value, tagDictionary);
    }
}
