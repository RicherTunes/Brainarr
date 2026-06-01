# Environment Variables

Brainarr recognises several environment variables for advanced configuration. Most users never need to set these — they are power-user and development overrides.

Set variables in the Lidarr Docker container environment, the systemd unit file, or the system environment before launching Lidarr.

## Model registry

| Variable | Purpose | Default |
| --- | --- | --- |
| `BRAINARR_USE_EXTERNAL_MODEL_REGISTRY` | Set to `true` to enable the external model registry. | *(unset — registry disabled)* |
| `BRAINARR_MODEL_REGISTRY_URL` | Override the model registry JSON URL. | Built-in URL from the model registry service. |
| `BRAINARR_REGISTRY_SHARED_CACHE` | Set to `true` to enable shared caching for model registry downloads. | *(unset — no shared cache)* |
| `BRAINARR_REGISTRY_SHARED_CACHE_TTL_SECONDS` | TTL for shared registry cache entries. | Built-in default. |

## Styles catalog

| Variable | Purpose | Default |
| --- | --- | --- |
| `BRAINARR_DISABLE_STYLES_REMOTE` | Set to `true` to disable remote styles-catalog fetches (fully offline). | *(unset — remote enabled)* |
| `BRAINARR_STYLES_CATALOG_URL` | Override the styles catalog JSON URL. | Built-in URL from the style catalog service. |
| `BRAINARR_STYLES_CATALOG_REF` | Override the git ref (branch/tag) for the styles catalog. | Built-in default. |

## Library analysis

| Variable | Purpose | Default |
| --- | --- | --- |
| `BRAINARR_STYLE_CONTEXT_PARALLEL` | Set to `true` to enable parallel style-context computation during library analysis. | *(unset — sequential)* |
| `BRAINARR_STYLE_CONTEXT_PARALLEL_THRESHOLD` | Minimum library size before parallel computation activates. | Built-in default. |
| `BRAINARR_STYLE_CONTEXT_PARALLEL_MAXDOP` | Max degree of parallelism for style-context computation. | Built-in default. |

## Provider overrides

| Variable | Purpose | Default |
| --- | --- | --- |
| `BRAINARR_ZAI_CODING_USER_AGENT` | Override the User-Agent header for Z.AI Coding provider requests. | Built-in default. |
| `BRAINARR_PREFS_DIR` | Override the directory for provider format-preference cache files. | Plugin config directory. |

## Testing

| Variable | Purpose | Default |
| --- | --- | --- |
| `BRAINARR_HEAVY_TESTS` | Set to `true` in the test environment to enable heavy (OOM-risk) test variants. | *(unset — heavy tests skip)* |
