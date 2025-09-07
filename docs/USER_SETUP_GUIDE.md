# User Setup Guide (Clean)

> Compatibility
> Requires Lidarr 2.14.1.4716+ on the plugins/nightly branch (Settings > General > Updates > Branch = nightly).

## Quick Start

### 1) Choose Your Provider

| Provider   | Privacy        | Cost     | Speed        | Best For               |
|------------|----------------|----------|--------------|------------------------|
| Ollama     | 100% Private   | Free     | Medium       | Privacy-focused users  |
| LM Studio  | 100% Private   | Free     | Medium       | GUI users              |
| OpenRouter | Cloud          | Varies   | Varies       | Trying many models     |
| DeepSeek   | Cloud          | Ultra-low| Fast         | Budget users           |
| Gemini     | Cloud          | Free tier| Fast         | Getting started        |
| Groq       | Cloud          | Low      | Ultra-fast   | Speed priority         |

### 2) Basic Configuration (in Lidarr)

1. Settings > Import Lists > Add > Brainarr
2. Name: AI Music Recommendations
3. Enable Automatic Add: Yes
4. Monitor: All Albums
5. Root Folder: /music
6. Quality Profile: Any
7. Metadata Profile: Standard
8. Tags: ai-recommendations

## Provider Setup

### Ollama (Local, Private)

1. Install: Linux/macOS `curl -fsSL https://ollama.com/install.sh | sh`; Windows: download from https://ollama.com/download
2. Pull a model:
   - `ollama pull qwen2.5` (recommended)
   - `ollama pull llama3`
   - `ollama pull mistral`
3. Configure in Brainarr:
   - Provider: Ollama
   - URL: `http://localhost:11434`
   - Click Test to populate models
   - Select model from dropdown

Tips:
- Ensure `ollama serve` is running
- Click Test after any change

### LM Studio (Local with GUI)

1. Install from https://lmstudio.ai
2. Load a model (Qwen 3, Llama 3 8B, etc.)
3. Start Local Server (Developer > Local Server)
4. Configure in Brainarr:
   - Provider: LM Studio
   - URL: `http://localhost:1234`
   - Click Test, then pick a model

### OpenRouter (Gateway)

1. Get API key: https://openrouter.ai/keys
2. Configure in Brainarr:
   - Provider: OpenRouter
   - API Key: paste key
   - Model: pick a route (DeepSeek V3, Claude 3.5, GPT‑4o, etc.)
   - Click Test

### DeepSeek (Ultra Cost-Effective)

1. Get API key: https://platform.deepseek.com
2. Configure in Brainarr:
   - Provider: DeepSeek
   - API Key: paste key
   - Model: `deepseek-chat`
   - Click Test

### Google Gemini (Free Tier)

1. Get API key: https://aistudio.google.com/apikey
2. Configure in Brainarr:
   - Provider: Google Gemini
   - API Key: paste key
   - Model: Flash (fast) or Pro (more capable)
   - Click Test

### Groq (Ultra-Fast)

1. Get API key: https://console.groq.com/keys
2. Configure in Brainarr:
   - Provider: Groq
   - API Key: paste key
   - Model: Llama 3.3 70B (recommended)
   - Click Test

## Discovery Settings

- Number of Recommendations: Default 20; start with 5–10; up to 50
- Discovery Mode: Similar (safe) / Adjacent (balanced) / Exploratory (broad)

## Testing Your Setup

The Test button:
- Verifies connectivity and keys
- Detects local models
- Shows errors clearly

## Troubleshooting

- No models (local):
  - Ollama: `curl http://localhost:11434/api/tags`
  - LM Studio: `curl http://localhost:1234/v1/models`
- No recommendations: ensure at least 10 artists in your library; verify provider is running; re‑Test
- Logs:
  - Docker: `docker logs <lidarr> | grep -i "brainarr\|plugin"`
  - Windows: `C:\\ProgramData\\Lidarr\\logs\\lidarr.txt`
  - Linux: `journalctl -u lidarr -e --no-pager | grep -i brainarr`

## Advanced

For detailed tuning (Recommendation Modes, Sampling Strategy, Backfill Strategy, Thinking Mode, timeouts, rate limiting, caching), see: wiki-content/Advanced-Settings.md
