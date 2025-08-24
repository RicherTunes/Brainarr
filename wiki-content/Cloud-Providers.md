# Cloud Providers - Powerful AI in the Cloud

Complete setup guides for cloud-based AI providers offering advanced capabilities and models.

## Why Choose Cloud Providers?

### ✅ Powerful Models
- **Latest AI technology** - Access to cutting-edge models
- **Large context windows** - Better understanding of complex libraries
- **Specialized capabilities** - Web search, reasoning, code understanding

### ✅ No Hardware Requirements
- **Minimal local resources** - Just need internet connection
- **No model downloads** - Instant access to models
- **Automatic updates** - Always latest model versions

### ✅ Scalability
- **Handle any library size** - Process thousands of artists efficiently
- **Fast processing** - Dedicated server infrastructure
- **Multiple models** - Switch between models easily

## Provider Comparison

| Provider | Best For | Cost | Quality | Speed | Free Tier |
|----------|----------|------|---------|-------|-----------|
| **🆓 Gemini** | Getting started | Free/Low | Good | Fast | ✅ 1,500 req/day |
| **💰 DeepSeek** | Cost-effective | Ultra-low | Excellent | Fast | ✅ Free credits |
| **⚡ Groq** | Speed priority | Low | Good | Ultra-fast | ✅ Limited free |
| **🌐 OpenRouter** | Model variety | Variable | Excellent | Variable | ❌ Pay-per-use |
| **🔍 Perplexity** | Web-enhanced | Medium | Excellent | Fast | ❌ Subscription |
| **🤖 OpenAI** | Premium quality | Medium | Excellent | Fast | ❌ Pay-per-use |
| **🧠 Anthropic** | Best reasoning | High | Best | Fast | ❌ Pay-per-use |

---

## 🆓 Google Gemini - Free Tier Available

Perfect for getting started with AI recommendations at no cost.

### Why Choose Gemini?
- **Generous free tier** - 1,500 requests/day, 1M tokens/minute
- **Long context** - 1M+ tokens (can analyze very large libraries)
- **High quality** - Competitive with GPT-4 for many tasks
- **Easy setup** - Simple API key registration

### Getting Started

