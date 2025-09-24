# Brainarr AI Provider Guide

## Overview

Brainarr supports 9 different AI providers, from completely free local options to premium cloud services. This guide helps you choose the right provider for your needs.

For a concise status view including defaults and current testing status, see: docs/PROVIDER_SUPPORT_MATRIX.md

> Compatibility
> Requires Lidarr 2.14.2.4786+ on the plugins/nightly branch. In Lidarr: Settings > General > Updates > set Branch = nightly. Older versions will not load Brainarr.

## Quick Comparison Table

| Provider | Type | Cost | Speed | Quality | Privacy | Best For |
|----------|------|------|-------|---------|---------|----------|
| **Ollama** | Local | Free | Fast | Good | 100% Private | Privacy-focused users |
| **LM Studio** | Local | Free | Fast | Good | 100% Private | GUI preference |
| **OpenRouter** | Gateway | Pay-per-use | Varies | Excellent | Cloud | Access to 200+ models |
| **DeepSeek** | Cloud | $0.14/M tokens | Fast | Excellent | Cloud | Cost-effective quality |
| **Gemini** | Cloud | Free tier + paid | Fast | Good | Cloud | Starting out |
| **Groq** | Cloud | Pay-per-use | Ultra-fast | Good | Cloud | Speed priority |
| **Perplexity** | Cloud | $5-20/month (Pro includes $5/mo API credit) | Fast | Excellent | Cloud | Web-enhanced results |
| **OpenAI** | Cloud | $20/month+ | Fast | Excellent | Cloud | GPT-4 quality |
| **Anthropic** | Cloud | Pay-per-use | Fast | Best | Cloud | Claude's reasoning |

## Detailed Provider Information

### Local Providers (100% Private)

#### Ollama

- **Setup**: `curl -fsSL https://ollama.com/install.sh | sh`
- **Models**: `ollama pull qwen2.5` (recommended)
- **Cost**: Completely free
- **RAM Required**: 8GB minimum, 16GB recommended
- **Pros**: Total privacy, no API limits, fast
- **Cons**: Requires local resources
- **Recommended Models**: qwen2.5, llama3.2, mistral
- **Last Verified**: Pending (1.2.4)

**Quick Test**

```bash
curl -s http://localhost:11434/api/tags | jq -r '.models[].name'
```

#### LM Studio

