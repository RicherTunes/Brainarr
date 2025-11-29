# Configuration

Brainarr keeps a local-first default and only sends prompts to the provider that you actively select. Use this page to understand the defaults you get on a fresh install and where to turn when you need deeper control.

## At a glance

- The `AI Provider` defaults to Ollama with `http://localhost:11434` and the `qwen2.5:latest` model. No cloud provider is contacted unless you switch providers and supply API keys.
- Recommendation sizing defaults to 20 results per run with `Discovery Mode = Adjacent` and `Sampling Strategy = Balanced` so Brainarr surfaces library-adjacent artists first.
- Planner cache settings start at 256 entries and a five-minute sliding TTL; they can be tuned for very large libraries but usually stay at the defaults.
- Iterative refinement is enabled for local providers to backfill sparse results automatically. Disable it only if you need one-pass recommendations.
- Token estimation uses the model registry plus the tokenizer registry. When a tokenizer is missing you will see a one-time fallback warning—follow the [tokenization and estimates guide](./tokenization-and-estimates.md) to add accurate tokenizers.

## Settings reference

| Setting | Default | Notes |
| --- | --- | --- |
| AI Provider | `Ollama` | Local-first default; switching to a cloud provider is opt-in. |
| Configuration URL | `http://localhost:11434` (Ollama) / `http://localhost:1234` (LM Studio) | Only editable for local providers; cloud providers display `N/A`. |
| Model Selection | `qwen2.5:latest` (Ollama) / `local-model` (LM Studio) | Click `Test` to auto-detect models before saving custom values. |
| Auto Detect Model | `true` | Keeps the model list fresh by querying the provider during tests. |
| Enable Iterative Refinement | `true` for local providers | Turns on the backfill planner that tops up sparse runs. |
| Max Recommendations | `20` (min `1`, max `50`) | Defines the target album count per run. |
| Discovery Mode | `Adjacent` | Keeps results anchored to nearby library styles. Options: `Similar`, `Adjacent`, `Exploratory`. |
| Sampling Strategy | `Balanced` | Evenly splits samples between artists and global albums. Options: `Minimal`, `Balanced`, `Comprehensive`. |
| Recommendation Mode | `SpecificAlbums` | Output focus. Options: `SpecificAlbums`, `ArtistsOnly`, `Artists`, `Mixed`. |
| Backfill Strategy | `Standard` | Controls iterative refinement intensity. Options: `Off`, `Conservative`, `Standard`, `Balanced`, `Aggressive`. |
| Plan Cache Capacity | `256` (clamped 16–1024) | Controls how many prompt plans remain warm; see [planner and cache](./planner-and-cache.md). |
| Plan Cache TTL (minutes) | `5` (clamped 1–60) | Sliding expiry refreshed on cache hits. |
| Prefer Minimal Prompt Formatting | `false` | Enable when a provider requires ASCII-only prompts or strict JSON. |

## Provider selection

1. Leave the default `Ollama` provider to keep Brainarr entirely local. Ensure the Ollama service and the `qwen2.5:latest` model are available.
2. Switch to `LM Studio` if you prefer its local runtime; the defaults use `http://localhost:1234` with the sentinel `local-model` value.
3. Choose a cloud provider only after you have credentials. Each provider reveals an API key field, optional base URL overrides, and any extra knobs defined in `BrainarrSettings`.
4. Use the `Test` button after every change; the orchestrator validates connectivity, model detection, and authentication before saving.

## Planner and cache

Planner controls live under the `Cache` and advanced sections of the settings panel. The defaults mirror the values in `CacheSettings` (256 entries, five-minute TTL). The planner logs `Plan cache configured` with `capacity` and `ttl_minutes` fields whenever you adjust these values. Refer to the [planner and cache guide](./planner-and-cache.md) for version salt details, cache invalidation rules, and glossary terms like *headroom* and *relaxed matching*.

## Tokenization and estimates

Token estimation combines the model registry with the tokenizer registry. When a tokenizer is missing Brainarr logs a single warning per model key and emits `tokenizer.fallback`. Follow the [tokenization and estimates guide](./tokenization-and-estimates.md) to register accurate tokenizers, understand drift expectations, and interpret the related metrics.

## Cloud providers

Enabling a cloud provider replaces the local URL/model fields with API key prompts. Every provider inherits the same guardrails:

- Brainarr never auto-enables cloud providers; you must select the provider and supply credentials.
- API keys stay in Lidarr''s secret store and are never logged. Validation runs through the orchestrator''s provider-specific smoke tests.
- Cloud models inherit the same headroom budgeting and cache behaviour as local runs. Adjust the planner headroom only if the provider exposes unusually small context windows.
- Track usage with the metrics endpoint (`tokenizer.fallback`, `prompt.actual_tokens`, etc.) so you can monitor drift and rate limits over time.

