# ⚙️ Provider Setup Guide

Complete configuration guide for all 9 supported AI providers.
> **Model IDs are case-sensitive.** Use lowercase, hyphenated IDs (e.g., `gpt-4o-mini`, `claude-3-5-sonnet-20240620`). When routing via OpenRouter, copy the full upstream slug from the Provider Matrix.

## 🎯 **Provider Selection Strategy**

### **🔒 Privacy-First (Recommended for Personal Use)**

- **Ollama** — Best local option; start with the [Local Providers guide](Local-Providers).
- **LM Studio** — GUI-friendly local alternative; see the [Local Providers guide](Local-Providers).

### **☁️ Cloud Providers (Best Performance)**

- **OpenAI** — Industry standard; follow the [Cloud Providers guide](Cloud-Providers).
- **Anthropic** — Excellent reasoning with strong safety; see the [Cloud Providers guide](Cloud-Providers).
- **Google Gemini** — Fast and cost-effective; see the [Cloud Providers guide](Cloud-Providers).
- **OpenRouter** — Gateway to many models; see the [Cloud Providers guide](Cloud-Providers).
- **Groq** — Ultra-fast inference; see the [Cloud Providers guide](Cloud-Providers).
- **DeepSeek** — Advanced reasoning; see the [Cloud Providers guide](Cloud-Providers).
- **Perplexity** — Web-aware responses; see the [Cloud Providers guide](Cloud-Providers).

---

## 🏠 **Local Providers**

### **🦙 Ollama (Recommended Local)**

**Setup Steps:**

1. **Install Ollama**: Download from <https://ollama.ai>
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

1. **Install LM Studio**: Download from <https://lmstudio.ai>
2. **Download a model**: Search for "music" or "recommendation" models
3. **Start local server**: Click "Local Server" → "Start Server"
4. **Configure Brainarr**:
   - Provider: **LM Studio**
   - URL: `http://localhost:1234`
   - Model: use the loaded model name (e.g., `qwen2.5:latest`)

**Pros:** User-friendly GUI, model management
**Cons:** Limited to specific models, resource intensive

---

## ☁️ **Cloud Providers**

### **🤖 OpenAI**

**Setup Steps:**

1. **Get API Key**: <https://platform.openai.com/api-keys>
2. **Configure Brainarr**:
   - Provider: **OpenAI**
   - API Key: `sk-...` (your key)
   - Model: `gpt-4o-mini` (OpenAI)

**Available Models:**

- `gpt-4o-mini` - Cost-effective, fast
- `gpt-4o` - Best quality, more expensive
- `gpt-3.5-turbo` - Budget option

**Cost:** ~$0.01-0.10 per recommendation batch

---

### **🧠 Anthropic (Claude)**

**Setup Steps:**

1. **Get API Key**: <https://console.anthropic.com>
2. **Configure Brainarr**:
   - Provider: **Anthropic**
   - API Key: `sk-ant-...`
   - Model: `claude-3-5-haiku-latest` (recommended)

**Available Models:**

- `claude-3-5-haiku-latest` - Fast, cost-effective
- `claude-3-5-sonnet-20240620` - Balanced performance
- `claude-3-opus` - Highest quality

**Cost:** ~$0.005-0.05 per recommendation batch

---

### **✨ Google Gemini**

**Setup Steps:**

1. **Get API Key**: <https://makersuite.google.com/app/apikey>
2. **Configure Brainarr**:
   - Provider: **Gemini**
   - API Key: `AIza...`
   - Model: `gemini-1.5-flash` (recommended)

**Available Models:**

- `gemini-1.5-flash` - Very fast, low cost
- `gemini-1.5-pro` - Higher quality
- `gemini-2.0-flash` - Latest version

**Cost:** ~$0.001-0.01 per recommendation batch

---

### **🌐 OpenRouter**

**Setup Steps:**

1. **Get API Key**: <https://openrouter.ai/keys>
2. **Configure Brainarr**:
   - Provider: **OpenRouter**
   - API Key: sk-or-...
   - Model: openrouter/anthropic/claude-3.5-haiku (recommended)

**Available Routes:**

- openrouter/anthropic/claude-3.5-haiku — Anthropic via OpenRouter
- openrouter/openai/gpt-4o-mini — OpenAI via OpenRouter
- openrouter/meta-llama/llama-3.1-70b-instruct — Meta's latest

> **Note:** Every OpenRouter slug includes the upstream provider. Cross-check the Provider Matrix before selecting a route.

**Pros:** Access to multiple models, competitive pricing
**Cost:** Variable based on model choice

---

### **⚡ Groq**

**Setup Steps:**

1. **Get API Key**: <https://console.groq.com/keys>
2. **Configure Brainarr**:
   - Provider: **Groq**
   - API Key: `gsk_...`
   - Model: `llama3-70b-8192` (recommended)

**Available Models:**

- `llama3-70b-8192` - Best balance
- `llama-3.2-90b-vision` - Latest with vision
- `mixtral-8x7b` - Alternative option

**Pros:** Extremely fast inference
**Cost:** Very low, generous free tier

---

### **🧠 DeepSeek**

**Setup Steps:**

1. **Get API Key**: <https://platform.deepseek.com>
2. **Configure Brainarr**:
   - Provider: **DeepSeek**
   - API Key: `sk-...`
   - Model: `deepseek-chat` (recommended)

**Available Models:**

- `deepseek-chat` - General purpose
- `deepseek-coder` - Logic-focused

**Pros:** Advanced reasoning, competitive pricing
**Cost:** ~$0.002-0.02 per recommendation batch

---

### **🔍 Perplexity**

**Setup Steps:**

1. **Get API Key**: <https://docs.perplexity.ai/docs/getting-started>
2. **Configure Brainarr**:
   - Provider: **Perplexity**
   - API Key: `pplx-...`
   - Model: `sonar-large` (recommended)

**Available Models:**

- `sonar-large` - Best for music discovery
- `sonar-huge` - Maximum capability

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

**Next:** Learn about [Advanced Settings](Advanced-Settings) and [Recommendation Modes](https://github.com/RicherTunes/Brainarr/blob/main/docs/RECOMMENDATION_MODES.md)!
