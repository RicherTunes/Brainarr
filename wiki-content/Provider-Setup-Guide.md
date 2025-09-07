# ⚙️ Provider Setup Guide

Complete configuration guide for all 9 supported AI providers.

## 🎯 **Provider Selection Strategy**

### **🔒 Privacy-First (Recommended for Personal Use)**
- **[[Ollama]]** - Best local option, easy setup
- **[[LM Studio]]** - User-friendly local alternative

### **☁️ Cloud Providers (Best Performance)**
- **[[OpenAI]]** - Industry standard, reliable
- **[[Anthropic]]** - Excellent reasoning, safe
- **[[Google Gemini]]** - Fast, cost-effective
- **[[OpenRouter]]** - Access to multiple models
- **[[Groq]]** - Ultra-fast inference
- **[[DeepSeek]]** - Advanced reasoning capabilities
- **[[Perplexity]]** - Search-enhanced responses

---

## 🏠 **Local Providers**

### **🦙 Ollama (Recommended Local)**

**Setup Steps:**
1. **Install Ollama**: Download from https://ollama.ai
2. **Pull a model**: `ollama pull qwen2.5:latest`
3. **Configure Brainarr**:
   - Provider: **Ollama**
   - URL: `http://localhost:11434`
   - Model: `qwen2.5:latest`

**Recommended Models:**
- `qwen2.5:latest` - Best overall (default)
- `llama3.1:8b` - Faster, good quality
- `mistral:7b` - Lightweight option

**Pros:** Complete privacy, no API costs, works offline
**Cons:** Requires local GPU/CPU resources

---

### **🎬 LM Studio**

**Setup Steps:**
1. **Install LM Studio**: Download from https://lmstudio.ai
2. **Download a model**: Search for "music" or "recommendation" models
3. **Start local server**: Click "Local Server" → "Start Server"
4. **Configure Brainarr**:
   - Provider: **LM Studio**
   - URL: `http://localhost:1234`
   - Model: `local-model`

**Pros:** User-friendly GUI, model management
**Cons:** Limited to specific models, resource intensive

---

## ☁️ **Cloud Providers**

### **🤖 OpenAI**

**Setup Steps:**
1. **Get API Key**: https://platform.openai.com/api-keys
2. **Configure Brainarr**:
   - Provider: **OpenAI**
   - API Key: `sk-...` (your key)
   - Model: `GPT4o_Mini` (recommended)

**Available Models:**
- `GPT4o_Mini` - Cost-effective, fast
- `GPT4o` - Best quality, more expensive
- `GPT35_Turbo` - Budget option

**Cost:** ~$0.01-0.10 per recommendation batch

---

### **🧠 Anthropic (Claude)**

**Setup Steps:**
1. **Get API Key**: https://console.anthropic.com
2. **Configure Brainarr**:
   - Provider: **Anthropic**
   - API Key: `sk-ant-...`
   - Model: `Claude35_Haiku` (recommended)

**Available Models:**
- `Claude35_Haiku` - Fast, cost-effective
- `Claude35_Sonnet` - Balanced performance
- `Claude3_Opus` - Highest quality

**Cost:** ~$0.005-0.05 per recommendation batch

---

### **✨ Google Gemini**

**Setup Steps:**
1. **Get API Key**: https://makersuite.google.com/app/apikey
2. **Configure Brainarr**:
   - Provider: **Gemini**
   - API Key: `AIza...`
   - Model: `Gemini_15_Flash` (recommended)

**Available Models:**
- `Gemini_15_Flash` - Very fast, low cost
- `Gemini_15_Pro` - Higher quality
- `Gemini_20_Flash` - Latest version

**Cost:** ~$0.001-0.01 per recommendation batch

---

### **🌐 OpenRouter**

**Setup Steps:**
1. **Get API Key**: https://openrouter.ai/keys
2. **Configure Brainarr**:
   - Provider: **OpenRouter**
   - API Key: `sk-or-...`
   - Model: `Claude35_Haiku` (recommended)

**Available Models:**
- `Claude35_Haiku` - Anthropic via OpenRouter
- `GPT4o` - OpenAI via OpenRouter
- `Llama32_90B` - Meta's latest

**Pros:** Access to multiple models, competitive pricing
**Cost:** Variable based on model choice

---

### **⚡ Groq**

**Setup Steps:**
1. **Get API Key**: https://console.groq.com/keys
2. **Configure Brainarr**:
   - Provider: **Groq**
   - API Key: `gsk_...`
   - Model: `Llama33_70B` (recommended)

**Available Models:**
- `Llama33_70B` - Best balance
- `Llama32_90B_Vision` - Latest with vision
- `Mixtral_8x7B` - Alternative option

**Pros:** Extremely fast inference
**Cost:** Very low, generous free tier

---

### **🧠 DeepSeek**

**Setup Steps:**
1. **Get API Key**: https://platform.deepseek.com
2. **Configure Brainarr**:
   - Provider: **DeepSeek**
   - API Key: `sk-...`
   - Model: `DeepSeek_Chat` (recommended)

**Available Models:**
- `DeepSeek_Chat` - General purpose
- `DeepSeek_Coder` - Logic-focused

**Pros:** Advanced reasoning, competitive pricing
**Cost:** ~$0.002-0.02 per recommendation batch

---

### **🔍 Perplexity**

**Setup Steps:**
1. **Get API Key**: https://docs.perplexity.ai/docs/getting-started
2. **Configure Brainarr**:
   - Provider: **Perplexity**
   - API Key: `pplx-...`
   - Model: `Sonar_Large` (recommended)

**Available Models:**
- `Sonar_Large` - Best for music discovery
- `Sonar_Huge` - Maximum capability

**Pros:** Search-enhanced recommendations
**Cost:** ~$0.01-0.05 per recommendation batch

---

## ⚙️ **Configuration Tips**

### **🎯 Recommendation Count**
- **Start with:** 10-20 recommendations
- **Large libraries:** 30-50 for more variety
- **Small libraries:** 5-10 to avoid overwhelming

### **📊 Discovery Mode**
- **Similar:** Safe recommendations (familiar genres)
- **Adjacent:** Moderate exploration (related genres)
- **Exploratory:** Maximum discovery (new genres)

### **🔄 Provider Failover**
Configure multiple providers for reliability:
1. **Primary:** Your preferred provider
2. **Fallback:** Alternative for when primary fails

### **⚡ Performance Optimization**
- **Caching:** Recommendations cached for 60 minutes
- **Rate Limiting:** Automatic throttling per provider
- **Batch Size:** Optimal batch sizes per provider

---

## ❓ **Common Configuration Issues**

### **API Key Invalid**
- Verify key is correct and active
- Check API usage limits/quotas
- Ensure account has sufficient credits

### **Connection Timeout**
- Check firewall settings
- Verify proxy configuration
- Increase timeout in advanced settings

### **No Recommendations**
- Verify provider configuration with "Test" button
- Check Lidarr logs for error details
- Ensure model supports the required format

### **Poor Quality Recommendations**
- Try different discovery mode
- Adjust recommendation count
- Consider switching provider/model

---

**Next:** Learn about [[Advanced Settings]] and [[Recommendation Modes]]!
