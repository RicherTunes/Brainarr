# Brainarr AI Provider Guide

## Overview
Brainarr supports 8 different AI providers, from completely free local options to premium cloud services. This guide helps you choose the right provider for your needs.

## Quick Comparison Table

| Provider | Type | Cost | Speed | Quality | Privacy | Best For |
|----------|------|------|-------|---------|---------|----------|
| **Ollama** | Local | Free | Fast | Good | 100% Private | Privacy-focused users |
| **LM Studio** | Local | Free | Fast | Good | 100% Private | GUI preference |
| **OpenRouter** | Gateway | Pay-per-use | Varies | Excellent | Cloud | Access to 200+ models |
| **DeepSeek** | Cloud | $0.14/M tokens | Fast | Excellent | Cloud | Cost-effective quality |
| **Gemini** | Cloud | Free tier + paid | Fast | Good | Cloud | Starting out |
| **Groq** | Cloud | Pay-per-use | Ultra-fast | Good | Cloud | Speed priority |
| **Perplexity** | Cloud | $5-20/month | Fast | Excellent | Cloud | Web-enhanced results |
| **OpenAI** | Cloud | $20/month+ | Fast | Excellent | Cloud | GPT-4 quality |
| **Anthropic** | Cloud | Pay-per-use | Fast | Best | Cloud | Claude's reasoning |

## Detailed Provider Information

### 🏠 Local Providers (100% Private)

#### Ollama
- **Setup**: `curl -fsSL https://ollama.com/install.sh | sh`
- **Models**: `ollama pull qwen2.5` (recommended)
- **Cost**: Completely free
- **RAM Required**: 8GB minimum, 16GB recommended
- **Pros**: Total privacy, no API limits, fast
- **Cons**: Requires local resources
- **Best Models**: qwen2.5, llama3.2, mistral

#### LM Studio
- **Setup**: Download from [lmstudio.ai](https://lmstudio.ai)
- **Cost**: Completely free
- **RAM Required**: 8GB minimum, 16GB recommended
- **Pros**: User-friendly GUI, model marketplace
- **Cons**: Manual model management
- **Best Models**: Any GGUF format model

### 🌐 Gateway Provider

#### OpenRouter
- **API Key**: Get at [openrouter.ai/keys](https://openrouter.ai/keys)
- **Cost**: Pay-per-use, varies by model
- **Pricing Examples**:
  - Claude 3.5 Haiku: $0.25/M input, $1.25/M output
  - GPT-4o Mini: $0.15/M input, $0.60/M output
  - DeepSeek V3: $0.14/M tokens (blended)
- **Pros**: Access to 200+ models with one API key
- **Cons**: Can get expensive with heavy use
- **Best For**: Testing different models

### 💰 Budget Cloud Providers

#### DeepSeek
- **API Key**: Get at [platform.deepseek.com](https://platform.deepseek.com)
- **Cost**: $0.14 per million tokens (cache miss), $0.014/M (cache hit)
- **Monthly Estimate**: ~$0.50-2.00 for typical use
- **Pros**: Incredible value, V3 matches GPT-4 quality
- **Cons**: Chinese company (privacy considerations)
- **Models**: deepseek-chat (V3), deepseek-coder
- **Note**: DeepSeek V3 released Jan 2025 with major performance improvements

#### Google Gemini
- **API Key**: FREE at [aistudio.google.com/apikey](https://aistudio.google.com/apikey)
- **Free Tier**: 
  - 15 requests/minute
  - 1 million tokens/minute
  - 1,500 requests/day
- **Paid**: $7/M input, $21/M output (Pro model)
- **Pros**: Generous free tier, 1M+ context window
- **Cons**: Rate limits on free tier
- **Models**: gemini-1.5-flash (fast), gemini-1.5-pro (powerful)

#### Groq
- **API Key**: Get at [console.groq.com](https://console.groq.com)
- **Cost**: 
  - Llama 3.3 70B: $0.59/M input, $0.79/M output
  - Mixtral 8x7B: $0.24/M tokens
- **Pros**: 10x faster inference than competitors
- **Cons**: Limited model selection
- **Best For**: When speed is critical

### 🤖 Premium Cloud Providers

#### Perplexity
- **API Key**: Get at [perplexity.ai/settings/api](https://perplexity.ai/settings/api)
- **Cost**: 
  - API: $5/month for 20M tokens
  - Pro subscription includes API access
- **Models**: 
  - sonar-large: Best for music discovery (web search)
  - sonar-small: Faster, lower cost
- **Pros**: Real-time web search integrated
- **Cons**: Higher cost for heavy use

#### OpenAI
- **API Key**: Get at [platform.openai.com](https://platform.openai.com)
- **Cost** (as of Jan 2025):
  - GPT-4o: $2.50/M input, $10/M output  
  - GPT-4o-mini: $0.15/M input, $0.60/M output
  - GPT-3.5-turbo: $0.50/M input, $1.50/M output
  - o1-preview: $15/M input, $60/M output (reasoning model)
- **Monthly Estimate**: $5-20 typical use
- **Pros**: Industry standard, reliable, extensive ecosystem
- **Cons**: Can get expensive with heavy use

#### Anthropic (Claude)
- **API Key**: Get at [console.anthropic.com](https://console.anthropic.com)
- **Cost** (as of Jan 2025):
  - Claude 3.5 Haiku: $0.80/M input, $4/M output
  - Claude 3.5 Sonnet: $3/M input, $15/M output
  - Claude 3 Opus: $15/M input, $75/M output
- **Pros**: Superior reasoning, analysis, and code understanding
- **Cons**: Premium pricing for premium quality
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