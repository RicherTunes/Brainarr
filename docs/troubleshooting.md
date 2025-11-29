# Troubleshooting

Use this playbook when Brainarr results look off. Every section links back to the underlying explanation so you can keep README, docs, and wiki aligned.

> **See also:** [Common Troubleshooting Guide](https://github.com/RicherTunes/Lidarr.Plugin.Common/blob/main/docs/how-to/TROUBLESHOOTING_COMMON.md) for issues shared across all Lidarr streaming plugins (plugin discovery, authentication patterns, connection issues).

## Quick answers (FAQ)

### Brainarr doesn't show up in Lidarr

- Confirm Lidarr is on the `nightly` branch (Settings → General → Updates).
- If the plugin was copied manually, ensure files live under `plugins/RicherTunes/Brainarr/` (no extra nested folder).
- Restart Lidarr and check **System → Logs** for `Brainarr: minVersion` entries.

### Recommendations are empty or duplicates

- Make sure your library has at least 10 artists/albums.
- Review Safety Gates (Minimum Confidence / Require MBIDs) and the Review Queue.
- Enable iterative refinement for local providers or increase Max Recommendations.

### How do I upgrade safely?

1. Pull the latest tag or release package.
2. Regenerate provider matrices if `docs/providers.yaml` changed (`pwsh ./scripts/sync-provider-matrix.ps1`).
3. Copy new plugin files over the old ones (or reinstall via the UI) and restart Lidarr.
4. Keep the previous ZIP handy; if you need to roll back, restore the prior DLL/manifest pair.

### Where can I ask questions?

- GitHub Issues for bugs/feature requests.
- GitHub Discussions (if enabled) or the Arr community channels for general Q&A.

---

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

- Styles matching seems incomplete or aliases don't resolve as expected.

### Notes

- Brainarr embeds a curated styles catalog and periodically refreshes from a canonical JSON in this repository. If the remote request fails or returns 304 (ETag unchanged), the embedded catalog remains authoritative and recommendations continue to work.
- Network fetches use a short timeout and back off between refresh attempts. You can continue using Brainarr without an internet connection.

---

## Provider-specific errors

### Google Gemini

#### 403 PERMISSION_DENIED / Service disabled {#403-permission_denied-service_disabled}

**Symptoms**: Gemini calls fail with `403 PERMISSION_DENIED` or logs mention "service disabled".

**Causes**:
- The Generative Language API is not enabled in your Google Cloud project.
- Your API key doesn't have permission for the requested model.

**Fixes**:
1. Go to [Google Cloud Console](https://console.cloud.google.com/) → APIs & Services → Enable APIs.
2. Search for "Generative Language API" and enable it.
3. Ensure your API key is associated with the correct project.
4. Test with `Gemini_25_Flash` (the most permissive model tier).

#### General Gemini issues {#google-gemini}

- Gemini models have generous free tiers but require a valid API key from [Google AI Studio](https://aistudio.google.com/).
- Some models (Gemini Pro) may have lower rate limits. Switch to Flash variants if you hit limits.

### OpenAI

#### Invalid API key {#invalid-api-key}

**Symptoms**: Logs show `401 Unauthorized` or "Invalid API key".

**Fixes**:
1. Verify your API key at [platform.openai.com/api-keys](https://platform.openai.com/api-keys).
2. Ensure the key hasn't been revoked or expired.
3. Check that you're pasting the full key (starts with `sk-`).

#### Rate limit exceeded {#rate-limit-exceeded}

**Symptoms**: Logs show `429 Too Many Requests` or rate limit errors.

**Fixes**:
1. Wait a few minutes before retrying.
2. Consider using a model with higher rate limits (GPT41_Mini vs GPT41).
3. Enable caching to reduce API calls.
4. Check your OpenAI usage dashboard for quota status.

### Anthropic

#### Credit limit reached {#credit-limit-reached}

**Symptoms**: Anthropic calls fail with credit/billing errors.

**Fixes**:
1. Check your credit balance at [console.anthropic.com](https://console.anthropic.com/).
2. Add payment method or purchase credits.
3. Use `Claude35_Haiku` for lower-cost operation.

#### General Anthropic issues {#anthropic}

- Anthropic requires pre-paid credits; there's no free tier.
- Enable Thinking Mode only if you need extended reasoning (increases token usage).
- Consider using OpenRouter for Anthropic models if you prefer pay-as-you-go billing.

### OpenRouter

#### General OpenRouter issues {#openrouter}

**Symptoms**: OpenRouter calls fail or return unexpected errors.

**Diagnosis**:
1. Check [OpenRouter status](https://status.openrouter.ai/) for upstream issues.
2. Verify your credit balance at [openrouter.ai/account](https://openrouter.ai/account).
3. Some upstream providers may have temporary outages.

**Fixes**:
- Try switching to `Auto` model to let OpenRouter pick an available route.
- Use a specific model (e.g., `GPT41_Mini`) if `Auto` isn't working.
- Check that your API key has sufficient permissions.

### DeepSeek

- DeepSeek is budget-friendly but may have slower response times.
- If using `DeepSeek_Reasoner` or `DeepSeek_R1`, expect higher latency for complex queries.
- Check [DeepSeek status](https://status.deepseek.com/) for service issues.

### Groq

- Groq offers ultra-fast inference but with strict rate limits.
- If you hit rate limits, switch to a smaller model (`Llama31_8B_Instant`) or reduce request frequency.
- Groq's free tier has generous limits for testing.

### Perplexity

- Perplexity models include web search capabilities, which may affect response times.
- Use `Sonar` for faster responses; `Sonar_Reasoning_Pro` for deeper analysis.
- Check your API usage at [perplexity.ai/settings/api](https://www.perplexity.ai/settings/api).
