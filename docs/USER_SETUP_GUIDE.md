# Brainarr Setup Guide

## 🚀 Quick Start

### Step 1: Choose Your Provider

Brainarr supports multiple AI providers. Here's how to choose:

| Provider | Privacy | Cost | Speed | Best For |
|----------|---------|------|-------|----------|
| **🏠 Ollama** | 100% Private | Free | Medium | Privacy-conscious users |
| **🖥️ LM Studio** | 100% Private | Free | Medium | GUI lovers |
| **🌐 OpenRouter** | Cloud | Varies | Varies | Testing many models |
| **💰 DeepSeek** | Cloud | Ultra-low | Fast | Budget users |
| **🆓 Gemini** | Cloud | Free tier | Fast | Getting started |
| **⚡ Groq** | Cloud | Low | Ultra-fast | Speed demons |

### Step 2: Provider-Specific Setup

## 🏠 Ollama Setup (Recommended for Privacy)

### 1. Install Ollama
```bash
# Linux/Mac
curl -fsSL https://ollama.com/install.sh | sh

# Windows
# Download from https://ollama.com/download
```

### 2. Pull a Model
```bash
# Recommended models for music recommendations
ollama pull llama3        # Best overall (8B)
ollama pull mistral       # Fast and efficient (7B)
ollama pull mixtral       # High quality (8x7B)
ollama pull gemma2        # Google's efficient model
```

### 3. Configure in Lidarr
1. Go to Settings → Import Lists → Add List → Brainarr
2. Select **Provider**: 🏠 Ollama (Local, Private)
3. **Ollama URL**: `http://localhost:11434` (default)
4. Click **Test** - This will verify connection
5. **Ollama Model**: Select from dropdown (populated after test)
6. Configure discovery settings
7. Save

### ⚠️ Important Notes:
- Always click **Test** first to populate model list
- Ollama must be running (`ollama serve`)
- Your music data never leaves your server

## 🖥️ LM Studio Setup (Local with GUI)

