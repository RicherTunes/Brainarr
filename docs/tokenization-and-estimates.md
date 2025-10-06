# Tokenization & estimates

Brainarr relies on token estimates to size prompts, enforce headroom, and surface cost signals. The token lifecycle combines the model registry (context limits and metadata) with the tokenizer registry (how to count tokens for a specific model).

## Registry workflow

1. When the planner builds a prompt, it asks `ModelTokenizerRegistry` for a tokenizer keyed by `provider:model`.
2. If an exact match is registered the tokenizer is returned immediately.
3. If no exact match exists, the registry tries a provider-only fallback (for example `openai`).
4. If neither key is known, the registry uses the `BasicTokenizer`, logs a warning once per model key, and emits the `tokenizer.fallback` metric with `model` and `reason` tags.
5. The planner records `prompt.tokens_pre`, applies compression, then records `prompt.tokens_post` and `prompt.compression_ratio` before honouring the headroom guard.

Because fallback warnings are cached in-memory, you will see at most one log entry per missing key until Lidarr restarts. The metric still increments so dashboards can show ongoing drift.

## Adding explicit tokenizers

Tokenizers live in `Brainarr.Plugin/Services/Tokenization`. To register an implementation:

- Create a new class that implements `ITokenizer` for the model or provider you want to support.
- Update `ModelTokenizerRegistry` (same directory) to seed the `overrides` dictionary with your tokenizer keyed by the provider or model name. The unit tests in `Brainarr.Tests/Services/Tokenization/ModelTokenizerRegistryTests.cs` illustrate the expected behaviour.
- If your tokenizer depends on additional registry metadata, extend the model registry loader under `Brainarr.Plugin/Services/Registry/ModelRegistryLoader.cs` so the information is available at runtime.
- Run `dotnet test` after adding the tokenizer to confirm coverage and re-run the docs guardrails before submitting a PR.

## Reading the metrics

| Metric | What it means | When to investigate |
| --- | --- | --- |
| `prompt.tokens_pre` | Baseline token estimate before compression. | Spikes usually mean the planner sampled more artists/albums than expected. |
| `prompt.tokens_post` | Token count after compression and sanitisation. | If this grows near the context limit, consider increasing headroom or using a bigger model. |
| `prompt.compression_ratio` | `tokens_post / tokens_pre`. | Ratios >1 imply the provider expanded the prompt; ratios <0.7 suggest compression is aggressively trimming styles. |
| `prompt.headroom_violation` | Planner had to trim content to stay under the model limit. | Tune the sampling shape or raise the headroom reserve for the model. |
| `tokenizer.fallback` | Brainarr used the basic tokenizer (±20% drift) for the model. | Add a proper tokenizer or adjust budgets to tolerate the drift. |

Export these metrics via Lidarr’s `/metrics/prometheus` endpoint and alert when drift persists—fallback estimates can skew planner headroom and cause unnecessary trimming.

## Drift expectations

The basic tokenizer follows a simple regex and intentionally reports a ±20% drift window. When you see repeated fallback metrics:

- Add a tokenizer or align the model name with an existing key.
- Cross-check the model''s documented context limit in the registry (`docs/models.map.json`) so the planner budgets correctly.
- Review the logs tagged with `tokenizer.fallback` for the exact key (model vs provider) that needs coverage.

Keeping accurate tokenizers tightens headroom calculations, reduces surprise trims, and keeps upgrade notes honest for cloud providers.