- **Setup**: Download from [lmstudio.ai](https://lmstudio.ai)
- **Cost**: Completely free
- **RAM Required**: 8GB minimum, 16GB recommended
- **Pros**: User-friendly GUI, model marketplace
- **Cons**: Manual model management
- **Recommended Models**: Qwen 3 (tested), Llama 3 8B, Qwen 2.5, Mistral 7B (GGUF)
- **Last Verified**: 2025-09-13 (1.2.4)
- **Tested Configuration**: Qwen 3 at ~40–50k tokens (shared GPU + CPU) on NVIDIA RTX 3090

**Quick Test**

```bash
curl -s http://localhost:1234/v1/models | jq
```

### Gateway Provider

#### OpenRouter

- **API Key**: Get at [openrouter.ai/keys](https://openrouter.ai/keys)
- **Cost**: Pay-per-use, varies by model
- **Pricing Examples**:
  - Claude Sonnet 4: $0.30/M input, $1.50/M output
  - GPT-4.1 Mini: $0.30/M input, $1.50/M output
  - Gemini 2.5 Flash: $0.20/M input, $0.60/M output
  - DeepSeek V3: $0.14/M tokens (blended)
- **Pros**: Access to 200+ models with one API key
- **Cons**: Can get expensive with heavy use
- **Best For**: Testing different models
- **Recommended Models**: `anthropic/claude-sonnet-4-20250514` (UI: `ClaudeSonnet4`), `openai/gpt-4.1-mini` (UI: `GPT41_Mini`), `google/gemini-2.5-flash` (UI: `Gemini25_Flash`), `meta-llama/llama-3.3-70b-versatile` (UI: `Llama33_70B`)
- **Last Verified**: Pending (1.2.4)

**Quick Test**

```bash
curl -s https://openrouter.ai/api/v1/models \
  -H "Authorization: Bearer YOUR_OPENROUTER_API_KEY" | jq '.data[0].id'
```

Troubleshooting
- Invalid API key: ensure it starts with `sk-or-` and is active
- 402 payment required: add credit or resolve billing at https://openrouter.ai/settings/billing
- 429 rate limit: wait 1–5 minutes, reduce frequency, enable caching
- See also: docs/TROUBLESHOOTING.md#openrouter

### Budget Cloud Providers

#### DeepSeek

- **API Key**: Get at [platform.deepseek.com](https://platform.deepseek.com)
- **Cost**: $0.14 per million tokens (cache miss), $0.014/M (cache hit)
- **Monthly Estimate**: ~$0.50-2.00 for typical use
- **Pros**: Incredible value, V3 matches GPT-4 quality
- **Cons**: Chinese company (privacy considerations)
- **Models**: `deepseek-chat` (UI: `DeepSeek_Chat`), `deepseek-reasoner` (UI: `DeepSeek_Reasoner`), `deepseek-r1` (UI: `DeepSeek_R1`)
- **Note**: DeepSeek V3 released Jan 2025 with major performance improvements
- **Recommended Models**: `DeepSeek_Chat` ▸ `deepseek-chat`; optional: `DeepSeek_Reasoner` ▸ `deepseek-reasoner`
- **Last Verified**: Pending (1.2.4)

**Quick Test**

```bash
curl -s https://api.deepseek.com/v1/models \
  -H "Authorization: Bearer YOUR_DEEPSEEK_API_KEY" | jq '.data[0].id'
```

#### Google Gemini

- **API Key**: FREE at [aistudio.google.com/apikey](https://aistudio.google.com/apikey)
- **Free Tier**:
  - 15 requests/minute
  - 1 million tokens/minute
  - 1,500 requests/day
- **Paid**: $7/M input, $21/M output (Pro model)
- **Pros**: Generous free tier, 1M+ context window
- **Cons**: Rate limits on free tier
- **Models**: gemini-2.5-flash (fast), gemini-2.5-pro (premium), gemini-2.0-flash (balanced)
- **Recommended Models**: `Gemini_25_Flash` ▸ `gemini-2.5-flash`; optional: `Gemini_25_Pro` ▸ `gemini-2.5-pro`, `Gemini_20_Flash` ▸ `gemini-2.0-flash`
- **Last Verified**: 2025-09-13 (1.2.4)

Important:
- The key’s Google Cloud project must have the Generative Language API enabled. If it isn’t, Google returns `403 PERMISSION_DENIED` with reason `SERVICE_DISABLED` and an activation URL.
- Fix: open the activation URL shown in logs, or go to `https://console.developers.google.com/apis/api/generativelanguage.googleapis.com/overview?project=YOUR_PROJECT_NUMBER` and click Enable. Wait 1–5 minutes, then retry. If you don’t control the project, use an AI Studio key instead.

Enable via gcloud (optional):
```bash
gcloud services enable generativelanguage.googleapis.com \
  --project YOUR_PROJECT_ID
```

Generate an API key (AI Studio):
- Go to https://aistudio.google.com/apikey and sign in to a Google account.
- Click Create API key (or Get API key), then copy the key (often starts with `AIza`).
- Treat it as a secret; paste into Brainarr under Provider: Google Gemini.
- Click Test in Brainarr to verify connectivity.

Sanity check with curl:
```bash
curl -s "https://generativelanguage.googleapis.com/v1beta/models?key=YOUR_GEMINI_API_KEY" | jq '.models[0].name'
```

Notes:
- Some managed/workspace Google accounts restrict this API; if you see an access restriction, try a personal account or ask an admin to allow the service.
- Keys created in Google Cloud Console must have the Generative Language API enabled on the project to avoid `SERVICE_DISABLED`.

**Quick Test**

```bash
curl -s "https://generativelanguage.googleapis.com/v1beta/models?key=YOUR_GEMINI_API_KEY" \
  | jq '.models[0].name'
```

#### Groq

- **API Key**: Get at [console.groq.com](https://console.groq.com)
- **Cost**:
  - Llama 3.3 70B: $0.59/M input, $0.79/M output
  - Mixtral 8x7B: $0.24/M tokens
- **Pros**: 10x faster inference than competitors
- **Cons**: Limited model selection
- **Best For**: When speed is critical
- **Models**: `llama-3.3-70b-versatile` (UI: `Llama33_70B_Versatile`), `llama-3.3-70b-specdec` (UI: `Llama33_70B_SpecDec`), `deepseek-r1-distill-llama-70b` (UI: `DeepSeek_R1_Distill_L70B`), `llama-3.1-8b-instant` (UI: `Llama31_8B_Instant`)
- **Recommended Models**: `Llama33_70B_Versatile` ▸ `llama-3.3-70b-versatile`; optional: `Llama31_8B_Instant` ▸ `llama-3.1-8b-instant`
- **Last Verified**: Pending (1.2.4)

**Quick Test**

```bash
curl -s https://api.groq.com/openai/v1/models \
  -H "Authorization: Bearer YOUR_GROQ_API_KEY" | jq '.data[0].id'
```

### Premium Cloud Providers

#### Perplexity

- **API Key**: Get at [perplexity.ai/settings/api](https://perplexity.ai/settings/api)
- **Cost**:
  - API: $5/month for 20M tokens
  - Pro subscription includes API access
- **Models**:
  - `sonar-pro` (UI: `Sonar_Pro`): Best for music discovery (web search)
  - `sonar-reasoning-pro` (UI: `Sonar_Reasoning_Pro`): Deep research mode
  - `sonar-reasoning` (UI: `Sonar_Reasoning`): Balanced reasoning + speed
  - `sonar` (UI: `Sonar`): Fastest, lowest cost
  - Offline instruct: llama-3.1-70b-instruct, llama-3.1-8b-instruct, mixtral-8x7b-instruct
- **Pros**: Real-time web search integrated
- **Cons**: Higher cost for heavy use
- **Recommended Models**: `Sonar_Pro` ▸ `sonar-pro`; optional: `Sonar_Reasoning_Pro` ▸ `sonar-reasoning-pro`
- **Last Verified**: 2025-09-13 (1.2.4)
  - Note: Perplexity Pro subscribers receive $5/month in API credits that can be used with Brainarr.

**Quick Test**

```bash
curl -s https://api.perplexity.ai/models \
  -H "Authorization: Bearer YOUR_PERPLEXITY_API_KEY" | jq '.data[0].id'
```

#### OpenAI

- **API Key**: Get at [platform.openai.com](https://platform.openai.com)
- **Cost** (as of Sep 2025):
  - GPT-4.1: $2.50/M input, $10/M output
  - GPT-4.1-mini: $0.25/M input, $1.25/M output
  - GPT-4o: $2.50/M input, $10/M output
  - GPT-4o-mini: $0.15/M input, $0.60/M output
  - o4-mini: $0.05/M input, $0.20/M output (budget reasoning)
- **Monthly Estimate**: $5-20 typical use
- **Pros**: Industry standard, reliable, extensive ecosystem
- **Cons**: Can get expensive with heavy use
- **Models**: `gpt-4.1` (`GPT41`), `gpt-4.1-mini` (`GPT41_Mini`), `gpt-4o` (`GPT4o`), `gpt-4o-mini` (`GPT4o_Mini`), `o4-mini` (`O4_Mini`)
- **Recommended Models**: `GPT41_Mini` ▸ `gpt-4.1-mini`; optional: `GPT4o` ▸ `gpt-4o`, `GPT4o_Mini` ▸ `gpt-4o-mini`
- **Last Verified**: Pending (1.2.4)

**Quick Test**

```bash
curl -s https://api.openai.com/v1/models \
  -H "Authorization: Bearer YOUR_OPENAI_API_KEY" | jq '.data[0].id'
```

#### Anthropic (Claude)

- **API Key**: Get at [console.anthropic.com](https://console.anthropic.com)
- **Cost** (as of Sep 2025):
  - Claude Sonnet 4: $3/M input, $15/M output
  - Claude 3.7 Sonnet: $3/M input, $15/M output
  - Claude 3.5 Haiku: $0.80/M input, $4/M output
  - Claude 3 Opus: $15/M input, $75/M output
- **Pros**: Superior reasoning, analysis, and code understanding
- **Cons**: Premium pricing for premium quality
- **Models**: `claude-sonnet-4-20250514` (`ClaudeSonnet4`), `claude-3-7-sonnet-20250219` (`Claude37_Sonnet`), `claude-3-5-haiku-20241022` (`Claude35_Haiku`), `claude-3-opus-latest` (`Claude3_Opus`)
- **Recommended Models**: `ClaudeSonnet4` ▸ `claude-sonnet-4-20250514`; optional: `Claude37_Sonnet` ▸ `claude-3-7-sonnet-20250219`, `Claude35_Haiku` ▸ `claude-3-5-haiku-20241022`
- **Last Verified**: Pending (1.2.4)

**Quick Test**

```bash
curl -s https://api.anthropic.com/v1/models \
  -H "x-api-key: YOUR_ANTHROPIC_API_KEY" \
  -H "anthropic-version: 2023-06-01" | jq '.data[0].id'
```

Troubleshooting
- Invalid key/auth error: recreate key at https://console.anthropic.com and ensure API access
- Credit/limit errors: add payment method or reduce usage
- 429 rate limit: wait a minute, or switch to Haiku for lower cost
- See also: docs/TROUBLESHOOTING.md#anthropic

- **Note**: Claude 3.5 Sonnet often preferred for complex recommendations

## Cost Estimation

### Typical Usage Patterns

- **Light** (5-10 recommendations/week): ~50K tokens/month
- **Medium** (20-30 recommendations/week): ~200K tokens/month
- **Heavy** (50+ recommendations/week): ~500K tokens/month

### Monthly Cost by Provider (Medium Usage ~200K tokens)

- **Ollama/LM Studio**: $0 (local)
- **DeepSeek V3**: $0.03
- **Gemini Flash**: $0 (free tier)
- **Groq (Llama 3.3)**: $0.15
- **OpenRouter** (varies): $0.20-5.00
- **GPT-4o-mini**: $0.20
- **Perplexity**: $5 (subscription)
- **GPT-4o**: $2.50
- **Claude 3.5 Haiku**: $1.00
- **Claude 3.5 Sonnet**: $3.60

## Setup Recommendations

### For Privacy-Focused Users

1. **Primary**: Ollama with qwen2.5
2. **Fallback**: None (keep everything local)

### For Best Value

1. **Primary**: DeepSeek V3
2. **Fallback**: Gemini Flash (free tier)

### For Best Quality

1. **Primary**: Claude 3.5 Sonnet (via OpenRouter)
2. **Fallback**: GPT-4o

### For Beginners

1. **Primary**: Gemini (free tier)
2. **Fallback**: DeepSeek

### For Testing

1. **Primary**: OpenRouter (access all models)
2. **Switch models as needed**

## Provider-Specific Tips

### Ollama Tips

- Install multiple models: `ollama pull qwen2.5 && ollama pull llama3.2`
- Keep models updated: `ollama pull qwen2.5:latest`
- Check loaded models: `ollama list`

### OpenRouter Tips

- Set spending limits in dashboard
- Use cheaper models for testing
- Monitor usage at openrouter.ai/activity

### DeepSeek Tips

- Use deepseek-chat for best results
- Enable caching for repeated queries
- Monitor token usage in dashboard

### Gemini Tips

- Stay within free tier limits
- Use Flash model for speed
- Upgrade to Pro for complex analysis

## Troubleshooting

### Common Issues

**"No models found"**

- Ollama: Run `ollama pull qwen2.5` first
- LM Studio: Load a model in the UI

**"Rate limit exceeded"**

- Add delays between requests
- Upgrade to paid tier
- Use fallback provider

**"Connection refused"**

- Check provider URL
- Verify API key is valid
- Check firewall settings

## Security Notes

- **API Keys**: Never commit to version control
- **Local Models**: No data leaves your machine
- **Cloud Providers**: Data is processed on their servers
- **OpenRouter**: Acts as proxy, sees your requests

## Performance Optimization

### Speed Priority

1. Groq (ultra-fast)
2. Gemini Flash
3. Local providers (Ollama/LM Studio)

### Quality Priority

1. Claude 3.5 Sonnet
2. GPT-4o
3. DeepSeek V3

### Cost Priority

1. Ollama/LM Studio (free)
2. Gemini (free tier)
3. DeepSeek ($0.14/M)

## Model Selection Guide

### By Music Knowledge

- **Best**: Perplexity Sonar (web-enhanced)
- **Good**: Claude, GPT-4o
- **Adequate**: DeepSeek, Gemini, Llama

### By Response Speed

- **Fastest**: Groq, Gemini Flash
- **Fast**: Local models, DeepSeek
- **Standard**: OpenAI, Anthropic

### By Context Length

- **Largest**: Gemini (1M+ tokens)
- **Large**: Claude (200K tokens)
- **Standard**: Most others (32-128K)

## FAQ

**Q: Which provider should I start with?**
A: Try Ollama for privacy or Gemini for free cloud access.

**Q: How much will it cost per month?**
A: $0 for local, $0-5 for budget cloud, $10-50 for premium.

**Q: Can I use multiple providers?**
A: Yes! Configure fallback chains for reliability.

**Q: Which gives the best recommendations?**
A: Claude and GPT-4o, but DeepSeek V3 is nearly as good at 1/10th the cost.

**Q: Is my data safe?**
A: 100% with local providers, varies with cloud (check their privacy policies).
