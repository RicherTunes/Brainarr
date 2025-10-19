# Troubleshooting

Use this playbook when Brainarr results look off. Every section links back to the underlying explanation so you can keep README, docs, and wiki aligned.

## Provider connection test fails or times out

### Symptoms

- The `Test` button in settings spins for a while and returns `false` or shows a timeout.
- Logs show `Provider connection test failed`.

### Steps

- Verify the base URL for local providers:
  - Ollama: `http://localhost:11434`
  - LM Studio: `http://localhost:1234`
- For cloud providers, confirm the API key is present and correctly scoped. Re‑click `Test` after any change.
- Brainarr executes provider tests on a dedicated thread with a strict timeout to avoid UI stalls. If tests still time out, check host firewalls and provider dashboards for rate‑limits.
- If a provider is down, temporarily switch back to a local provider to isolate the issue.

## Prompt was trimmed or recommendations look sparse

### Symptoms

- Runs log `prompt.headroom_violation` or show “prompt was trimmed for headroom.”
- Recommendations cluster around a handful of styles even though you expect more variety.

### Likely causes

- The active model has a small context window (common with lightweight Ollama models).
- Too many styles are allowed during sampling, exhausting the headroom budget.
- Brainarr fell back to the basic tokenizer and overestimated or underestimated tokens.

### Fixes

- Switch to a model with a larger context window or raise the headroom reserve in the planner settings (see [planner & cache](./planner-and-cache.md)).
- Reduce the relaxed matching inflation or tighten style filters in the configuration panel.
- Add a dedicated tokenizer for the model so estimates stay within ±5%; follow [tokenization & estimates](./tokenization-and-estimates.md).
- Re-run the import list after adjustments and confirm `prompt.tokens_post` stabilises in logs or metrics.

## Cloud provider JSON errors

### Symptoms

- Logs show JSON parsing failures (`Invalid JSON`, `Schema validation failed`) after a cloud provider call.
- The provider matrix lists the provider as experimental and failover does not recover quickly.

### Steps

- Confirm you are using a supported model ID; provider adapters expect the defaults defined in `BrainarrConstants` unless you override them via the UI.
- Re-run the `Test` button after entering API keys to catch schema or auth regressions early.
- If errors persist, switch back to the local provider to isolate whether the issue is upstream. Local runs share the same planner and cache, so successful local runs implicate the remote provider.
- Update provider notes in `docs/providers.yaml` only after confirming the root cause and regenerating the matrix with `pwsh ./scripts/sync-provider-matrix.ps1`.

## Cache confusion

### Symptoms

- “Old” recommendations reappear after you change styles or providers.
- Logs do not show new planner activity even though you changed configuration.

### Steps

- Remember the plan cache uses a five-minute sliding TTL by default. Wait for the TTL to expire or click `Disable` then `Enable` on the import list to flush the cache immediately.
- Check `System → Logs` for `Plan cache configured` entries; if you changed capacity/TTL and do not see the message, save the settings again.
- If recommendations still look stale, open the Brainarr settings panel and tweak Plan Cache Capacity by ±1 to force a reconfiguration. The planner will emit new metrics and rebuild the cache.

## Logs & metrics

- Use `tokenizer.fallback` to spot models that still rely on the estimator. Each warning fires once per key but the metric keeps counting for dashboards.
- Track `prompt.plan_cache_hit`, `prompt.plan_cache_miss`, `prompt.plan_cache_evict`, and `prompt.plan_cache_size` to monitor cache health.
- `prompt.tokens_pre` and `prompt.tokens_post` help you verify whether compression and headroom are behaving as expected.
- Export metrics via Lidarr’s `/metrics/prometheus` endpoint and annotate dashboards with the planner build version (`ConfigVersion`) after upgrades.
- Re-import the updated Grafana dashboards in `docs/assets/` so the new metric names (`prompt.plan_cache_*`, `tokenizer.fallback`) render correctly.

Need more? Read [tokenization & estimates](./tokenization-and-estimates.md) for tokenizer drift handling and [planner & cache](./planner-and-cache.md) for version salt details.

## Styles catalog refresh issues

### Symptoms

- Styles matching seems incomplete or aliases don’t resolve as expected.

### Notes

- Brainarr embeds a curated styles catalog and periodically refreshes from a canonical JSON in this repository. If the remote request fails or returns 304 (ETag unchanged), the embedded catalog remains authoritative and recommendations continue to work.
- Network fetches use a short timeout and back off between refresh attempts. You can continue using Brainarr without an internet connection.
