# ☁️ Cloud Providers - Enterprise AI Performance

Complete setup guide for cloud-based AI providers. These services offer cutting-edge models with enterprise-grade reliability and performance.

## 💰 **Cost-Effectiveness Ranking**

| Rank | Provider | Cost/1K Rec | Free Tier | Best Model |
|------|----------|-------------|-----------|------------|
| 1️⃣ | **DeepSeek** | $0.002 | ✅ Limited | `deepseek-chat` |
| 2️⃣ | **Gemini** | $0.005 | ✅ Generous | `gemini-1.5-flash` |
| 3️⃣ | **Groq** | $0.010 | ✅ Good | `llama-3.3-70b-versatile` |
| 4️⃣ | **OpenRouter** | $0.015 | ✅ Trial | `anthropic/claude-3.5-haiku` |
| 5️⃣ | **Perplexity** | $0.020 | ✅ Limited | `llama-3.1-sonar-large-128k-online` |
| 6️⃣ | **OpenAI** | $0.050 | ❌ Pay-per-use | `gpt-4o-mini` |
| 7️⃣ | **Anthropic** | $0.080 | ✅ Trial | `claude-3-5-haiku-latest` |

---

## 💎 **Budget-Friendly Champions**

### **🧠 DeepSeek (Ultra Low Cost)**

**Why DeepSeek**: 10-20x cheaper than OpenAI with V3 reasoning capabilities.

#### **Setup Steps**
1. **Get API Key**: Visit https://platform.deepseek.com/api_keys
2. **Pricing**: $0.27 per million tokens (extremely affordable)
3. **Free Credits**: $5 free credit for new accounts

#### **Brainarr Configuration**
- **Provider**: `DeepSeek`
- **API Key**: `sk-...` (your DeepSeek key)
- **Model**: `deepseek-chat` (V3 - latest and best)

#### **Available Models**
- **`deepseek-chat`**: V3 model, best overall performance
- **`deepseek-coder`**: Optimized for structured output
- **`deepseek-reasoner`**: R1 model with thinking capabilities

#### **Special Features**
- **Thinking Tags**: Handles `<thinking>...</thinking>` reasoning output
- **Cache Optimization**: Prompt caching for repeat requests
- **Code Filtering**: Automatically removes code comments from responses
- **Ultra-Low Cost**: Perfect for high-volume recommendations

---

### **✨ Google Gemini (Speed Champion)**

**Why Gemini**: Ultra-fast inference with generous free tier.

#### **Setup Steps**
1. **Get API Key**: Visit https://makersuite.google.com/app/apikey
2. **Pricing**: $0.075 per million tokens (very affordable)
3. **Free Tier**: 15 requests per minute, 1500 requests per day

#### **Brainarr Configuration**
- **Provider**: `Gemini`
- **API Key**: `AIza...` (your Google AI key)
- **Model**: `gemini-1.5-flash` (recommended for speed)

#### **Available Models**
- **`gemini-1.5-flash`**: Ultra-fast, 1M context window
- **`gemini-1.5-flash-8b`**: Even faster, smaller model
- **`gemini-1.5-pro`**: Higher quality, 2M context window  
- **`gemini-2.0-flash-exp`**: Latest experimental version

#### **Special Features**
- **JSON Mode**: Native `responseMimeType: "application/json"` support
- **Safety Settings**: Configured to allow music content
- **Context Window**: Up to 2M tokens for large library analysis
- **Speed**: Sub-second response times

---

## ⚡ **Performance Leaders**

### **🚀 Groq (Ultra-Fast Inference)**

**Why Groq**: Fastest inference available, excellent free tier.

#### **Setup Steps**
1. **Get API Key**: Visit https://console.groq.com/keys
2. **Pricing**: $0.59 per million tokens
3. **Free Tier**: 30 requests per minute, generous daily limits

#### **Brainarr Configuration**
- **Provider**: `Groq`
- **API Key**: `gsk_...` (your Groq key)
- **Model**: `llama-3.3-70b-versatile` (latest, most capable)

#### **Available Models**
- **`llama-3.3-70b-versatile`**: Latest Llama 3.3, best quality
- **`llama-3.2-90b-vision-preview`**: Multimodal capabilities
- **`llama-3.1-70b-versatile`**: Previous generation, still excellent
- **`mixtral-8x7b-32768`**: Fast mixture-of-experts model
- **`gemma2-9b-it`**: Google's efficient model

