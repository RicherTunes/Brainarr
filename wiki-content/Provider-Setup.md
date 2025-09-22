# ⚙️ Provider Setup - Complete Configuration Guide

Comprehensive setup guide for all 9 AI providers supported by Brainarr. Each provider has been tested and optimized for music discovery.

## 🎯 **Quick Provider Comparison**

| Provider | Setup Time | Cost | Free Tier | Best For |
|----------|------------|------|-----------|----------|
| **Ollama** | 10 min | Free | ✅ Unlimited | Privacy + Control |
| **DeepSeek** | 2 min | Ultra Low | ✅ $5 credit | Budget + Quality |
| **Gemini** | 2 min | Low | ✅ 1500/day | Speed + Free tier |
| **Groq** | 2 min | Low | ✅ 30/min | Ultra-fast inference |
| **LM Studio** | 15 min | Free | ✅ Unlimited | GUI + Privacy |
| **OpenRouter** | 3 min | Variable | ✅ $1 credit | Model variety |
| **Perplexity** | 3 min | Medium | ✅ Limited | Current trends |
| **OpenAI** | 2 min | High | ❌ Trial only | Industry standard |
| **Anthropic** | 3 min | High | ✅ $5 credit | Safety + Reasoning |

---

## 🏠 **Privacy-First Local Providers**

### **🦙 Ollama (Most Popular Local)**

**Perfect for**: Complete privacy, no ongoing costs, offline operation.

#### **Setup Process (Ollama)**

```bash
# 1. Install Ollama (5 minutes)
curl -fsSL https://ollama.ai/install.sh | sh  # Linux/macOS
# OR download from https://ollama.ai for Windows

# 2. Download recommended model (5 minutes)
ollama pull qwen2.5:latest

# 3. Verify it's working
ollama list
ollama run qwen2.5:latest "Suggest 3 jazz albums"
```

#### **Brainarr Configuration (Ollama)**

- **Provider**: `Ollama`
- **URL**: `http://localhost:11434` (default)
- **Model**: `qwen2.5:latest` (auto-detected)

**Advanced Options**:

- **Temperature**: `0.7` (creativity: 0.0 = deterministic, 1.0 = very creative)
- **Top P**: `0.9` (nucleus sampling for variety)
- **Max Tokens**: `2000` (response length limit)

#### **Model Recommendations**

```bash
# Excellent for music (default)
ollama pull qwen2.5:latest        # 4.7GB, best overall

# Fast alternatives
ollama pull llama3.1:8b          # 4.7GB, good speed
ollama pull mistral:7b           # 4.1GB, lightweight

# Quality alternatives
ollama pull phi3:medium          # 7.9GB, Microsoft's model
ollama pull gemma2:9b            # 5.4GB, Google's model
```

---

### **🎬 LM Studio (GUI-Based Local)**

**Perfect for**: Users who prefer visual model management.

#### **Setup Process (LM Studio)**

1. **Download**: Visit <https://lmstudio.ai>
1. **Install**: Run installer for your platform
1. **Download Model**:

- Open LM Studio  >  **"Discover"** tab
- Search: "Phi-3-medium" or "Mistral-7B"
- Click **"Download"**

1. **Start Server**:

- **"Local Server"** tab  >  Select model  >  **"Start Server"**

#### **Brainarr Configuration (LM Studio)**

- **Provider**: `LM Studio`
- **URL**: `http://localhost:1234` (LM Studio default)
- **Model**: `local-model` (auto-detected)

---

## ☁️ **Budget Cloud Champions**

### **🧠 DeepSeek (Ultra-Low Cost Leader)**

**Perfect for**: High-volume recommendations on a budget.

#### **Why DeepSeek**

- **Cost**: 10-20x cheaper than OpenAI
- **Quality**: V3 model rivals GPT-4 performance
- **Features**: Advanced reasoning capabilities
- **Free Credit**: $5 for new accounts

#### **Setup Process (DeepSeek)**

1. **Sign Up**: <https://platform.deepseek.com>
1. **Get API Key**: <https://platform.deepseek.com/api_keys>
1. **Pricing**: $0.27 per million tokens (ultra-affordable)

#### **Brainarr Configuration (DeepSeek)**

- **Provider**: `DeepSeek`
- **API Key**: `sk-...` (from DeepSeek dashboard)
- **Model**: `deepseek-chat` (V3 - latest and best)

#### **Available Models (DeepSeek)**

- **`deepseek-chat`**: V3 reasoning model, best overall
- **`deepseek-coder`**: Optimized for structured output
- **`deepseek-reasoner`**: R1 model with enhanced reasoning

#### **Special Features (DeepSeek)**

