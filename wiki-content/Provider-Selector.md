
<!-- SYNCED_WIKI_PAGE: Do not edit in the GitHub Wiki UI. This page is synced from wiki-content/ in the repository. -->
> Source of truth lives in README.md and docs/. Make changes via PRs to the repo; CI auto-publishes to the Wiki.

# Provider Selector

> Use this quick decision tree to choose a starting provider. Each outcome links to the detailed setup guides.

```text
Start
 ├── Need to stay 100% offline or avoid API keys?
 │     ├── Yes → [Ollama](Local-Providers#🦙-ollama-most-popular-local)
 │     └── Prefer a GUI for local models → [LM Studio](Local-Providers#🎬-lm-studio-gui-based-local)
 ├── Already have Claude Code or OpenAI Codex CLI installed?
 │     ├── Claude Code → [Claude Code Subscription](../docs/configuration.md#claude-code-subscription)
 │     └── OpenAI Codex → [OpenAI Codex Subscription](../docs/configuration.md#openai-codex-subscription)
 ├── Looking for the lowest cloud cost?
 │     └── [DeepSeek](Cloud-Providers#🧠-deepseek-ultra-low-cost-leader) (add Gemini Flash as fallback)
 ├── Need generous free tier and speed?
 │     └── [Gemini Flash](Cloud-Providers#✨-google-gemini-speed--free-tier-champion)
 ├── Want access to many premium models with one key?
 │     └── [OpenRouter](Cloud-Providers#openrouter)
 ├── Highest-quality reasoning (budget OK)?
 │     └── [Anthropic Claude 3.5](Cloud-Providers#anthropic)
 └── Fastest responses possible?
       └── [Groq Llama 3.x](Cloud-Providers#🚀-groq-lightning-fast-inference)
```

## Recommended fallback chains

| Goal | Primary | Fallback(s) |
|------|---------|-------------|
| Stay offline | Ollama | LM Studio |
| Budget cloud | DeepSeek | Gemini Flash → OpenRouter (GPT-4.1 Mini) |
| Premium quality | Claude 3.5 Sonnet (via OpenRouter) | GPT-4o → DeepSeek Chat |
| Speed | Groq Llama 3.2 | OpenRouter (Gemini Flash) → Local Ollama |

After choosing a starting point, follow the [Provider Setup Hub](Provider-Setup.md) and then run through the [Operations Playbook](Operations.md) to validate.