#### **Special Features**
- **Response Time Tracking**: Logs actual inference speed
- **Usage Metrics**: Queue time and total time monitoring
- **JSON Object Mode**: Structured output support
- **Ultra-Fast**: Typically responds in 200-500ms

---

### **🌐 OpenRouter (Model Marketplace)**

**Why OpenRouter**: Access to 200+ models from different providers in one API.

#### **Setup Steps**
1. **Get API Key**: Visit https://openrouter.ai/keys
2. **Pricing**: Variable by model (often cheaper than direct providers)
3. **Free Credits**: $1 free credit for testing

#### **Brainarr Configuration**
- **Provider**: `OpenRouter`
- **API Key**: `sk-or-...` (your OpenRouter key)  
- **Model**: `anthropic/claude-3.5-haiku` (best balance)

#### **Recommended Models**

**Best Value:**
- **`anthropic/claude-3.5-haiku`**: Fast Anthropic model
- **`deepseek/deepseek-chat`**: Ultra-low cost with great quality
- **`google/gemini-flash-1.5`**: Google's speed champion

**Balanced Performance:**
- **`anthropic/claude-3.5-sonnet`**: Excellent reasoning
- **`openai/gpt-4o-mini`**: OpenAI's efficient model
- **`meta-llama/llama-3.1-70b-instruct`**: Meta's powerful model

**Premium Quality:**
- **`openai/gpt-4o`**: Latest OpenAI flagship
- **`anthropic/claude-3-opus`**: Most capable Anthropic model
- **`google/gemini-pro-1.5`**: Google's premium model

#### **Special Features**
- **Model Fallback**: Automatic fallback if primary model unavailable
- **Transforms**: "middle-out" optimization for balanced performance
- **Route Fallback**: Automatic routing to available models
- **Model Tracking**: Logs which model actually handled each request

---

## 🎯 **Premium Quality Leaders**

### **🔍 Perplexity (Search-Enhanced AI)**

**Why Perplexity**: Real-time web search enhances music discovery with current trends.

#### **Setup Steps**
1. **Get API Key**: Visit https://docs.perplexity.ai/docs/getting-started
2. **Pricing**: $1-5 per million tokens (varies by model)
3. **Free Tier**: Limited requests for evaluation

#### **Configuration**
- **Provider**: `Perplexity`
- **API Key**: `pplx-...` (your Perplexity key)
- **Model**: `llama-3.1-sonar-large-128k-online` (search-enhanced)

#### **Available Models**
- **`llama-3.1-sonar-large-128k-online`**: Best for music discovery
- **`llama-3.1-sonar-small-128k-online`**: Faster, lower cost
- **`llama-3.1-sonar-huge-128k-online`**: Maximum capability

#### **Unique Features**
- **Web Search Integration**: Real-time music data and trends
- **Citation Handling**: Automatically removes citation markers
- **Current Information**: Up-to-date music releases and artist info
- **Music Database Access**: Enhanced with MusicBrainz and other sources

---

### **🤖 OpenAI (Industry Standard)**

**Why OpenAI**: Most mature API with consistent performance and reliability.

#### **Setup Steps**
1. **Get API Key**: Visit https://platform.openai.com/api-keys
2. **Pricing**: $0.15-15 per million tokens (varies by model)
3. **Free Trial**: $5 credit for new accounts

#### **Configuration**
- **Provider**: `OpenAI`
- **API Key**: `sk-...` (your OpenAI key)
- **Model**: `gpt-4o-mini` (most cost-effective)

#### **Available Models**
- **`gpt-4o-mini`**: Most cost-effective, excellent quality
- **`gpt-4o`**: Latest multimodal model, premium quality
- **`gpt-4-turbo`**: Previous generation, still excellent
- **`gpt-3.5-turbo`**: Legacy model, lowest cost

#### **Features**
- **JSON Object Mode**: Native structured output
- **Consistent Quality**: Reliable, predictable responses
- **Mature Ecosystem**: Well-documented, widely supported
- **Fine-Tuning**: Custom model training available

---

### **🧠 Anthropic Claude (Safety-First AI)**

**Why Anthropic**: Exceptional reasoning with built-in safety features.

