using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Service for metrics snapshots and observability UI rendering.
    /// Extracted from BrainarrOrchestrator to separate UI/metrics concerns.
    /// </summary>
    public interface IObservabilityService
    {
        object GetMetricsSnapshot();
        object GetObservabilitySummary(IDictionary<string, string> query);
        object GetObservabilityOptions();
        string GetObservabilityHtml(IDictionary<string, string> query);
    }
}
