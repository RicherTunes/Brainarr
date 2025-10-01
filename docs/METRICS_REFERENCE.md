# Metrics Reference

Brainarr emits structured metrics through the plugin telemetry interface. Use this table to wire dashboards and alerts without inspecting the source code.

| Metric | Description | Tags | Emitted by |
| --- | --- | --- | --- |
| `prompt.plan_cache_hit` | Increments when the prompt planner reuses a cached plan. | `model` (planner budget model key) | `PlanCache`
| `prompt.plan_cache_miss` | Counts cache misses when fetching plans directly from the cache layer. | `cache=prompt_plan` | `PlanCache`
| `prompt.plan_cache_evict` | Increments when an entry expires or is evicted from the LRU. | `cache=prompt_plan` | `PlanCache`
| `prompt.plan_cache_size` | Current number of entries in the plan cache. | `cache=prompt_plan` | `PlanCache`
| `prompt.actual_tokens` | Final prompt token count reported by the tokenizer. | `model` | `LibraryAwarePromptBuilder`
| `prompt.tokens_pre` | Estimated tokens before compression cycles. | `model` | `LibraryAwarePromptBuilder`
| `prompt.tokens_post` | Token count after compression or trimming. | `model` | `LibraryAwarePromptBuilder`
| `prompt.compression_ratio` | Ratio of post-compression tokens to baseline estimate. | `model` | `LibraryAwarePromptBuilder`
| `tokenizer.fallback` | Logged once per model key when falling back to the basic tokenizer. | `model`, `reason` | `ModelTokenizerRegistry`
| `prompt.headroom_violation` | Counts times the headroom guard had to clamp the prompt to stay within the model context. | `model` | `LibraryAwarePromptBuilder`

> **Tip:** Expose the metrics endpoint via `metrics/prometheus` (see the Observability wiki page) and label dashboards with the same tag keys. The CI `Docs Truth Check` job verifies this table stays aligned with the source constants in `MetricsNames.cs`.
