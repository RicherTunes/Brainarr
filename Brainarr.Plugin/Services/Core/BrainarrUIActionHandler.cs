using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;
using NzbDrone.Core.ImportLists.Brainarr.Services.Core;
using NzbDrone.Core.ImportLists.Brainarr.Services.Resilience;
using NzbDrone.Core.ImportLists.Brainarr.Services.Styles;
using NzbDrone.Core.ImportLists.Brainarr.Services.Support;
using NzbDrone.Core.ImportLists.Brainarr.Utils;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Core
{
    /// <summary>
    /// Centralized handler for UI actions in the Brainarr plugin.
    /// Delegates to specialized managers for different action types.
    /// </summary>
    public class BrainarrUIActionHandler : IBrainarrUIActionHandler
    {
        private readonly Logger _logger;
        private readonly IProviderLifecycleManager _providerLifecycleManager;
        private readonly IModelOptionsProvider _modelOptionsProvider;
        private readonly IReviewQueueManager _reviewQueueManager;
        private readonly IStyleCatalogService _styleCatalog;
        private readonly NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics _metrics;
        private readonly IProviderHealthMonitor _providerHealth;

        public BrainarrUIActionHandler(
            Logger logger,
            IProviderLifecycleManager providerLifecycleManager,
            IModelOptionsProvider modelOptionsProvider,
            IReviewQueueManager reviewQueueManager,
            IStyleCatalogService styleCatalog,
            NzbDrone.Core.ImportLists.Brainarr.Performance.IPerformanceMetrics metrics,
            IProviderHealthMonitor providerHealth)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _providerLifecycleManager = providerLifecycleManager ?? throw new ArgumentNullException(nameof(providerLifecycleManager));
            _modelOptionsProvider = modelOptionsProvider ?? throw new ArgumentNullException(nameof(modelOptionsProvider));
            _reviewQueueManager = reviewQueueManager ?? throw new ArgumentNullException(nameof(reviewQueueManager));
            _styleCatalog = styleCatalog ?? throw new ArgumentNullException(nameof(styleCatalog));
            _metrics = metrics;
            _providerHealth = providerHealth;
        }

        /// <summary>
        /// Handles a UI action and returns the result.
        /// </summary>
        /// <param name="action">Action name to handle.</param>
        /// <param name="query">Optional query parameters.</param>
        /// <param name="settings">Current settings.</param>
        /// <returns>Action result object.</returns>
        public object HandleAction(string action, IDictionary<string, string> query, BrainarrSettings settings)
        {
            _logger.Debug($"Handling UI action: {action}");

            try
            {
                return action.ToLowerInvariant() switch
                {
                    // Provider and model actions
                    "getmodeloptions" => SafeAsyncHelper.RunSafeSync(() => _modelOptionsProvider.GetModelOptionsAsync(settings, query)),
                    "detectmodels" => SafeAsyncHelper.RunSafeSync(() => _modelOptionsProvider.DetectModelsAsync(settings, query)),
                    "testconnection" => SafeAsyncHelper.RunSafeSync(() => _providerLifecycleManager.TestProviderConnectionAsync(settings)),
                    "getproviderstatus" => _providerLifecycleManager.GetProviderStatus(),

                    // Review Queue actions
                    "review/getqueue" => _reviewQueueManager.GetPendingItems(),
                    "review/accept" => HandleReviewAccept(query),
                    "review/reject" => HandleReviewReject(query),
                    "review/never" => HandleReviewNever(query),
                    "review/apply" => HandleReviewApply(settings, query),
                    "review/clear" => _reviewQueueManager.ClearApprovalSelections(settings),
                    "review/rejectselected" => HandleRejectSelected(settings, query),
                    "review/neverselected" => HandleNeverSelected(settings, query),
                    "review/getoptions" => _reviewQueueManager.GetReviewOptions(),
                    "review/getsummaryoptions" => _reviewQueueManager.GetReviewSummaryOptions(),

                    // Metrics and observability
                    "metrics/get" => GetMetricsSnapshot(),
                    "metrics/prometheus" => MetricsCollector.ExportPrometheus(),
                    "observability/get" => settings.EnableObservabilityPreview ? GetObservabilitySummary(query, settings) : new { disabled = true },
                    "observability/getoptions" => settings.EnableObservabilityPreview ? GetObservabilityOptions() : new { options = Array.Empty<object>() },
                    "observability/html" => settings.EnableObservabilityPreview ? GetObservabilityHtml(query) : "<html><body><p>Observability preview is disabled.</p></body></html>",

                    // Styles TagSelect options
                    "styles/getoptions" => GetStylesOptions(query),

                    _ => throw new NotSupportedException($"Action '{action}' is not supported")
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error handling action: {action}");
                return new { error = ex.Message };
            }
        }

        private object HandleReviewAccept(IDictionary<string, string> query)
        {
            var artist = query.TryGetValue("artist", out var a) ? a : null;
            var album = query.TryGetValue("album", out var b) ? b : null;
            var notes = query.TryGetValue("notes", out var n) ? n : null;
            return _reviewQueueManager.HandleReviewUpdate(artist, album, ReviewQueueService.ReviewStatus.Accepted, notes);
        }

        private object HandleReviewReject(IDictionary<string, string> query)
        {
            var artist = query.TryGetValue("artist", out var a) ? a : null;
            var album = query.TryGetValue("album", out var b) ? b : null;
            var notes = query.TryGetValue("notes", out var n) ? n : null;
            return _reviewQueueManager.HandleReviewUpdate(artist, album, ReviewQueueService.ReviewStatus.Rejected, notes);
        }

        private object HandleReviewNever(IDictionary<string, string> query)
        {
            var artist = query.TryGetValue("artist", out var a) ? a : null;
            var album = query.TryGetValue("album", out var b) ? b : null;
            var notes = query.TryGetValue("notes", out var n) ? n : null;
            return _reviewQueueManager.HandleReviewNever(artist, album, notes);
        }

        private object HandleReviewApply(BrainarrSettings settings, IDictionary<string, string> query)
        {
            var keysCsv = query.TryGetValue("keys", out var k) ? k : null;
            return _reviewQueueManager.ApplyApprovalsNow(settings, keysCsv);
        }

        private object HandleRejectSelected(BrainarrSettings settings, IDictionary<string, string> query)
        {
            var keysCsv = query.TryGetValue("keys", out var k) ? k : null;
            return _reviewQueueManager.RejectOrNeverSelected(settings, keysCsv, ReviewQueueService.ReviewStatus.Rejected);
        }

        private object HandleNeverSelected(BrainarrSettings settings, IDictionary<string, string> query)
        {
            var keysCsv = query.TryGetValue("keys", out var k) ? k : null;
            return _reviewQueueManager.RejectOrNeverSelected(settings, keysCsv, ReviewQueueService.ReviewStatus.Never);
        }

        private object GetMetricsSnapshot()
        {
            var counts = _reviewQueueManager.GetCounts();
            var perf = _metrics?.GetSnapshot();
            return new
            {
                review = new { pending = counts.pending, accepted = counts.accepted, rejected = counts.rejected, never = counts.never },
                cache = new { },
                provider = _providerLifecycleManager.GetProviderStatus(),
                artistPromotion = new { events = perf?.ArtistModeGatingEvents, promoted = perf?.ArtistModePromotedRecommendations }
            };
        }

        private object GetStylesOptions(IDictionary<string, string> query)
        {
            try
            {
                string Get(IDictionary<string, string> q, string k) => q != null && q.TryGetValue(k, out var v) ? v : null;
                var q = Get(query, "query") ?? string.Empty;
                var items = _styleCatalog.Search(q, 50)
                    .Select(s => new { value = s.Slug, name = s.Name })
                    .ToList();
                return new { options = items };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "styles/getoptions failed");
                return new { options = Array.Empty<object>() };
            }
        }

        private object GetObservabilitySummary(IDictionary<string, string> query, BrainarrSettings settings)
        {
            try
            {
                var window = TimeSpan.FromMinutes(15);
                var lat = MetricsCollector.GetAllMetrics("provider.latency", window);
                var err = MetricsCollector.GetAllMetrics("provider.errors", window);
                var thr = MetricsCollector.GetAllMetrics("provider.429", window);

                string Get(IDictionary<string, string> q, string k) => q != null && q.TryGetValue(k, out var v) ? v : null;
                var prov = Get(query, "provider");
                var mod = Get(query, "model");

                string sf(string v)
                {
                    try { return Services.Telemetry.ProviderMetricsHelper.SanitizeName(v); }
                    catch { return v; }
                }

                var pf = string.IsNullOrWhiteSpace(prov) ? null : sf(prov);
                var mf = string.IsNullOrWhiteSpace(mod) ? null : sf(mod);

                bool Match(string name)
                {
                    if (pf != null && !name.Contains($".{pf}.", StringComparison.Ordinal)) return false;
                    if (mf != null && !name.EndsWith($".{mf}", StringComparison.Ordinal)) return false;
                    return true;
                }

                lat = lat.Where(kv => Match(kv.Key)).ToDictionary(k => k.Key, v => v.Value);
                err = err.Where(kv => Match(kv.Key)).ToDictionary(k => k.Key, v => v.Value);
                thr = thr.Where(kv => Match(kv.Key)).ToDictionary(k => k.Key, v => v.Value);

                double GetP(System.Collections.Generic.Dictionary<string, MetricsSummary> d, string k, double def = 0)
                    => d.TryGetValue(k, out var s) && s?.Percentiles != null ? s.Percentiles.P95 : def;

                int GetC(System.Collections.Generic.Dictionary<string, MetricsSummary> d, string k)
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
                .Select(x => new { value = x.key, name = $"{x.key} — p95={x.p95:F0}ms, errors={x.errors}, 429={x.throttles}" })
                .ToList();

                return new { options = rows };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "observability/getoptions failed");
                return new { options = new[] { new { value = "error", name = ex.Message } } };
            }
        }

        private object GetObservabilityOptions()
        {
            try
            {
                return GetObservabilitySummary(new Dictionary<string, string>(), null);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Non-critical: Failed to get observability options");
                return new { options = Array.Empty<object>() };
            }
        }

        private string GetObservabilityHtml(IDictionary<string, string> query)
        {
            try
            {
                var window = TimeSpan.FromMinutes(15);
                var lat = MetricsCollector.GetAllMetrics("provider.latency", window);
                var err = MetricsCollector.GetAllMetrics("provider.errors", window);
                var thr = MetricsCollector.GetAllMetrics("provider.429", window);

                string Get(IDictionary<string, string> q, string k) => q != null && q.TryGetValue(k, out var v) ? v : null;
                var prov = Get(query, "provider");
                var mod = Get(query, "model");

                string sf(string v)
                {
                    try { return Services.Telemetry.ProviderMetricsHelper.SanitizeName(v); }
                    catch { return v; }
                }

                var pf = string.IsNullOrWhiteSpace(prov) ? null : sf(prov);
                var mf = string.IsNullOrWhiteSpace(mod) ? null : sf(mod);

                bool Match(string name)
                {
                    if (pf != null && !name.Contains($".{pf}.", StringComparison.Ordinal)) return false;
                    if (mf != null && !name.EndsWith($".{mf}", StringComparison.Ordinal)) return false;
                    return true;
                }

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
