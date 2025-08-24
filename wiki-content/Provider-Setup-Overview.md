# Provider Setup Overview

Choose and configure the right AI provider for your needs. This guide helps you select from 8 supported providers based on your priorities.

## Quick Provider Selector

### I want maximum privacy 🔒
→ **[Ollama](Local-Providers#ollama)** or **[LM Studio](Local-Providers#lm-studio)**
- 100% local processing
- No data ever leaves your network
- Free to use

### I want to start free 🆓
→ **[Google Gemini](Cloud-Providers#google-gemini)**
- Generous free tier (1,500 requests/day)
- High-quality recommendations
- Easy setup

### I want the lowest cost 💰
→ **[DeepSeek](Cloud-Providers#deepseek)**
- 10-20x cheaper than GPT-4
- Excellent quality
- $0.50-2.00/month typical usage

### I want the fastest responses ⚡
→ **[Groq](Cloud-Providers#groq)**
- 10x faster than competitors
- 500+ tokens/second
- Very affordable

### I want the highest quality 🎯
→ **[OpenAI GPT-4](Cloud-Providers#openai)** or **[Anthropic Claude](Cloud-Providers#anthropic)**
- Industry-leading recommendation quality
- Advanced reasoning capabilities
- Premium pricing

### I want to try everything 🧪
→ **[OpenRouter](Cloud-Providers#openrouter)**
- Access to 200+ models with one API key
- Test different providers easily
- Pay-per-use model

## Complete Provider Comparison

| Provider | Type | Cost | Speed | Quality | Privacy | Setup | Best For |
|----------|------|------|-------|---------|---------|--------|----------|
| **🏠 Ollama** | Local | Free | Fast | Good | 💚 Perfect | Easy | Privacy-conscious users |
| **🖥️ LM Studio** | Local | Free | Fast | Good | 💚 Perfect | Easy | GUI lovers who want privacy |
| **🆓 Gemini** | Cloud | Free/Low | Fast | Good | 🟡 Cloud | Easy | Getting started |
| **💰 DeepSeek** | Cloud | Ultra-low | Fast | Excellent | 🟡 Cloud | Easy | Budget-conscious |
| **⚡ Groq** | Cloud | Low | Ultra-fast | Good | 🟡 Cloud | Easy | Speed priority |
| **🌐 OpenRouter** | Gateway | Variable | Variable | Excellent | 🟡 Cloud | Easy | Model experimentation |
| **🔍 Perplexity** | Cloud | Medium | Fast | Excellent | 🟡 Cloud | Easy | Web-enhanced results |
| **🤖 OpenAI** | Cloud | Medium | Fast | Excellent | 🟡 Cloud | Easy | GPT-4 quality |
| **🧠 Anthropic** | Cloud | High | Fast | Best | 🟡 Cloud | Easy | Reasoning tasks |

## Cost Analysis

### Free Options
1. **Ollama** - Completely free (uses your hardware)
2. **LM Studio** - Completely free (uses your hardware)
3. **Google Gemini** - 1,500 requests/day free tier

### Budget Options (Monthly estimates)
1. **DeepSeek** - $0.50-2.00/month for typical usage
2. **Groq** - $1-5/month depending on model
3. **Gemini Paid** - $2-8/month for moderate usage

### Premium Options (Monthly estimates)
1. **OpenRouter** - $5-20/month depending on models used
2. **Perplexity** - $5-20/month (includes web search)
3. **OpenAI** - $10-50/month for GPT-4 usage
4. **Anthropic** - $15-60/month for Claude usage

## Hardware Requirements

### Local Providers
| Provider | RAM Required | Storage | GPU | CPU |
|----------|--------------|---------|-----|-----|
| **Ollama** | 8GB min, 16GB rec | 4-20GB per model | Optional | 4+ cores |
| **LM Studio** | 8GB min, 16GB rec | 4-20GB per model | Optional | 4+ cores |

### Cloud Providers
- **RAM**: Minimal (just Lidarr requirements)
- **Storage**: Minimal (no model storage needed)
- **Network**: Stable internet connection required

## Provider-Specific Setup Guides

### 🏠 Local Providers (100% Private)
- **[Ollama Setup](Local-Providers#ollama)** - Command-line local AI
- **[LM Studio Setup](Local-Providers#lm-studio)** - GUI-based local AI

### ☁️ Cloud Providers
- **[Google Gemini](Cloud-Providers#google-gemini)** - Free tier, high quality
- **[DeepSeek](Cloud-Providers#deepseek)** - Ultra cost-effective
- **[Groq](Cloud-Providers#groq)** - Ultra-fast inference
- **[OpenRouter](Cloud-Providers#openrouter)** - Access to 200+ models
- **[Perplexity](Cloud-Providers#perplexity)** - Web-enhanced responses
- **[OpenAI](Cloud-Providers#openai)** - GPT-4 quality
- **[Anthropic](Cloud-Providers#anthropic)** - Claude reasoning

## Privacy Considerations

### 🟢 Perfect Privacy (Local Providers)
- **Ollama** and **LM Studio**
- Your library data never leaves your server
- No external API calls
- Complete control over data

### 🟡 Cloud Privacy Considerations
- Library metadata sent to provider for analysis
- Most providers don't store conversation data
- API keys should be kept secure
- Consider using local providers for sensitive libraries

## Performance Comparison

### Speed Rankings (Tokens per second)
1. **Groq** - 500+ tokens/second
2. **DeepSeek** - 100+ tokens/second  
3. **Gemini** - 50+ tokens/second
4. **Local Providers** - 20-100 tokens/second (hardware dependent)
5. **OpenAI/Anthropic** - 20-50 tokens/second

### Quality Rankings (Recommendation accuracy)
1. **Anthropic Claude** - Best reasoning and understanding
2. **OpenAI GPT-4** - Excellent across all categories
3. **DeepSeek V3** - Surprisingly good for the cost
4. **Gemini Pro** - Strong performance, especially with context
5. **Local Models** - Good, depends on specific model chosen

## Configuration Examples

### Privacy-First Configuration
```yaml
Provider: Ollama
URL: http://localhost:11434
Model: llama3.2:latest
Discovery Mode: Adjacent
Max Recommendations: 10
```

### Cost-Optimized Configuration
```yaml
Provider: DeepSeek
API Key: [your-key]
Model: deepseek-chat
Discovery Mode: Similar
Max Recommendations: 5
Cache Duration: 240 minutes
```

### Quality-Focused Configuration
```yaml
Provider: OpenAI
API Key: [your-key]
Model: gpt-4o
Discovery Mode: Exploratory
Max Recommendations: 15
Cache Duration: 60 minutes
```

### Speed-Optimized Configuration
```yaml
Provider: Groq
API Key: [your-key]
Model: llama-3.3-70b-versatile
Discovery Mode: Adjacent
Max Recommendations: 20
Cache Duration: 30 minutes
```

## Provider Migration

### Switching Providers
1. **Backup current settings**
2. **Configure new provider**
3. **Test connection**
4. **Compare recommendation quality**
5. **Update configuration**

### Multi-Provider Setup
While Brainarr supports one provider at a time, you can:
1. Create multiple Brainarr import lists
2. Configure each with different providers
3. Compare results
4. Keep the best performing one

## Troubleshooting Provider Issues

### Connection Problems
1. **Check provider status** (for cloud providers)
2. **Verify API key format**
3. **Test network connectivity**
4. **Check firewall settings**

### Poor Recommendation Quality
1. **Try different discovery modes**
2. **Increase recommendation count**
3. **Switch to higher-quality provider**
4. **Check library analysis settings**

### High Costs
1. **Switch to cheaper provider** (DeepSeek, Gemini)
2. **Use local providers** (Ollama, LM Studio)
3. **Increase cache duration**
4. **Reduce recommendation frequency**
5. **Lower max recommendations count**

## Provider-Specific Features

### Advanced Features by Provider
| Feature | Ollama | LM Studio | Gemini | DeepSeek | Groq | OpenRouter | Perplexity | OpenAI | Anthropic |
|---------|---------|-----------|---------|----------|-------|------------|------------|---------|-----------|
| **Auto Model Detection** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| **Multiple Models** | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Long Context** | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| **Web Search** | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ | ❌ |
| **Custom Endpoints** | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |

## Next Steps

1. **Choose your provider** based on the comparison above
2. **Follow the specific setup guide** for your chosen provider
3. **Configure basic settings** in [Basic Configuration](Basic-Configuration)
4. **Test your setup** with [Getting Your First Recommendations](Getting-Your-First-Recommendations)

## Need Help?

- **[Provider Troubleshooting](Provider-Troubleshooting)** - Provider-specific issues
- **[Common Issues](Common-Issues)** - General problems and solutions
- **[FAQ](FAQ)** - Frequently asked questions about providers

**Ready to set up your provider?** Choose from the guides above and get started!