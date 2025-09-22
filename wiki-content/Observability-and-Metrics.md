# Observability & Metrics (Preview)

Brainarr exposes lightweight, model‑aware metrics to help you understand latency, error rates, and throttling across providers and models. This preview is designed for maintainers and power users; it lives under Advanced settings and can be disabled with a single flag.

## Where to find it (UI)

- Lidarr → Import Lists → Brainarr → Advanced → "Observability (Preview)"
- The field shows top `provider:model` series for the last 15 minutes (count, p95, errors, 429).
- Click the Help link to open a compact HTML preview (table) that’s easy to scan.

> Screenshot placeholder (add your image): `docs/assets/observability-preview.png`

## Actions (internal)

These are the lightweight endpoints the UI calls. You can invoke them directly via the plugin action endpoint if you’re debugging.

- `observability/get` — JSON summary for last 15 minutes
- `observability/getoptions` — Options for the TagSelect preview
- `observability/html` — Compact HTML table (preview)
- `metrics/prometheus` — Prometheus‑formatted plain text

## What’s collected

- Latency histograms per `{provider}:{model}` — p50 / p95 / p99, average, counts
- Error counters per `{provider}:{model}`
- Throttle (HTTP 429) counters per `{provider}:{model}`

## Interpreting Metrics

- p50 vs p95/p99: p50 is median (typical), p95/p99 highlight tail latency. Rising p95 with flat p50 usually means intermittent slowness or backpressure.
- Errors vs Throttles: Errors indicate request failures (4xx/5xx/timeouts). Throttles (429) mean the upstream rate‑limited us; reduce concurrency or enable adaptive throttling.
- Series Cardinality: Metrics are keyed per provider+model. Prefer a small set of models per provider to keep dashboards readable.

## Tuning Playbooks

- High p95 (>2× baseline) with few errors:
  - Reduce per‑model concurrency caps (Advanced → hidden) temporarily.
  - If cloud provider: verify region/model availability; try a faster route (e.g., Groq llamas for speed, OpenAI mini for cost/speed).
- Frequent 429 spikes:
  - Enable `EnableAdaptiveThrottling`, start with CloudCap=2, LocalCap=8, TTL=60s.
  - If persistent: lower `MaxConcurrentPerModelCloud` to 1–2 for the affected model.
- Error clusters on one model:
  - Switch model/route (Provider Settings), or fall back to a more stable model.
  - Check structured‑JSON toggle for gateways that don’t support schema well.

## 429 Troubleshooting

- Causes: Provider QPS caps, shared tenancy, temporary bursts.
- Quick mitigations: Reduce concurrency caps; enable adaptive throttling; spread load across models/providers.
- Safe defaults: CloudCap=2, LocalCap=8, TTL=60; increase gradually when 429s subside.

## Provider Notes

- OpenAI: `gpt-4.1-mini` (UI: `GPT41_Mini`) is a good latency/cost default.
- Groq: very fast on Llama; watch 429 during peaks.
- Perplexity: ‘online’ sonar models may vary with live search; consider structured JSON off if schema errors occur.
- OpenRouter: be explicit with vendor/model (e.g., `anthropic/claude-3.5-sonnet`).

## Prometheus How‑To

1. Scrape endpoint: call `metrics/prometheus` via the plugin action endpoint.
1. Minimal scrape config (example):

```yaml
scrape_configs:
  - job_name: 'brainarr'
    metrics_path: /importlist/brainarr?action=metrics/prometheus
    static_configs:
      - targets: ['localhost:8686']  # replace with your Lidarr host:port
```

1. Grafana starter panel (import as JSON, edit metric names if needed):

```json
// Or import the full dashboard JSON:
// docs/assets/grafana-brainarr-observability.json
{
  "title": "Brainarr p95 latency (example panel)",
  "type": "timeseries",
  "targets": [
    { "expr": "{__name__=~\"provider_latency_.*_p95\"}" }
  ]
}
```

See also: `dashboards/README.md` for more queries and tips.

## FAQ

- The TagSelect is empty: No recent data; trigger a fetch or wait a few minutes.
- HTML preview is empty: Same as above, or preview disabled via flag.
- Update frequency: Metrics are event‑driven; summaries query last 15 minutes on demand.
- Hide preview pre‑release: Set `EnableObservabilityPreview=false`.

## Privacy & Cost

- No request/response bodies are exported by Observability; only aggregate timings and counters.
- Structured JSON may reduce parsing errors but can affect cost/latency depending on provider support.

## Test Recipes

- Simulate load: increase `MaxRecommendations`, enable refinement, run multiple fetches; observe p95 uptick.
- Simulate 429 handling: raise concurrency caps aggressively on a single model; verify 429 counters and adaptive cap decay.
- Prometheus smoke test: curl the `metrics/prometheus` action and check for metric lines.

## Common Baselines (initial guidance)

- Premium (OpenAI/Anthropic): p95 under 2–4s; errors < 2%; 429 < 1%.
- Fast (Groq): p95 sub‑second to ~1.5s; errors < 2%; 429 occasional during peaks.
- Local (Ollama/LM Studio): larger variance; tune caps to avoid CPU saturation; p95 depends on model size.

## Adaptive throttling (hidden, off by default)

When enabled, Brainarr temporarily reduces per‑model concurrency after HTTP 429 responses and gradually restores it over the throttle window.

- Flags (Advanced → Hidden):
  - `EnableAdaptiveThrottling` (default: false)
  - `AdaptiveThrottleSeconds` (default: 60)
  - `AdaptiveThrottleCloudCap` (default: 2 if unset)
  - `AdaptiveThrottleLocalCap` (default: 8 if unset)

## Per‑model concurrency (hidden)

Set overall caps without code changes:

- `MaxConcurrentPerModelCloud`
- `MaxConcurrentPerModelLocal`

## Disable the preview quickly

- Set `EnableObservabilityPreview = false` to disable the preview UI and its actions without redeploying.

## Implementation pointers

- Model identity: `Services/Core/ModelKeys.cs`
- Limiters (+ adaptive): `Services/Resilience/LimiterRegistry.cs`
- HTTP resilience (429 hook): `Resilience/ResiliencePolicy.cs`
- Observability actions: `Services/Core/BrainarrOrchestrator.cs`
- Prometheus export: `Services/Telemetry/MetricsCollector.cs`
