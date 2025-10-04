# Brainarr Dashboards

This folder ships Grafana dashboards for Brainarr's planner and provider telemetry. They stay aligned with the metric names emitted in 1.3.x (prompt.plan_cache_*, prompt.tokens_*, prompt.headroom_violation, 	okenizer.fallback, etc.).

## Prerequisites

- Prometheus scraping Lidarr's plugin metrics endpoint, for example:

`yaml
scrape_configs:
  - job_name: 'brainarr'
    metrics_path: /importlist/brainarr?action=metrics/prometheus
    static_configs:
      - targets: ['<lidarr-host>:<port>']
`

- Brainarr must have produced at least one run in the selected time range; metrics are event-driven.

## Importing

1. In Grafana choose **Dashboards → Import**.
2. Upload grafana-brainarr-observability.json for the full view, or grafana-brainarr-srebar.json for the SRE summary bar.
3. Select the Prometheus datasource that scrapes Lidarr (/metrics/prometheus).
4. Panels rely on the following labels:
   - model – planner model keys (e.g., ollama:qwen2.5).
   - provider – upstream provider IDs for latency/error series.
   - cache – fixed to prompt_plan for cache metrics.

## Dashboard variables

- latency_series matches time series such as provider_latency_*_p95.
- rrors_series covers provider_errors.* counters.
- 	hrottle_series covers provider_429.* counters.

Use the variable dropdowns to focus on a provider/model family.

## Updating & publishing

Run the docs guard before committing dashboard edits so README/doc references stay truthful:

`pwsh
pwsh ./scripts/check-docs-consistency.ps1
`

If you add metrics or change labels, update the README troubleshooting section and docs/METRICS_REFERENCE.md accordingly.

## Troubleshooting

- Empty panels usually mean no recent Brainarr runs—expand the time range or trigger a manual run.
- No data at all indicates the scrape path or Lidarr URL is incorrect.
- High 429 counts? Enable provider throttling or reduce per-model concurrency.

Optional alert examples live in dashboards/alerts-brainarr.yaml; tailor thresholds to your environment.
