using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Handles metrics snapshots and observability UI rendering.
    /// Extracted from BrainarrOrchestrator to separate UI/metrics concerns.
    /// </summary>
    public class ObservabilityService : IObservabilityService
    {
        private readonly ReviewQueueService _reviewQueue;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics _metrics;
        private readonly Func<string> _getProviderStatus;
        private readonly Logger _logger;

        public ObservabilityService(
            ReviewQueueService reviewQueue,
            NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics metrics,
            Func<string> getProviderStatus,
            Logger logger)
        {
            _reviewQueue = reviewQueue ?? throw new ArgumentNullException(nameof(reviewQueue));
            _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
            _getProviderStatus = getProviderStatus ?? throw new ArgumentNullException(nameof(getProviderStatus));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public object GetMetricsSnapshot()
        {
            var counts = _reviewQueue.GetCounts();
            var perf = _metrics.GetSnapshot();
            return new
            {
                review = new { pending = counts.pending, accepted = counts.accepted, rejected = counts.rejected, never = counts.never },
                cache = new { },
                provider = _getProviderStatus(),
                artistPromotion = new { events = perf.ArtistModeGatingEvents, promoted = perf.ArtistModePromotedRecommendations }
            };
        }

        public object GetObservabilitySummary(IDictionary<string, string> query)
        {
            try
            {
                var window = TimeSpan.FromMinutes(15);
                var lat = NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.GetAllMetrics("provider.latency", window);
                var err = NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.GetAllMetrics("provider.errors", window);
                var thr = NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.GetAllMetrics("provider.429", window);

                string Get(IDictionary<string, string> q, string k) => q != null && q.TryGetValue(k, out var v) ? v : null;
                var prov = Get(query, "provider");
                var mod = Get(query, "model");
                string sf(string v) { try { return NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.ProviderMetricsHelper.SanitizeName(v); } catch { return v; } }
                var pf = string.IsNullOrWhiteSpace(prov) ? null : sf(prov);
                var mf = string.IsNullOrWhiteSpace(mod) ? null : sf(mod);
                bool Match(string name) { if (pf != null && !name.Contains($".{pf}.", StringComparison.Ordinal)) return false; if (mf != null && !name.EndsWith($".{mf}", StringComparison.Ordinal)) return false; return true; }
                lat = lat.Where(kv => Match(kv.Key)).ToDictionary(k => k.Key, v => v.Value);
                err = err.Where(kv => Match(kv.Key)).ToDictionary(k => k.Key, v => v.Value);
                thr = thr.Where(kv => Match(kv.Key)).ToDictionary(k => k.Key, v => v.Value);

                double GetP(System.Collections.Generic.Dictionary<string, NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsSummary> d, string k, double def = 0)
                    => d.TryGetValue(k, out var s) && s?.Percentiles != null ? s.Percentiles.P95 : def;
                int GetC(System.Collections.Generic.Dictionary<string, NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsSummary> d, string k)
                    => d.TryGetValue(k, out var s) ? s.Count : 0;

                var keys = lat.Keys.Union(err.Keys).Union(thr.Keys).ToList();
                var rows = keys.Select(k => new
                {
                    key = k.Replace("provider.", string.Empty),
                    p95 = GetP(lat, k),
                    errors = GetC(err, k),
                    throttles = GetC(thr, k)
                })
                .OrderByDescending(x => x.p95)
                .Take(25)
                .Select(x => new { value = x.key, name = $"{x.key} \u2013 p95={x.p95:F0}ms, errors={x.errors}, 429={x.throttles}" })
                .ToList();

                return new { options = rows };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "observability/getoptions failed");
                return new { options = new[] { new { value = "error", name = ex.Message } } };
            }
        }

        public object GetObservabilityOptions()
        {
            try
            {
                return GetObservabilitySummary(new Dictionary<string, string>());
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Non-critical: Failed to get observability options");
                return new { options = Array.Empty<object>() };
            }
        }

        public string GetObservabilityHtml(IDictionary<string, string> query)
        {
            try
            {
                var window = TimeSpan.FromMinutes(15);
                var lat = NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.GetAllMetrics("provider.latency", window);
                var err = NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.GetAllMetrics("provider.errors", window);
                var thr = NzbDrone.Core.ImportLists.Brainarr.Services.Resilience.MetricsCollector.GetAllMetrics("provider.429", window);

                string Get(IDictionary<string, string> q, string k) => q != null && q.TryGetValue(k, out var v) ? v : null;
                var prov = Get(query, "provider");
                var mod = Get(query, "model");
                string sf(string v) { try { return NzbDrone.Core.ImportLists.Brainarr.Services.Telemetry.ProviderMetricsHelper.SanitizeName(v); } catch { return v; } }
                var pf = string.IsNullOrWhiteSpace(prov) ? null : sf(prov);
                var mf = string.IsNullOrWhiteSpace(mod) ? null : sf(mod);
                bool Match(string name) { if (pf != null && !name.Contains($".{pf}.", StringComparison.Ordinal)) return false; if (mf != null && !name.EndsWith($".{mf}", StringComparison.Ordinal)) return false; return true; }

                var keys = new System.Collections.Generic.HashSet<string>();
                foreach (var k in lat.Keys) if (Match(k)) keys.Add(k);
                foreach (var k in err.Keys) if (Match(k)) keys.Add(k);
                foreach (var k in thr.Keys) if (Match(k)) keys.Add(k);

                var rows = new System.Text.StringBuilder();
                rows.AppendLine("<table style='font-family:Segoe UI,Arial,sans-serif;border-collapse:collapse;'>");
                rows.AppendLine("<tr><th style='text-align:left;padding:6px;border-bottom:1px solid #ddd'>Series</th><th style='text-align:right;padding:6px;border-bottom:1px solid #ddd'>p95 (ms)</th><th style='text-align:right;padding:6px;border-bottom:1px solid #ddd'>Errors</th><th style='text-align:right;padding:6px;border-bottom:1px solid #ddd'>429</th></tr>");
                foreach (var k in keys)
                {
                    var series = k.Replace("provider.", string.Empty);
                    var p95 = lat.TryGetValue(k, out var s1) && s1?.Percentiles != null ? s1.Percentiles.P95 : 0;
                    var ec = err.TryGetValue(k, out var s2) ? s2.Count : 0;
                    var tc = thr.TryGetValue(k, out var s3) ? s3.Count : 0;
                    rows.AppendLine($"<tr><td style='padding:6px;border-bottom:1px solid #f0f0f0'>{System.Net.WebUtility.HtmlEncode(series)}</td><td style='text-align:right;padding:6px;border-bottom:1px solid #f0f0f0'>{p95:F0}</td><td style='text-align:right;padding:6px;border-bottom:1px solid #f0f0f0'>{ec}</td><td style='text-align:right;padding:6px;border-bottom:1px solid #f0f0f0'>{tc}</td></tr>");
                }
                rows.AppendLine("</table>");
                return $"<html><body><h3>Observability (last 15m)</h3>{rows}</body></html>";
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "observability/html failed");
                return $"<html><body><p>Error generating observability view: {System.Net.WebUtility.HtmlEncode(ex.Message)}</p></body></html>";
            }
        }
    }
}
