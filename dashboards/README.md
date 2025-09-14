# Brainarr Dashboards

This folder contains Grafana dashboards and notes to visualize Brainarr’s model‑aware metrics.

## 1) Prerequisites

- Prometheus scraping the plugin export (example):

```yaml
scrape_configs:
  - job_name: 'brainarr'
    metrics_path: /importlist/brainarr?action=metrics/prometheus
    static_configs:
      - targets: ['<lidarr-host>:<port>']
```

- The Brainarr plugin must be enabled and producing runs (metrics are event‑driven).

## 2) Import the dashboard

- In Grafana: Dashboards → Import → Upload
- Select `docs/assets/grafana-brainarr-observability.json`
- Choose your Prometheus datasource when prompted

For a compact “at‑a‑glance” view, also try `docs/assets/grafana-brainarr-srebar.json`.

## 3) Dashboard variables

- `latency_series`: matches metric names like `provider_latency_*_p95`
- `errors_series`: matches `provider_errors.*`
- `throttle_series`: matches `provider_429.*`

Tip: Use the variable dropdowns to focus on a specific provider:model series.

## 4) Useful PromQL snippets

- Top 10 latency p95 series (by value):

```promql
Topk(10, {__name__=~"provider_latency_.*_p95"})
```

- Current p95 vs 24h baseline (%) across selected series:

```promql
100 * (
  max({__name__=~"$latency_series"})
  / clamp_min(max(avg_over_time({__name__=~"$latency_series"}[24h])), 1)
)
```

- Sum of errors over selected series (snapshot):

```promql
sum({__name__=~"$errors_series"})
```

- Sum of throttles (429) over selected series (snapshot):

```promql
sum({__name__=~"$throttle_series"})
```

> Note: The exporter emits snapshot gauges (count/avg/min/max/p50/p95/p99) each scrape. If you prefer time‑windowed aggregates, apply `*_over_time()` functions (e.g., `avg_over_time`) over these series.

## 5) Troubleshooting

- Empty panels: ensure Brainarr ran in the last 15 minutes or widen the time range.
- No metrics scraped: verify the `metrics_path`, container/host port, and that the import list action route is accessible from Prometheus.
- High 429 counts: Consider enabling Adaptive Throttling (Advanced → Hidden) and reducing per‑model concurrency caps.

## 6) Next ideas

- Add per‑provider template variables (regex extract from `__name__`) to filter by provider/model families.
- Create alert rules on p95 anomaly: e.g., `p95 now vs 24h baseline > 2.5x for 10m`.

## 7) Alerts (examples)

- Import `dashboards/alerts-brainarr.yaml` into your Prometheus Alertmanager rules.
  - BrainarrP95LatencyRegression: triggers when p95 exceeds 2.5× 24h baseline for 10 minutes.
  - BrainarrHighThrottleRate: triggers when 429 counters exceed a threshold for 10 minutes.

Adjust thresholds and `for:` durations to your environment.