Keep this page aligned with the UI by re-running `pwsh ./scripts/sync-provider-matrix.ps1` and `bash ./scripts/check-docs-consistency.sh` whenever you change providers or defaults.

## Timeout settings

Brainarr validates timeout values to ensure they fall within safe operational bounds:

| Setting | Min | Default | Max | Notes |
| --- | --- | --- | --- | --- |
| AI Request Timeout | 5s | 30s | 600s | User-configurable timeout for AI provider calls |
| Local Provider Timeout | - | 360s | - | Auto-applied when local providers use default timeout |
| Test Connection Timeout | - | 10s | - | Fixed timeout for connectivity tests |
| Model Detection Timeout | - | 10s | - | Fixed timeout for model auto-detection |

**Automatic timeout elevation**: When using local providers (Ollama or LM Studio) with a timeout at or below the default (30s), Brainarr automatically elevates the effective timeout to 360 seconds. This accommodates the slower first-token latency typical of local models running on consumer hardware.

## Styles catalog

Brainarr ships with an embedded music styles catalog used for normalization and matching (e.g., mapping "Prog Rock" to "progressive-rock"). The catalog provides:

- **Style normalization**: Maps user-friendly names to canonical slugs (e.g., "Prog Rock" → "progressive-rock")
- **Similarity matching**: Groups related styles for better recommendation diversity
- **Style filtering**: Optionally filter recommendations by preferred styles

Periodically, the plugin refreshes this catalog from a canonical JSON hosted in this repository. If the remote fetch is unavailable, the embedded catalog remains authoritative; functionality does not depend on network access.

## Circuit breaker

The circuit breaker protects against cascading failures when providers experience issues:

| Setting | Default | Notes |
| --- | --- | --- |
| Failure Rate Threshold | 50% | Opens circuit when failure rate exceeds this |
| Sampling Window | 20 requests | Window size for failure rate calculation |
| Minimum Throughput | 10 requests | Minimum requests before evaluating failure rate |
| Break Duration | 30s | How long to wait before testing recovery |

When the circuit opens, subsequent requests fail fast until the provider recovers.

## Anthropic extended thinking

When using Anthropic (Claude) models, you can enable extended thinking for deeper reasoning:

| Setting | Default | Notes |
| --- | --- | --- |
| Thinking Mode | `Off` | Controls Claude extended thinking. Options: `Off`, `Auto`, `On`. |
| Thinking Budget Tokens | `0` | Token budget for thinking. Leave `0` to let Anthropic decide. Typical range: 2000–8000. |

**Auto mode**: When enabled, Brainarr adds thinking support for direct Anthropic calls. For OpenRouter with Anthropic routes, it automatically switches to the `:thinking` model variant.

## Advanced settings

These settings appear under the **Advanced** section in Lidarr's settings panel.

### Manual model override

| Setting | Notes |
| --- | --- |
| Manual Model ID | Exact API model ID to use (e.g., `gpt-4.1-mini`, `claude-sonnet-4-20250514`). Overrides the dropdown selection when set. |

### Sampling shape

Fine-tune how Brainarr samples your library for prompt building. These control the distribution of artist/album candidates drawn from top matches vs. recent additions.

| Setting | Default | Notes |
| --- | --- | --- |
| Artist Similar/Adjacent/Exploratory Top % | varies | Percentage drawn from top matches for each discovery mode. |
| Artist Similar/Adjacent/Exploratory Recent % | varies | Percentage drawn from recent additions for each discovery mode. |
| Album Similar/Adjacent/Exploratory Top % | varies | Same as above, for album sampling. |
| Album Similar/Adjacent/Exploratory Recent % | varies | Same as above, for album sampling. |
| Minimum Albums per Artist | `0` | Floor for albums per artist after compression. |
| Relaxed Match Inflation | `1.0` | Maximum multiplier when relaxed style matching expands pools (range: 1.0–5.0). |

### Recommendation modes explained

| Mode | Behavior |
| --- | --- |
| `SpecificAlbums` | Recommends specific albums with full metadata (artist, album, year). |
| `ArtistsOnly` | Returns only artist names; Lidarr imports all albums for each artist. |
| `Artists` | Similar to ArtistsOnly but with different prompt framing. |
| `Mixed` | Combines album-specific and artist recommendations in the same run. |

### Discovery modes explained

| Mode | Behavior |
| --- | --- |
| `Similar` | Recommends music very close to your existing library style. |
| `Adjacent` | Default. Explores nearby genres while staying anchored to your collection. |
| `Exploratory` | Ventures further afield for diverse discoveries. |

### Backfill strategies explained

| Strategy | Behavior |
| --- | --- |
| `Off` | Single-pass only; no iterative refinement. |
| `Conservative` | Minimal top-up attempts if results fall short. |
| `Standard` | Default. Balanced refinement for most libraries. |
| `Balanced` | More aggressive than Standard; good for sparse results. |
| `Aggressive` | Maximum refinement attempts; may increase API calls. |
