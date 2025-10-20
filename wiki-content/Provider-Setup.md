
<!-- SYNCED_WIKI_PAGE: Do not edit in the GitHub Wiki UI. This page is synced from wiki-content/ in the repository. -->
> Source of truth lives in README.md and docs/. Make changes via PRs to the repo; CI auto-publishes to the Wiki.

# Provider Setup Hub

> Canonical provider data lives in `docs/providers.yaml`. After editing status or defaults, run `pwsh ./scripts/sync-provider-matrix.ps1` to regenerate the README, docs, and wiki fragments.

## 1. Pick your starting point

- Review the generated matrix in [README ▸ Provider status](../README.md#provider-status) or `docs/PROVIDER_MATRIX.md` for verification state and notes.
- Use [Provider Basics](Provider-Basics) for quick comparisons (privacy, cost, speed).

## 2. Follow the dedicated guides

- **Local-first setup:** [Local Providers](Local-Providers) covers Ollama, LM Studio, hardware tips, and smoke tests.
- **Cloud & gateway setup:** [Cloud Providers](Cloud-Providers) walks through API key creation, rate limits, and model selection for OpenAI, Anthropic, Gemini, DeepSeek, Groq, Perplexity, and OpenRouter.
- **Fallback chains & failover:** See [Cloud Providers ▸ Multi-Provider Strategy](Cloud-Providers#multi-provider-strategy) for configuring priority lists and automatic failover.

## 3. Validate the configuration

- Run the [First Run Guide](First-Run-Guide) to test connectivity, review the first recommendation batch, and tune discovery settings.
- If borderline items need manual approval, enable Queue Borderline Items and follow the [Review Queue](Review-Queue) workflow.

## 4. Maintain accuracy over time

- Record verified provider runs in `docs/VERIFICATION-RESULTS.md` when you complete smoke tests.
- Update `docs/providers.yaml` (or its verification notes) whenever provider status changes, then rerun the sync script so all surfaces stay in sync.
- Monitor usage with the Observability preview and `dashboards/README.md` (see [Troubleshooting](Troubleshooting) for pointers).

Need more depth? Jump into the provider-specific deep dives linked above; this page intentionally stays a short index so the real walkthroughs live in one place.
