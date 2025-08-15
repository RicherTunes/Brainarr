# 🚀 Brainarr Quick Start Guide

Get Brainarr up and running in 5 minutes with this step-by-step guide!

## 📋 Table of Contents
1. [Choose Your AI Provider](#1-choose-your-ai-provider)
2. [Install Brainarr](#2-install-brainarr)
3. [Configure in Lidarr](#3-configure-in-lidarr)
4. [Test Your Setup](#4-test-your-setup)
5. [Your First Recommendations](#5-your-first-recommendations)

---

## 1. Choose Your AI Provider

### 🏆 Recommended Options

#### Option A: Ollama (FREE, Private, No Signup)
**Best for**: Privacy-conscious users, unlimited free usage
```bash
# Install Ollama (Linux/Mac)
curl -fsSL https://ollama.ai/install.sh | sh

# Install Ollama (Windows)
# Download from: https://ollama.ai/download

# Pull a music-capable model
ollama pull qwen2.5:latest
# or
ollama pull llama3.2:latest
```
✅ **Ready!** Ollama runs at `http://localhost:11434`

#### Option B: Google Gemini (FREE Tier Available)
**Best for**: Cloud convenience with free tier
1. Go to https://aistudio.google.com/apikey
2. Click "Create API Key"
3. Copy your key (starts with `AIza...`)

#### Option C: OpenAI (High Quality, Paid)
**Best for**: Best quality recommendations
1. Go to https://platform.openai.com/api-keys
2. Create an API key
3. Add $5 credit to your account

---

## 2. Install Brainarr

### Windows Installation
```powershell
# Download the latest release
Invoke-WebRequest -Uri "https://github.com/yourusername/brainarr/releases/latest/download/Brainarr.Plugin.dll" -OutFile "Brainarr.Plugin.dll"

# Copy to Lidarr plugins
Copy-Item "Brainarr.Plugin.dll" -Destination "C:\ProgramData\Lidarr\plugins\"

# Restart Lidarr
Restart-Service Lidarr
```

### Linux/Docker Installation
```bash
# Download the latest release
wget https://github.com/yourusername/brainarr/releases/latest/download/Brainarr.Plugin.dll

# Copy to Lidarr plugins
sudo cp Brainarr.Plugin.dll /var/lib/lidarr/plugins/
# or for Docker
docker cp Brainarr.Plugin.dll lidarr:/config/plugins/

# Restart Lidarr
sudo systemctl restart lidarr
# or for Docker
docker restart lidarr
```

---

## 3. Configure in Lidarr

### Step 1: Add Brainarr Import List
1. Open Lidarr web interface
2. Go to **Settings** → **Import Lists**
3. Click the **+** button
4. Select **Brainarr AI Music Discovery**

### Step 2: Basic Configuration
Fill in these essential fields:

```yaml
Name: AI Music Discovery
Enable Automatic Add: Yes
Monitor: All Albums
Root Folder: /your/music/folder
Quality Profile: (your preferred quality)
```

### Step 3: Provider Configuration

#### For Ollama (Local)
```yaml
AI Provider: Ollama
Ollama URL: http://localhost:11434
Model: qwen2.5:latest  # (or click Test to auto-detect)
```

#### For Google Gemini (Cloud)
```yaml
AI Provider: Gemini
Gemini API Key: AIza... (your key)
Model: gemini-1.5-flash
```

#### For OpenAI (Cloud)
```yaml
AI Provider: OpenAI
OpenAI API Key: sk-... (your key)
Model: gpt-4o-mini
```

### Step 4: Discovery Settings
Start with these beginner-friendly settings:

```yaml
Discovery Mode: Similar  # Stay close to your taste
Recommendation Type: Artists  # Discover new artists
Sampling Strategy: Balanced  # Good balance of speed/quality
Max Recommendations: 10  # Start small
```

---

## 4. Test Your Setup

### The Magic Test Button! 🎯

1. Click the **Test** button in Brainarr settings
2. Wait 5-10 seconds
3. Look for these success indicators:

#### ✅ Success Messages:
```
✅ Connected successfully to [Provider Name]
✅ Found 5 Ollama models: qwen2.5, llama3.2...
🎯 Recommended: Copy one of these models into the field above
Test successful: Connected to [Provider Name]
```

#### ❌ If Test Fails:

**"Cannot connect to provider"**
- Ollama: Check it's running with `ollama list`
- Cloud: Verify your API key is correct

**"No models found"**
- Ollama: Run `ollama pull qwen2.5:latest`
- Cloud: Your API key might not have permissions

---

## 5. Your First Recommendations

### Manual Test Run
1. Save your Brainarr configuration
2. Go to **System** → **Tasks**
3. Find "Import List Sync"
4. Click **Run** (▶️ button)
5. Wait 30-60 seconds
6. Check **Activity** → **Queue** for new artists!

### What to Expect

#### First Run (10 recommendations, Similar mode):
```
Based on your library containing:
- Pink Floyd, Led Zeppelin, The Beatles

Brainarr might recommend:
- King Crimson (Progressive rock like Pink Floyd)
- Deep Purple (Classic rock like Led Zeppelin)  
- The Kinks (British invasion like The Beatles)
- Yes (Progressive rock)
- Cream (Blues rock)
```

### Understanding Recommendations

Each recommendation includes:
- **Artist**: The recommended artist
- **Album**: A representative album
- **Confidence**: 0.7-1.0 (higher = better match)
- **Reason**: Why this was recommended

---

## 📊 Quick Settings Reference

### For Different Use Cases

#### 🎯 "I want exact matches to my taste"
```yaml
Discovery Mode: Similar
Sampling Strategy: Comprehensive
Max Recommendations: 15
```

#### 🌟 "I want to explore a bit"
```yaml
Discovery Mode: Adjacent
Sampling Strategy: Balanced
Max Recommendations: 20
Target Genres: (leave empty for auto)
```

#### 🚀 "I want musical adventure!"
```yaml
Discovery Mode: Exploratory
Sampling Strategy: Comprehensive
Max Recommendations: 30
Music Moods: Experimental, Dark
```

#### 💰 "I want to minimize costs"
```yaml
Provider: Ollama (free) or Gemini (free tier)
Sampling Strategy: Minimal
Max Recommendations: 10
Cache Duration: 120 minutes
```

---

## 🎉 Success Tips

### Do's ✅
- **Start small**: 10 recommendations, Similar mode
- **Test first**: Always use Test button before saving
- **Check library size**: Need 20+ artists for good results
- **Use moods/eras**: For targeted discovery
- **Monitor costs**: Check API usage for cloud providers

### Don'ts ❌
- Don't set Max Recommendations > 30 initially
- Don't use Exploratory mode with < 50 artists
- Don't disable caching (wastes API calls)
- Don't forget to set Root Folder
- Don't skip the Test button!

---

## 🆘 Quick Troubleshooting

| Problem | Solution |
|---------|----------|
| "No artists added" | Check Root Folder and Quality Profile |
| "Same recommendations" | Clear cache or wait 60 minutes |
| "High API costs" | Switch to Ollama or reduce recommendations |
| "Poor matches" | Switch to Comprehensive sampling |
| "Various Artists appears" | Update to latest Brainarr version |

---

## 🎊 Congratulations!

You now have AI-powered music discovery in Lidarr! 

### Next Steps:
1. Let it run for a week and see what you discover
2. Adjust Discovery Mode based on results
3. Try different Music Moods for variety
4. Join our community for tips and support

### Getting Help:
- 📖 Full documentation: [README.md](README.md)
- 🐛 Report issues: [GitHub Issues](https://github.com/yourusername/brainarr/issues)
- 💬 Community: [GitHub Discussions](https://github.com/yourusername/brainarr/discussions)

---

**Happy Discovering! 🎵**