#### Step 1: Get API Key (FREE)
1. Visit [aistudio.google.com/apikey](https://aistudio.google.com/apikey)
2. Sign in with Google account
3. Click "Get API Key" → "Create API Key"
4. Copy the key (starts with `AIza...`)

#### Step 2: Configure in Brainarr
1. **Provider**: 🆓 Google Gemini (Free Tier)
2. **API Key**: Paste your key
3. **Model**: Choose from:
   - `gemini-1.5-flash` - Fastest, most efficient
   - `gemini-1.5-pro` - Best quality, 2M context window
4. **Click Test** - Should return "Connection successful"
5. **Save**

#### Configuration Example
```yaml
Provider: Gemini
API Key: AIzaXXXXXXXXXXXXXXXXXXXXXXXXX
Model: gemini-1.5-flash
Discovery Mode: Adjacent
Max Recommendations: 10
Cache Duration: 120 minutes
```

### Free Tier Limits
- **15 requests per minute**
- **1 million tokens per minute**  
- **1,500 requests per day**
- **Rate limiting recommended** to stay within limits

### Cost (Paid Tier)
- **Flash**: $0.075 per 1M input tokens, $0.30 per 1M output tokens
- **Pro**: $1.25 per 1M input tokens, $5.00 per 1M output tokens

**Monthly estimate**: $1-5 for typical usage on paid tier

---

## 💰 DeepSeek - Ultra Cost-Effective

Best value for money with quality rivaling GPT-4.

### Why Choose DeepSeek?
- **Incredible value** - 10-20x cheaper than GPT-4
- **High quality** - DeepSeek V3 matches GPT-4 in many benchmarks
- **Fast processing** - Optimized for efficiency
- **Generous free credits** - Often includes free trial credits

### Getting Started

#### Step 1: Get API Key
1. Visit [platform.deepseek.com](https://platform.deepseek.com)
2. Sign up (often includes $5-10 free credits)
3. Go to API Keys section
4. Create new API key
5. Copy the key (starts with `sk-...`)

#### Step 2: Configure in Brainarr
1. **Provider**: 💰 DeepSeek (Ultra Cheap)
2. **API Key**: Paste your key
3. **Model**: Choose from:
   - `deepseek-chat` - Latest V3 model (recommended)
   - `deepseek-coder` - If you want technical reasoning
4. **Click Test** - Should return "Connection successful"
5. **Save**

#### Configuration Example
```yaml
Provider: DeepSeek
API Key: sk-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
Model: deepseek-chat
Discovery Mode: Exploratory
Max Recommendations: 15
Cache Duration: 180 minutes
Rate Limit: 10 requests/minute
```

### Pricing
- **Chat Model**: $0.14 per 1M tokens (cache miss), $0.014 per 1M tokens (cache hit)
- **Context**: 64K tokens
- **Rate limits**: 30 requests/minute

**Monthly estimate**: $0.50-2.00 for typical usage

### Performance Tips
- Enable caching for 90% cost reduction
- DeepSeek V3 (released Jan 2025) significantly improved quality
- Great for exploratory discovery mode

---

## ⚡ Groq - Ultra-Fast Inference

When speed is your priority, Groq delivers 10x faster responses.

### Why Choose Groq?
- **Incredible speed** - 500+ tokens/second (vs 20-50 for others)
- **Low latency** - Near-instant responses
- **Affordable pricing** - Very competitive rates
- **Good quality** - Strong performance with fast models

### Getting Started

#### Step 1: Get API Key
1. Visit [console.groq.com](https://console.groq.com)
2. Sign up (free tier available)
3. Go to API Keys
4. Create new API key
5. Copy the key

#### Step 2: Configure in Brainarr
1. **Provider**: ⚡ Groq (Ultra Fast)
2. **API Key**: Paste your key
3. **Model**: Choose from:
   - `llama-3.3-70b-versatile` - Best quality/speed balance (recommended)
   - `mixtral-8x7b-32768` - Very fast, good quality
   - `llama-3.1-70b-versatile` - Highest quality
4. **Click Test**
5. **Save**

#### Configuration Example
```yaml
Provider: Groq
API Key: gsk_XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
Model: llama-3.3-70b-versatile
Discovery Mode: Adjacent
Max Recommendations: 20
Cache Duration: 60 minutes
Rate Limit: 30 requests/minute
```

### Pricing (Examples)
- **Llama 3.3 70B**: $0.59/M input, $0.79/M output tokens
- **Mixtral 8x7B**: $0.24/M tokens
- **Llama 3.1 8B**: $0.05/M input, $0.08/M output tokens

**Monthly estimate**: $1-5 for typical usage

### Speed Comparison
- **Groq**: 500+ tokens/second
- **Others**: 20-50 tokens/second
- **10-25x faster** response times

---

## 🌐 OpenRouter - Access to 200+ Models

One API key for all the best models from different providers.

### Why Choose OpenRouter?
- **Model variety** - 200+ models with one API key
- **Easy comparison** - Test different models easily
- **Flexible pricing** - Pay only for what you use
- **Latest models** - Access to newest releases immediately

### Getting Started

#### Step 1: Get API Key
1. Visit [openrouter.ai/keys](https://openrouter.ai/keys)
2. Sign up and add payment method
3. Create API key
4. Add initial credits ($5-10 recommended)

#### Step 2: Configure in Brainarr
1. **Provider**: 🌐 OpenRouter (200+ Models)
2. **API Key**: Paste your key
3. **Model**: Choose from popular options:
   - `anthropic/claude-3-5-haiku` - Fast, efficient
   - `openai/gpt-4o-mini` - Balanced quality/cost
   - `deepseek/deepseek-chat` - Ultra cost-effective
   - `anthropic/claude-3-5-sonnet` - Highest quality
4. **Click Test**
5. **Save**

#### Configuration Example
```yaml
Provider: OpenRouter
API Key: sk-or-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
Model: anthropic/claude-3-5-haiku
Discovery Mode: Exploratory
Max Recommendations: 12
Cache Duration: 120 minutes
Rate Limit: 15 requests/minute
```

### Popular Model Recommendations

#### Budget Options
- `deepseek/deepseek-chat` - $0.14/M tokens
- `google/gemini-flash-1.5` - $0.075/M input tokens
- `meta-llama/llama-3.1-8b-instruct` - $0.18/M tokens

#### Balanced Options
- `anthropic/claude-3-5-haiku` - $0.25/M input, $1.25/M output
- `openai/gpt-4o-mini` - $0.15/M input, $0.60/M output

#### Premium Options
- `anthropic/claude-3-5-sonnet` - $3/M input, $15/M output
- `openai/gpt-4o` - $2.50/M input, $10/M output

**Monthly estimate**: $5-20 depending on model choice

---

## 🔍 Perplexity - Web-Enhanced AI

AI with real-time web search capabilities for discovering trending music.

### Why Choose Perplexity?
- **Web search integration** - Finds latest music trends and releases
- **Real-time data** - Access to current music information
- **High quality** - Combines multiple model capabilities
- **Music discovery focus** - Excellent for finding new and trending artists

### Getting Started

#### Step 1: Get API Access
1. Visit [perplexity.ai/settings/api](https://perplexity.ai/settings/api)
2. Subscribe to Pro plan ($20/month) or API plan ($5/month)
3. Generate API key
4. Copy the key

#### Step 2: Configure in Brainarr
1. **Provider**: 🔍 Perplexity (Web Search)
2. **API Key**: Paste your key
3. **Model**: Choose from:
   - `llama-3.1-sonar-small-128k-online` - Fast web search
   - `llama-3.1-sonar-large-128k-online` - Best quality web search
   - `llama-3.1-sonar-huge-128k-online` - Premium quality
4. **Click Test**
5. **Save**

#### Configuration Example
```yaml
Provider: Perplexity
API Key: pplx-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
Model: llama-3.1-sonar-large-128k-online
Discovery Mode: Exploratory
Max Recommendations: 10
Cache Duration: 240 minutes  # Cache longer due to subscription cost
```

### Pricing Options
- **API Plan**: $5/month + $0.20/1K requests
- **Pro Plan**: $20/month (includes API access)
- **Web searches**: Higher cost but more current information

**Monthly estimate**: $5-20 depending on usage

---

## 🤖 OpenAI - Industry Standard

The gold standard for AI quality with GPT-4 models.

### Why Choose OpenAI?
- **Industry leading quality** - GPT-4 sets the standard
- **Consistent performance** - Reliable, well-tested models
- **Regular updates** - Continuous model improvements
- **Broad compatibility** - Well-supported across applications

### Getting Started

#### Step 1: Get API Key
1. Visit [platform.openai.com](https://platform.openai.com)
2. Sign up and add payment method
3. Go to API Keys section
4. Create new secret key
5. Copy the key (starts with `sk-...`)

#### Step 2: Configure in Brainarr
1. **Provider**: 🤖 OpenAI (GPT-4)
2. **API Key**: Paste your key
3. **Model**: Choose from:
   - `gpt-4o-mini` - Cost-effective, good quality
   - `gpt-4o` - Best balance of quality/speed
   - `gpt-4-turbo` - Highest quality (legacy)
   - `o1-preview` - Best reasoning (expensive)
4. **Click Test**
5. **Save**

#### Configuration Example
```yaml
Provider: OpenAI
API Key: sk-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
Model: gpt-4o
Discovery Mode: Adjacent
Max Recommendations: 12
Cache Duration: 90 minutes
Rate Limit: 10 requests/minute
```

### Pricing (as of Jan 2025)
- **GPT-4o Mini**: $0.15/M input, $0.60/M output tokens
- **GPT-4o**: $2.50/M input, $10/M output tokens
- **o1-preview**: $15/M input, $60/M output tokens

**Monthly estimate**: $10-50 depending on model and usage

---

## 🧠 Anthropic - Best Reasoning

Claude models excel at understanding context and providing thoughtful recommendations.

### Why Choose Anthropic?
- **Superior reasoning** - Best at understanding musical relationships
- **Safety focused** - Reliable, well-aligned responses
- **Long context** - 200K tokens (can analyze massive libraries)
- **Constitutional AI** - More thoughtful and nuanced recommendations

### Getting Started

#### Step 1: Get API Key
1. Visit [console.anthropic.com](https://console.anthropic.com)
2. Sign up and add payment method
3. Go to API Keys
4. Create new API key
5. Copy the key

#### Step 2: Configure in Brainarr
1. **Provider**: 🧠 Anthropic (Claude)
2. **API Key**: Paste your key
3. **Model**: Choose from:
   - `claude-3-5-haiku` - Fastest, most cost-effective
   - `claude-3-5-sonnet` - Best balance (recommended)
   - `claude-3-opus` - Highest quality (expensive)
4. **Click Test**
5. **Save**

#### Configuration Example
```yaml
Provider: Anthropic
API Key: sk-ant-XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX
Model: claude-3-5-sonnet-20241022
Discovery Mode: Exploratory
Max Recommendations: 15
Cache Duration: 120 minutes
Rate Limit: 5 requests/minute
```

### Pricing
- **Claude 3.5 Haiku**: $0.25/M input, $1.25/M output tokens
- **Claude 3.5 Sonnet**: $3/M input, $15/M output tokens
- **Claude 3 Opus**: $15/M input, $75/M output tokens

**Monthly estimate**: $15-60 depending on model and usage

---

## Cost Optimization Strategies

### 1. Enable Aggressive Caching
```yaml
Cache Duration: 240-480 minutes (4-8 hours)
Enable Caching: Yes
```

### 2. Use Rate Limiting
```yaml
Rate Limiting: Enabled
Requests per Minute: 5-10
```

### 3. Choose Cost-Effective Models
1. **DeepSeek**: Ultra-low cost, high quality
2. **Gemini**: Free tier for testing
3. **Groq**: Fast processing = lower costs
4. **OpenRouter**: Compare model costs easily

### 4. Optimize Request Frequency
```yaml
Fetch Interval: Weekly instead of daily
Max Recommendations: 5-10 instead of 20+
Library Sampling: Minimal for large libraries
```

### 5. Monitor Usage
- Enable token usage logging
- Set up billing alerts with providers
- Review costs monthly and adjust accordingly

## Security Best Practices

### API Key Security
1. **Never commit API keys** to version control
2. **Rotate keys regularly** (monthly recommended)
3. **Use environment variables** for key storage
4. **Set usage limits** in provider dashboards
5. **Monitor for unusual usage** patterns

### Network Security
1. **Use HTTPS only** - All providers use secure endpoints
2. **Consider VPN** for additional privacy
3. **Firewall rules** if running on servers
4. **Regular security updates** for all components

## Troubleshooting Cloud Providers

### API Key Issues
1. **Format validation**: Check key starts correctly (sk-, AIza-, etc.)
2. **Billing setup**: Most providers require payment method
3. **Usage limits**: Check if you've exceeded quotas
4. **Key permissions**: Ensure key has required scopes

### Rate Limiting
1. **Respect provider limits** - Don't exceed documented rates
2. **Implement backoff** - Brainarr handles this automatically
3. **Distribute load** - Use caching to reduce requests
4. **Monitor quotas** - Check usage in provider dashboards

### Quality Issues
1. **Try different models** within same provider
2. **Adjust discovery mode** - More/less conservative
3. **Increase context** with comprehensive library sampling
4. **Compare providers** - Each has different strengths

## Next Steps

After setting up your cloud provider:

1. **[Basic Configuration](Basic-Configuration)** - Configure Brainarr settings
2. **[Cost Optimization](Cost-Optimization)** - Minimize expenses
3. **[Performance Tuning](Performance-Tuning)** - Optimize for your needs
4. **[Getting Your First Recommendations](Getting-Your-First-Recommendations)** - Test your setup

## Need Help?

- **[Provider Troubleshooting](Provider-Troubleshooting#cloud-providers)** - Cloud-specific issues
- **[Common Issues](Common-Issues)** - General problems
- **[Cost Optimization](Cost-Optimization)** - Reduce expenses

**Ready to harness the power of cloud AI for your music discovery!**