- **Reasoning Mode**: Handles `<thinking>` tags for complex analysis
- **Prompt Caching**: Reduces costs for similar requests
- **JSON Extraction**: Advanced parsing from mixed-content responses

---

### **✨ Google Gemini (Speed + Free Tier Champion)**

**Perfect for**: Fast recommendations with generous free usage.

#### **Why Gemini**

- **Speed**: Sub-second response times

- **Free Tier**: 15 requests/minute, 1500/day
- **Context**: Up to 2M tokens for large libraries
- **Quality**: Excellent for music understanding

#### **Setup Process (Gemini)**

1. **Google Account**: Sign in to Google AI Studio
1. **Get API Key**: <https://makersuite.google.com/app/apikey>
1. **Free Quota**: Starts immediately, no credit card required

#### **Brainarr Configuration (Gemini)**

- **Provider**: `Gemini`

- **API Key**: `AIza...` (from Google AI Studio)
- **Model**: `gemini-1.5-flash` (recommended balance)

#### **Model Selection**

- **`gemini-1.5-flash`**: Best balance (default) - 1M context

- **`gemini-1.5-flash-8b`**: Fastest option - smaller model
- **`gemini-1.5-pro`**: Highest quality - 2M context
- **`gemini-2.0-flash-exp`**: Latest experimental - cutting edge

#### **Special Features (Gemini)**

- **Native JSON Mode**: `responseMimeType: "application/json"`

- **Safety Controls**: Configured for music content appropriateness
- **Context Windows**: Massive context for comprehensive library analysis

---

### **🚀 Groq (Lightning-Fast Inference)**

**Perfect for**: Users who want instant recommendations.

#### **Why Groq**

- **Speed**: Fastest inference available (200-500ms responses)

- **Free Tier**: 30 requests/minute - very generous
- **Models**: Latest Llama and Mixtral models
- **Cost**: Very competitive pricing

#### **Setup Process (Groq)**

1. **Sign Up**: <https://console.groq.com>
1. **Get API Key**: <https://console.groq.com/keys>
1. **Free Tier**: Starts immediately

#### **Brainarr Configuration (Groq)**

- **Provider**: `Groq`

- **API Key**: `gsk_...` (from Groq Console)
- **Model**: `llama-3.3-70b-versatile` (latest, most capable)

#### **Available Models (Groq)**

- **`llama-3.3-70b-versatile`**: Latest Llama 3.3 (recommended)

- **`llama-3.2-90b-vision-preview`**: Multimodal capabilities
- **`llama-3.1-70b-versatile`**: Previous gen, excellent
- **`mixtral-8x7b-32768`**: Mixture-of-experts, fast
- **`gemma2-9b-it`**: Google model via Groq

#### **Performance Features**

- **Response Time Tracking**: Monitors actual inference speed

- **Queue Metrics**: Shows processing queue status
- **Ultra-Fast Hardware**: Custom silicon for AI inference

---

## 🌟 **Premium Quality Providers**

### **🌐 OpenRouter (Model Marketplace)**

**Perfect for**: Access to cutting-edge models from multiple providers.

#### **Why OpenRouter**

- **Variety**: 200+ models from OpenAI, Anthropic, Google, Meta, etc.

- **Pricing**: Often cheaper than direct provider access
- **Features**: Model routing, fallbacks, and optimization
- **Trial**: $1 free credit for testing

#### **Setup Process (OpenRouter)**

1. **Sign Up**: <https://openrouter.ai>
1. **Get API Key**: <https://openrouter.ai/keys>
1. **Choose Model**: Browse available models and pricing

#### **Brainarr Configuration (OpenRouter)**

- **Provider**: `OpenRouter`

- **API Key**: `sk-or-...` (from OpenRouter dashboard)
- **Model**: `anthropic/claude-3.5-haiku` (recommended)

#### **Model Categories**

**Best Value Models:**

- **`anthropic/claude-3.5-haiku`**: Fast Anthropic, excellent quality
- **`deepseek/deepseek-chat`**: Ultra-low cost via OpenRouter
- **`google/gemini-flash-1.5`**: Google's speed champion
- **`meta-llama/llama-3.1-8b-instruct`**: Open source, very affordable

**Premium Models:**

- **`openai/gpt-4o`**: OpenAI's flagship model
- **`anthropic/claude-3-opus`**: Anthropic's most capable
- **`google/gemini-pro-1.5`**: Google's premium model
- **`meta-llama/llama-3.1-405b-instruct`**: Massive open model

**Specialized Models:**

- **`mistral/mistral-large`**: European AI leader
- **`cohere/command-r-plus`**: Enterprise-focused
- **`qwen/qwen-72b-chat`**: Alibaba's advanced model

#### **OpenRouter Features**

- **Model Fallbacks**: Automatic fallback to available models

