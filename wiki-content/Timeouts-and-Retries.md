
<!-- SYNCED_WIKI_PAGE: Do not edit in the GitHub Wiki UI. This page is synced from wiki-content/ in the repository. -->
> Source of truth lives in README.md and docs/. Make changes via PRs to the repo; CI auto-publishes to the Wiki.

# Timeouts & Retries (summary)

This page summarizes the default timeouts and retries for Brainarr and is kept in sync with the repository README and code defaults.

- Canonical docs: ../README.md#reliability--timeouts
- Configuration details: ../docs/configuration.md

## Global

- Test timeout: 10s (connection/model checks)
- Retries: 3 attempts with backoff
- Circuit breaker: opens for 1 minute on sustained failures (half-open requires 3 successes)
- Hard guardrail: 120s maximum per request

## Provider call timeouts (defaults)

- Ollama: 60s
- LM Studio: 60s
- OpenAI: 30s
- Anthropic: 30s
- Gemini: 30s
- Perplexity: 25s
- Groq: 20s
- OpenRouter (gateway): 45s

Notes

- Values are configurable per provider in Advanced Settings. Defaults are rooted in code (Constants.cs, AdvancedProviderSettings).
