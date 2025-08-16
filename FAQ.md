# 🙋 Frequently Asked Questions (FAQ)

## 📋 Table of Contents

- [🚀 Getting Started](#-getting-started)
- [🔧 Installation & Setup](#-installation--setup)
- [🤖 AI Providers](#-ai-providers)
- [🎵 Recommendations](#-recommendations)
- [💰 Cost & Privacy](#-cost--privacy)
- [🔍 Troubleshooting](#-troubleshooting)
- [🛠️ Advanced Usage](#️-advanced-usage)

---

## 🚀 Getting Started

### Q: What is Brainarr and what does it do?
**A:** Brainarr is an AI-powered import list plugin for Lidarr that generates intelligent music recommendations based on your existing music library. It uses AI models to analyze your taste and suggest new artists/albums you might enjoy.

### Q: Is Brainarr free to use?
**A:** Yes! Brainarr itself is completely free and open-source. However, some AI providers (like OpenAI) charge for API usage. We recommend starting with **Ollama** (100% free and private) or **Google Gemini** (has a free tier).

### Q: What's the difference between local and cloud AI providers?
**A:** 
- **Local providers** (Ollama, LM Studio) run AI models on your own hardware - 100% private, no data leaves your network, completely free
- **Cloud providers** (OpenAI, Anthropic, etc.) use remote servers - faster setup but require API keys and may have costs

### Q: How many recommendations can I get?
**A:** You can configure anywhere from 5-50 recommendations per sync. We recommend starting with 10-15 to see how well they match your taste.

---

## 🔧 Installation & Setup

### Q: Which Lidarr versions are supported?
**A:** Brainarr requires Lidarr 4.0.0 or higher. It's tested with the latest Lidarr releases.

### Q: I don't see Brainarr in my Import Lists dropdown. What's wrong?
**A:** This usually means the plugin wasn't installed correctly:

1. **Check the plugin directory:**
   - Windows: `C:\ProgramData\Lidarr\plugins\Brainarr\`
   - Linux: `/var/lib/lidarr/plugins/Brainarr/`
   - Docker: `/config/plugins/Brainarr/`

2. **Verify files are present:**
   ```bash
   # Should see these files:
   Lidarr.Plugin.Brainarr.dll
   plugin.json
   # Plus dependencies (NLog.dll, etc.)
   ```

3. **Check permissions (Linux):**
   ```bash
   sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins/Brainarr/
   chmod 644 /var/lib/lidarr/plugins/Brainarr/*
   ```

4. **Restart Lidarr completely** and check logs for errors

### Q: What's the easiest way to get started?
**A:** Follow this 5-minute setup:

1. **Install Ollama** (free, private):
   ```bash
   curl -fsSL https://ollama.ai/install.sh | sh
   ollama pull qwen2.5:latest
   ```

2. **Install Brainarr** in Lidarr plugins directory

3. **Configure in Lidarr:**
   - Settings → Import Lists → Add → Brainarr
   - Provider: Ollama
   - URL: `http://localhost:11434`
   - Click "Test" to auto-detect models
   - Save

### Q: Do I need to configure anything else in Lidarr?
**A:** Make sure you have:
- A **Root Folder** set for music storage
- A **Quality Profile** configured
- **Automatic Import** enabled if you want hands-off operation

---

## 🤖 AI Providers

### Q: Which AI provider should I choose?
**A:** Depends on your priorities:

| Priority | Recommended Provider | Why |
|----------|---------------------|-----|
| **Privacy** | Ollama | 100% local, no data leaves your network |
| **Free/Budget** | Ollama or Gemini | Ollama is completely free, Gemini has free tier |
| **Quality** | OpenAI GPT-4o or Anthropic Claude | Best recommendation quality |
| **Speed** | Groq | 10x faster inference |
| **Convenience** | OpenRouter | Access to 200+ models with one API key |

### Q: How do I set up Ollama?
**A:** 
```bash
# Install Ollama
curl -fsSL https://ollama.ai/install.sh | sh

# Pull a music-capable model (choose one):
ollama pull qwen2.5:latest     # Recommended - great for music
ollama pull llama3.2:latest    # Alternative option
ollama pull phi3:latest        # Smaller, faster

# Verify it's running
ollama list
curl http://localhost:11434/api/tags
```

In Brainarr settings:
- Provider: Ollama
- URL: `http://localhost:11434`
- Model: (will auto-populate after clicking Test)

### Q: How do I get API keys for cloud providers?
**A:** 

**Free Options:**
- **Google Gemini**: [aistudio.google.com/apikey](https://aistudio.google.com/apikey) - Free tier available
- **OpenRouter**: [openrouter.ai/keys](https://openrouter.ai/keys) - Access to multiple models

**Paid Options:**
- **OpenAI**: [platform.openai.com/api-keys](https://platform.openai.com/api-keys)
- **Anthropic**: [console.anthropic.com](https://console.anthropic.com)

### Q: Why does the "Test" button fail?
**A:** Common causes:

**For Ollama:**
- Ollama isn't running: `ollama serve`
- No models installed: `ollama pull qwen2.5:latest`
- Wrong URL: Should be `http://localhost:11434`
- Firewall blocking localhost connections

**For Cloud Providers:**
- Invalid API key format
- API key has no credits/permissions
- Network/firewall blocking outbound HTTPS

---

## 🎵 Recommendations

### Q: Why am I not getting any recommendations?
**A:** Check these common issues:

1. **Library too small**: Need at least 20 artists for good results
2. **Provider not connected**: Click "Test" button - should show success
3. **Cache**: Recommendations are cached for 60 minutes - wait or restart Lidarr
4. **Discovery mode**: Try "Similar" mode first
5. **Check logs**: Look for errors in Lidarr logs

### Q: The recommendations don't match my taste. How can I improve them?
**A:** Try these adjustments:

1. **Change sampling strategy:**
   - **Minimal**: Fast but basic analysis
   - **Balanced**: Good compromise (default)
   - **Comprehensive**: Deep analysis, best quality

2. **Be more specific:**
   ```yaml
   Target Genres: Progressive Rock, Jazz Fusion
   Music Moods: Dark, Experimental
   Music Eras: 70s Classic Rock
   ```

3. **Adjust discovery mode:**
   - **Similar**: Stay close to your current taste
   - **Adjacent**: Explore related genres
   - **Exploratory**: Discover completely new territories

4. **Try a different AI model:**
   - Qwen2.5 (Ollama) - excellent for music
   - GPT-4o (OpenAI) - high quality but expensive
   - Claude 3.5 Sonnet (Anthropic) - great reasoning

### Q: I keep getting the same recommendations. What's wrong?
**A:** This is usually cache-related:

1. **Wait 60 minutes** for cache to expire
2. **Restart Lidarr** to clear cache immediately
3. **Check if artists already exist** in your library (Brainarr filters duplicates)
4. **Increase variety**: Use "Adjacent" or "Exploratory" mode

### Q: Why do I see "Various Artists" in recommendations?
**A:** This was an issue in earlier versions but should be fixed in v1.0.0+:

1. **Update to latest version** if you haven't
2. **Enable MusicBrainz** integration in Lidarr settings
3. **Temporary workaround**: Add "Various Artists" to exclusions

### Q: How often should I run recommendations?
**A:** Recommendations work best when run:
- **Weekly**: Good balance between fresh content and not overwhelming
- **After adding new music**: Let your taste profile update
- **When exploring new genres**: Adjust settings and re-run

---

## 💰 Cost & Privacy

### Q: How much do cloud providers cost?
**A:** Rough estimates per recommendation batch (20 recommendations):

| Provider | Cost per batch | Notes |
|----------|---------------|-------|
| **Ollama** | FREE | 100% local |
| **Gemini** | FREE-$0.01 | Free tier available |
| **DeepSeek** | $0.01-0.02 | 10-20x cheaper than GPT-4 |
| **Groq** | $0.02-0.05 | Very fast |
| **OpenAI GPT-4o-mini** | $0.05-0.10 | Good quality |
| **OpenAI GPT-4o** | $0.50-1.00 | Premium quality |

### Q: How can I minimize costs?
**A:** 

1. **Use local providers**: Ollama is completely free
2. **Enable caching**: Prevents repeat API calls (enabled by default)
3. **Use budget providers**: DeepSeek, Gemini free tier
4. **Reduce frequency**: Weekly instead of daily
5. **Lower recommendations**: 10 instead of 30 per batch
6. **Use minimal sampling**: Faster and cheaper

### Q: Is my music library data private?
**A:** 

**With Local Providers (Ollama, LM Studio):**
- ✅ 100% private - no data leaves your network
- ✅ No internet connection required for processing
- ✅ Your music taste stays on your hardware

**With Cloud Providers:**
- ⚠️ Artist/album names are sent to AI providers for analysis
- ⚠️ No file contents or personal data, just music metadata
- ⚠️ Read each provider's privacy policy
- ✅ Brainarr doesn't store or log your library data

### Q: Does Brainarr collect any telemetry?
**A:** No. Brainarr:
- ❌ No usage tracking
- ❌ No data collection
- ❌ No "phone home" functionality
- ✅ Completely open source - you can verify this

---

## 🔍 Troubleshooting

### Q: Brainarr seems slow. How can I speed it up?
**A:** 

1. **Use faster providers**: Groq (cloud) or local Ollama
2. **Reduce sampling**: Use "Minimal" instead of "Comprehensive"
3. **Lower recommendation count**: 10 instead of 30
4. **Check your hardware**: Ollama needs decent CPU/RAM
5. **Use smaller models**: `phi3:mini` instead of larger models

### Q: I get "Connection timeout" errors. What should I do?
**A:** 

**For Ollama:**
```bash
# Check if Ollama is running
curl http://localhost:11434/api/tags

# If not running, start it
ollama serve

# Check model is loaded
ollama list
```

**For Cloud Providers:**
- Check internet connection
- Verify API key is correct
- Check if provider has service issues
- Try a different provider as backup

### Q: Where can I find Brainarr logs?
**A:** 

**In Lidarr logs** (`/var/log/lidarr/lidarr.txt` or Windows equivalent):
```bash
# Search for Brainarr entries
grep -i brainarr /var/log/lidarr/lidarr.txt | tail -20

# Look for errors
grep -i "brainarr.*error" /var/log/lidarr/lidarr.txt
```

**Enable debug logging** in Lidarr:
- Settings → General → Log Level: Debug
- This provides much more detailed information

### Q: Can I use multiple AI providers?
**A:** Currently, Brainarr uses one primary provider per configuration. However, you can:
- Set up multiple Brainarr import lists with different providers
- Use OpenRouter to access multiple models with one API key
- The automatic failover will use backup providers if primary fails

---

## 🛠️ Advanced Usage

### Q: Can I customize the AI prompts?
**A:** The prompts are built-in and optimized for music recommendations. However, you can influence them by:
- Setting specific **Target Genres** 
- Defining **Music Moods** (Dark, Upbeat, Experimental, etc.)
- Specifying **Music Eras** (80s New Wave, 70s Progressive Rock, etc.)
- Choosing different **Discovery Modes**

### Q: How do I contribute to Brainarr?
**A:** We welcome contributions! See our [Contributing Guide](CONTRIBUTING.md):

1. **Report bugs**: Use GitHub Issues
2. **Suggest features**: GitHub Discussions
3. **Add AI providers**: Follow the provider template
4. **Improve documentation**: Always appreciated
5. **Test new releases**: Help us find issues

### Q: Can I run Brainarr in Docker?
**A:** Yes! It works in Docker containers:

```bash
# For Lidarr in Docker, mount plugins directory
docker run -v /host/path/plugins:/config/plugins lidarr

# For Ollama in Docker (if using local AI)
docker run -d -p 11434:11434 ollama/ollama
docker exec -it ollama ollama pull qwen2.5:latest
```

### Q: What if my favorite AI provider isn't supported?
**A:** 

1. **Check if it's compatible** with OpenAI API format - many providers are
2. **Use OpenRouter** - provides access to 200+ models
3. **Request it**: Open a GitHub Issue with the provider details
4. **Contribute it**: Follow our provider development guide

### Q: How do I backup my Brainarr configuration?
**A:** Your configuration is stored in Lidarr's database. Back up:
- **Lidarr database**: Usually `lidarr.db` 
- **Lidarr config directory**: Contains all your settings
- **Export settings**: Use Lidarr's backup/restore functionality

---

## 🆘 Still Need Help?

### 📚 Additional Resources

| Resource | Purpose | Link |
|----------|---------|------|
| 🚀 **Quick Start** | 5-minute setup guide | [QUICKSTART.md](QUICKSTART.md) |
| 🔧 **Troubleshooting** | Detailed problem solving | [TROUBLESHOOTING.md](TROUBLESHOOTING.md) |
| 📖 **User Guide** | Complete setup guide | [docs/USER_SETUP_GUIDE.md](docs/USER_SETUP_GUIDE.md) |
| 🎯 **Provider Guide** | AI provider comparison | [docs/PROVIDER_GUIDE.md](docs/PROVIDER_GUIDE.md) |

### 💬 Community Support

- **🐛 Bug Reports**: [GitHub Issues](https://github.com/yourusername/brainarr/issues)
- **💡 Feature Requests**: [GitHub Discussions](https://github.com/yourusername/brainarr/discussions)
- **❓ Questions**: [GitHub Discussions Q&A](https://github.com/yourusername/brainarr/discussions/categories/q-a)

### 🏷️ When Reporting Issues

Please include:
- **Brainarr version**: Found in plugin.json
- **Lidarr version**: Help → About
- **Provider being used**: Ollama, OpenAI, etc.
- **Error messages**: From Lidarr logs
- **Configuration**: Screenshots of settings (hide API keys!)

---

**📝 Last Updated**: January 2025  
**🔄 This FAQ is regularly updated based on community questions and feedback**