- **Route Optimization**: "middle-out" performance optimization
- **Usage Analytics**: Detailed model performance metrics
- **Cost Optimization**: Intelligent model selection for cost/quality

---

### **🔍 Perplexity (Search-Enhanced AI)**

**Perfect for**: Discovering current music trends and new releases.

#### **Why Perplexity**

- **Real-Time Data**: Web search integration for current music info

- **Music Database Access**: Connected to music databases and news
- **Trend Awareness**: Knows current releases, festivals, etc.
- **Citation Quality**: Provides sources for recommendations

#### **Setup Process (Perplexity)**

1. **Sign Up**: <https://www.perplexity.ai>
1. **Get API Key**: <https://docs.perplexity.ai/docs/getting-started>
1. **Billing**: Add payment method for usage beyond free tier
   - Tip: Perplexity Pro subscribers receive $5/month in API credits that work with the Perplexity API (useful for Brainarr).

#### **Brainarr Configuration (Perplexity)**

- **Provider**: `Perplexity`

- **API Key**: `pplx-...` (from Perplexity API settings)
- **Model**: `llama-3.1-sonar-large-128k-online` (search-enhanced)
- **Status**: Verified in Brainarr 1.2.4

#### **Available Models (Perplexity)**

- **`llama-3.1-sonar-large-128k-online`**: Best for music discovery

- **`llama-3.1-sonar-small-128k-online`**: Faster, lower cost
- **`llama-3.1-sonar-huge-128k-online`**: Maximum capability

#### **Unique Advantages**

- **Current Music Data**: Real-time information about new releases

- **Festival/Tour Info**: Incorporates concert and festival data
- **Critical Reception**: Includes review scores and critical consensus
- **Citation Handling**: Automatic citation marker removal for clean parsing

---

### **🤖 OpenAI (Industry Standard)**

**Perfect for**: Reliable, consistent performance with mature ecosystem.

#### **Why OpenAI**

- **Reliability**: Most mature and stable API
- **Quality**: Consistent, predictable results
- **Ecosystem**: Extensive documentation and community
- **JSON Mode**: Native structured output support

#### **Setup Process (OpenAI - Direct API)**

1. **Sign Up**: <https://platform.openai.com>
1. **Get API Key**: <https://platform.openai.com/api-keys>
1. **Add Billing**: Required for usage beyond trial

#### **Brainarr Configuration (OpenAI)**

- **Provider**: `OpenAI`
- **API Key**: `sk-...` (from OpenAI dashboard)
- **Model**: `gpt-4o-mini` (most cost-effective)

#### **Model Selection (OpenAI)**

- **`gpt-4o-mini`**: Best value, excellent for music recommendations
- **`gpt-4o`**: Latest model, multimodal capabilities
- **`gpt-4-turbo`**: Previous flagship, still excellent
- **`gpt-3.5-turbo`**: Budget option, lower quality

#### **Pricing (as of 2025)**

- **GPT-4o-mini**: $0.15 input / $0.60 output per 1M tokens
- **GPT-4o**: $2.50 input / $10.00 output per 1M tokens
- **GPT-4-turbo**: $10.00 input / $30.00 output per 1M tokens

---

### **🧠 Anthropic Claude (Safety + Reasoning Leader)**

**Perfect for**: High-quality recommendations with built-in safety features.

#### **Why Anthropic**

- **Reasoning Quality**: Exceptional analysis of music preferences
- **Safety**: Built-in harmful content filtering
- **Long Context**: 200K+ tokens for comprehensive library analysis
- **Reliability**: Consistent performance and uptime

#### **Setup Process (Anthropic - Additional)**

1. **Sign Up**: <https://console.anthropic.com>
1. **Get API Key**: Create new API key in console
1. **Free Credits**: $5 trial credit for new accounts

#### **Brainarr Configuration (Anthropic)**

- **Provider**: `Anthropic`
- **API Key**: `sk-ant-...` (from Anthropic Console)
- **Model**: `claude-3-5-haiku-latest` (fast + cost-effective)

#### **Model Tiers (Anthropic)**

- **`claude-3-5-haiku-latest`**: Fast, cost-effective ($0.25 input / $1.25 output per 1M tokens)
- **`claude-3-5-sonnet-latest`**: Balanced performance ($3 input / $15 output per 1M tokens)
- **`claude-3-opus-20240229`**: Highest quality ($15 input / $75 output per 1M tokens)

#### **Advanced Features (Anthropic)**

- **Messages API**: Clean separation of system prompts and user content
- **Long Context**: Up to 200K tokens for analyzing large music libraries
- **Safety Filtering**: Reduces inappropriate or harmful recommendations
- **Reasoning**: Excellent at understanding complex music relationships