#### **Setup Steps**
1. **Get API Key**: Visit https://console.anthropic.com
2. **Pricing**: $3-15 per million tokens (premium pricing)
3. **Free Credits**: $5 trial credit

#### **Configuration**
- **Provider**: `Anthropic`
- **API Key**: `sk-ant-...` (your Anthropic key)
- **Model**: `claude-3-5-haiku-latest` (fast and cost-effective)

#### **Available Models**
- **`claude-3-5-haiku-latest`**: Fast, cost-effective
- **`claude-3-5-sonnet-latest`**: Balanced performance
- **`claude-3-opus-20240229`**: Highest quality, most expensive

#### **Features**
- **Advanced Reasoning**: Excellent for complex music analysis
- **Safety Built-in**: Reduced harmful outputs
- **Long Context**: Up to 200K tokens for large library analysis
- **System Messages**: Clean separation of instructions and content

---

## 🔧 **Provider Selection Strategy**

### **For Different Use Cases**

**🏠 Personal Use (Budget-Conscious):**
1. **DeepSeek** - Ultra-low cost, excellent quality
2. **Gemini** - Fast, generous free tier
3. **Groq** - Speed champion with good free tier

**🏢 Professional Use (Quality-First):**
1. **Anthropic Claude** - Best reasoning and safety
2. **OpenAI GPT-4o** - Industry standard reliability  
3. **Perplexity** - Current music trends and data

**⚡ High-Volume Use:**
1. **DeepSeek** - Ultra-low cost for bulk recommendations
2. **OpenRouter** - Access to multiple models, competitive pricing
3. **Gemini** - Fast inference for quick turnaround

### **Multi-Provider Strategy**

**Recommended Setup:**
- **Primary**: Your preferred provider (quality + cost balance)
- **Fallback**: Different provider type for reliability
- **Emergency**: Local provider for offline capability

**Example Configuration:**
1. **Primary**: DeepSeek (cost-effective, high quality)
2. **Fallback**: Gemini (different company, fast)
3. **Emergency**: Ollama (offline capability)

---

## 📊 **Provider Comparison Matrix**

| Feature | DeepSeek | Gemini | Groq | OpenRouter | Perplexity | OpenAI | Anthropic |
|---------|----------|--------|------|------------|------------|--------|-----------|
| **Cost** | 🟢 Ultra Low | 🟢 Low | 🟡 Medium | 🟡 Variable | 🟡 Medium | 🔴 High | 🔴 High |
| **Speed** | 🟡 Fast | 🟢 Ultra Fast | 🟢 Ultra Fast | 🟡 Fast | 🟡 Fast | 🟡 Fast | 🟡 Fast |
| **Quality** | 🟢 Excellent | 🟡 Very Good | 🟡 Good | 🟢 Excellent | 🟢 Excellent | 🟢 Excellent | 🟢 Excellent |
| **Reliability** | 🟡 Good | 🟢 Excellent | 🟡 Good | 🟢 Excellent | 🟡 Good | 🟢 Excellent | 🟢 Excellent |
| **Free Tier** | 🟡 Limited | 🟢 Generous | 🟢 Good | 🟡 Trial | 🟡 Limited | 🔴 Trial Only | 🟡 Trial |

---

## 🔐 **Security & Best Practices**

### **API Key Management**
- **Never Commit**: Keep API keys out of version control
- **Environment Variables**: Use secure environment storage when possible
- **Key Rotation**: Regularly rotate API keys for security
- **Access Control**: Use least-privilege API key permissions

### **Rate Limit Management**
- **Respect Limits**: Configure Brainarr rate limiting appropriately
- **Monitor Usage**: Watch for approaching API quotas
- **Burst Handling**: Configure burst size for peak usage
- **Fallback Strategy**: Have backup providers configured

### **Cost Control**
- **Set Budgets**: Configure spending limits in provider dashboards
- **Monitor Usage**: Track token consumption regularly
- **Optimize Settings**: Tune max tokens and recommendation count
- **Cache Utilization**: Leverage Brainarr's 60-minute caching

---

**Next Steps:**
- **Ready to test?** Follow the [[First Run Guide]]
- **Need optimization?** Check [[Performance Tuning]]  
- **Having issues?** See [[Troubleshooting Guide]]