### 1. Install LM Studio
Download from [lmstudio.ai](https://lmstudio.ai)

### 2. Load a Model
1. Open LM Studio
2. Go to Models tab
3. Search and download a model (e.g., Llama 3 8B)
4. Load the model

### 3. Start Local Server
1. Go to Developer → Local Server
2. Select your loaded model
3. Click "Start Server"
4. Note the URL (usually `http://localhost:1234`)

### 4. Configure in Lidarr
1. Select **Provider**: 🖥️ LM Studio (Local, GUI)
2. **LM Studio URL**: `http://localhost:1234`
3. Click **Test** to verify
4. **LM Studio Model**: Select from dropdown
5. Save

## 🌐 OpenRouter Setup (Access 200+ Models)

### 1. Get API Key
1. Visit [openrouter.ai/keys](https://openrouter.ai/keys)
2. Sign up/login
3. Create new API key
4. Copy the key

### 2. Configure in Lidarr
1. Select **Provider**: 🌐 OpenRouter (200+ Models)
2. **OpenRouter API Key**: Paste your key
3. **OpenRouter Model**: Choose from:
   - DeepSeek V3 (cheapest)
   - Claude 3.5 Haiku (balanced)
   - GPT-4o (premium)
4. Click **Test**
5. Save

### 💡 OpenRouter Benefits:
- One API key for all models
- Pay-per-use pricing
- Automatic fallback if model unavailable
- Access to latest models immediately

## 💰 DeepSeek Setup (Ultra Cost-Effective)

### 1. Get API Key
1. Visit [platform.deepseek.com](https://platform.deepseek.com)
2. Sign up (often has free credits)
3. Generate API key

### 2. Configure
1. Select **Provider**: 💰 DeepSeek (Ultra Cheap)
2. **DeepSeek API Key**: Your key
3. **DeepSeek Model**: DeepSeek Chat (recommended)
4. Test and Save

### 📊 Cost Comparison:
- DeepSeek: ~$0.14 per million tokens
- GPT-4: ~$30 per million tokens
- **210x cheaper!**

## 🆓 Google Gemini Setup (Free Tier!)

### 1. Get FREE API Key
1. Visit [aistudio.google.com/apikey](https://aistudio.google.com/apikey)
2. Sign in with Google account
3. Click "Get API Key"
4. Create new key (FREE!)

### 2. Configure
1. Select **Provider**: 🆓 Google Gemini (Free Tier)
2. **Gemini API Key**: Your free key
3. **Gemini Model**: 
   - Flash (fastest, 1M context)
   - Pro (most capable, 2M context)
4. Test and Save

### 🎁 Free Tier Includes:
- 1,500 requests/day
- 1M token context window
- Perfect for testing!

## ⚡ Groq Setup (Ultra-Fast)

### 1. Get API Key
1. Visit [console.groq.com/keys](https://console.groq.com/keys)
2. Sign up (free tier available)
3. Create API key

### 2. Configure
1. Select **Provider**: ⚡ Groq (Ultra Fast)
2. **Groq API Key**: Your key
3. **Groq Model**: Llama 3.3 70B (recommended)
4. Test and Save

### 🚀 Speed Comparison:
- Groq: ~500 tokens/second
- Others: ~20-50 tokens/second
- **10-25x faster!**

## ⚙️ Discovery Settings

### Number of Recommendations
- **Default**: 10 albums
- **Beginners**: Start with 5
- **Power Users**: Up to 50
- 💡 Start small and increase if you like results

### Discovery Mode
Choose how adventurous recommendations should be:

| Mode | Description | Example |
|------|-------------|---------|
| **Similar** | Very close to your taste | If you like Pink Floyd → More prog rock |
| **Adjacent** | Related genres | If you like Metal → Hard rock, punk |
| **Exploratory** | New territories | If you like Rock → Jazz, electronic |

## 🧪 Testing Your Setup

### The Test Button Does:
1. ✅ Verifies connection to provider
2. ✅ Validates API key (if needed)
3. ✅ Populates available models
4. ✅ Estimates response speed
5. ✅ Shows any errors clearly

### Common Test Results:

#### ✅ Success
```
Connection successful!
Models detected: 3
Response time: 245ms
```

#### ❌ Failures and Solutions

**"Connection refused"**
- Local provider not running
- Wrong URL/port
- Firewall blocking connection

**"Invalid API key"**
- Check key is copied correctly
- Verify key hasn't expired
- Ensure correct provider selected

**"Model not found"**
- Pull/download the model first
- Select different model
- Click Test to refresh list

## 📊 Provider Comparison

### Privacy Ranking
1. 🏠 Ollama - 100% private
2. 🖥️ LM Studio - 100% private
3. Others - Cloud-based

### Cost Ranking (Cheapest First)
1. 🏠 Ollama - Free
2. 🖥️ LM Studio - Free
3. 🆓 Gemini - Free tier
4. 💰 DeepSeek - $0.14/M tokens
5. ⚡ Groq - $0.10-0.30/M tokens
6. 🌐 OpenRouter - Varies by model
7. Others - Premium pricing

### Speed Ranking
1. ⚡ Groq - 500+ tokens/sec
2. 💰 DeepSeek - 100+ tokens/sec
3. 🆓 Gemini - 50+ tokens/sec
4. Others - 20-50 tokens/sec

### Quality Ranking
1. 🧠 Anthropic Claude
2. 🤖 OpenAI GPT-4
3. 🌐 OpenRouter (Claude/GPT)
4. 🆓 Gemini Pro
5. 💰 DeepSeek V3
6. 🏠 Ollama (depends on model)

## 🎯 Recommendations by Use Case

### "I want maximum privacy"
→ Use **Ollama** or **LM Studio**

### "I want lowest cost"
→ Use **Gemini** (free) or **DeepSeek** (cheap)

### "I want fastest responses"
→ Use **Groq**

### "I want best quality"
→ Use **Anthropic** or **OpenAI**

### "I want to try everything"
→ Use **OpenRouter**

### "I'm just starting"
→ Use **Gemini** (free tier)

## 🔧 Troubleshooting

### No models showing in dropdown
1. Click **Test** button first
2. Ensure provider is running
3. Check connection URL

### Recommendations seem generic
1. Try different Discovery Mode
2. Increase recommendation count
3. Switch to better model

### Too expensive
1. Switch to DeepSeek or Gemini
2. Reduce recommendation count
3. Use local provider (Ollama)

### Too slow
1. Switch to Groq
2. Use smaller model
3. Reduce recommendation count

## 💡 Pro Tips

1. **Start Local**: Try Ollama first for privacy
2. **Test Free Tiers**: Gemini offers generous free tier
3. **Use Test Button**: Always test after changes
4. **Start Small**: Begin with 5-10 recommendations
5. **Experiment**: Try different Discovery Modes
6. **Monitor Costs**: Cloud providers charge per use
7. **Cache Results**: Enable caching to reduce API calls
8. **Failover**: Consider backup provider

## 📚 Additional Resources

- [Ollama Documentation](https://github.com/ollama/ollama)
- [LM Studio Guide](https://lmstudio.ai/docs)
- [OpenRouter Models](https://openrouter.ai/models)
- [Gemini Pricing](https://ai.google.dev/pricing)
- [Groq Playground](https://console.groq.com/playground)

## 🆘 Getting Help

1. Check this guide first
2. Look at validation messages
3. Use Test button for diagnostics
4. Check provider-specific docs
5. Report issues on GitHub

## 🎉 You're Ready!

Once configured and tested:
1. Save your settings
2. Run manual import to test
3. Enable automatic import
4. Enjoy AI-powered music discovery!

Remember: **Always click Test after configuration!**