---

## 🔧 **Configuration Best Practices**

### **Provider Selection Strategy**

#### **For Different Library Sizes**

#### Small Library (< 500 albums)

- **Primary**: Gemini (fast, free tier sufficient)
- **Backup**: DeepSeek (low cost for expansion)

#### Medium Library (500-2000 albums)

- **Primary**: DeepSeek (cost-effective for regular use)
- **Backup**: Groq (fast processing)
- **Local**: Ollama (privacy for sensitive preferences)

#### Large Library (2000+ albums)

- **Primary**: Anthropic Claude (best reasoning for complex taste)
- **Backup**: OpenRouter (model variety)
- **Local**: Ollama (offline capability)

#### **Multi-Provider Failover Setup**

**Recommended Configuration**:

1. Primary: Your main provider (quality + cost optimized)
1. Secondary: Different company (avoid single points of failure)
1. Tertiary: Local provider (always available)

**Example Setup**:

- **Primary**: `DeepSeek` (ultra-low cost, high quality)
- **Secondary**: `Gemini` (different company, fast)
- **Tertiary**: `Ollama` (complete offline fallback)

### **Performance Optimization**

#### **Request Size Optimization**

```text
Small Libraries (< 500 albums):     10-15 recommendations
Medium Libraries (500-2000 albums): 20-25 recommendations
Large Libraries (2000+ albums):     30-50 recommendations
```

#### **Rate Limiting Best Practices**

```text
Local Providers:     30 requests/minute (no external limits)
Budget Cloud:        10 requests/minute (API limits)
Premium Cloud:       20 requests/minute (higher limits)
```

#### **Cache Optimization**

- **Default**: 60 minutes (balances freshness vs. performance)
- **High-Change Libraries**: 30 minutes
- **Stable Libraries**: 120 minutes
- **Testing**: 5 minutes for rapid iteration

### **Security Configuration**

#### **API Key Security**

- **Principle**: Use least-privilege API keys when possible
- **Rotation**: Rotate keys monthly for production use
- **Monitoring**: Monitor usage for unexpected spikes
- **Backup Keys**: Have backup keys ready for failover

#### **Network Security**

```bash
# Local providers - restrict to localhost
# Cloud providers - allow HTTPS outbound only
sudo ufw allow out 443          # HTTPS for all cloud providers
sudo ufw deny out 80            # Block insecure HTTP
sudo ufw allow out 11434        # Ollama local only
sudo ufw allow out 1234         # LM Studio local only
```

---

## 🧪 **Testing Your Setup**

### **Basic Connectivity Test**

1. Configure provider in Brainarr settings
1. Click **"Test"** button
1. Should show: **"Test was successful"** ✅

### **Recommendation Test**

1. Import Lists > Brainarr > Manual Import
1. Click "Manual Import" to trigger immediate test
1. Check System > Logs for processing details

### **Health Monitoring**

1. System > Tasks > Check Health
1. Should show green status for configured providers
1. Failed providers will show red with error details

---

## 🔀 **Switching Between Providers**

### **Runtime Provider Changes**

Brainarr supports hot-swapping providers without restart:

1. Settings > Import Lists > Brainarr
1. Change "Provider" dropdown
1. Configure new provider settings
1. Test and Save
1. Next recommendation run uses new provider

### **Provider Migration Checklist**

- ✅ Test new provider configuration
- ✅ Verify API key and model settings
- ✅ Check rate limits and quotas
- ✅ Monitor initial recommendations quality
- ✅ Update fallback provider if needed

---

## 📊 **Usage Monitoring**

### **Built-in Monitoring**

Brainarr automatically tracks:

- **Provider Health**: Success/failure rates per provider
- **Response Times**: Average response times for performance monitoring
- **Cache Hit Rates**: Cache effectiveness for optimization
- **Error Patterns**: Common issues for troubleshooting

### **Lidarr Integration**

Monitor through Lidarr's interface:

- **System**  >  **Logs**: Detailed operation logs
- **System**  >  **Tasks**: Health check status
- **Settings**  >  **Import Lists**: Last execution results

### **Cost Tracking**

For cloud providers, monitor usage:

- Check provider dashboards monthly
- Set up billing alerts
- Track token consumption patterns
- Optimize based on actual usage

---

## 🎯 **Recommendation: Start Here**

### **New to AI**

Start with **Gemini** (generous free tier, fast setup)

### **Privacy-Conscious**

Start with **Ollama** (complete local control)

### **Budget-Focused**

Start with **DeepSeek** (ultra-low cost, high quality)

### **Performance-Critical**

Start with **Groq** (fastest inference available)

**Next Step**: Follow the specific setup guide for your chosen provider, then test with [[First Run Guide]]!\n\n
