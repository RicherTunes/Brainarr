# 🔧 Brainarr Troubleshooting Guide

This guide covers all common issues and their solutions. If your issue isn't listed here, please check the [GitHub Issues](https://github.com/yourusername/brainarr/issues).

## 📑 Table of Contents

1. [Installation Issues](#installation-issues)
2. [Connection Problems](#connection-problems)
3. [No Recommendations Generated](#no-recommendations-generated)
4. [Poor Quality Recommendations](#poor-quality-recommendations)
5. [Performance Issues](#performance-issues)
6. [Cost Management](#cost-management)
7. [Error Messages](#error-messages)
8. [Advanced Debugging](#advanced-debugging)

---

## Installation Issues

### ❌ "Brainarr not showing in Import Lists"

**Symptoms:**
- Brainarr doesn't appear in the Import Lists dropdown
- Other import lists work fine

**Solutions:**
1. **Check plugin location:**
   ```bash
   # Linux
   ls -la /var/lib/lidarr/plugins/
   
   # Windows
   dir C:\ProgramData\Lidarr\plugins\
   
   # Docker
   docker exec lidarr ls -la /config/plugins/
   ```

2. **Verify .NET version:**
   ```bash
   dotnet --version
   # Should be 6.0 or higher
   ```

3. **Check Lidarr logs:**
   ```bash
   tail -100 /var/log/lidarr/lidarr.txt | grep -i plugin
   ```

4. **Restart Lidarr completely:**
   ```bash
   sudo systemctl stop lidarr
   sudo systemctl start lidarr
   ```

### ❌ "Plugin failed to load"

**Check file permissions:**
```bash
# Linux
chmod 644 /var/lib/lidarr/plugins/Brainarr.Plugin.dll
chown lidarr:lidarr /var/lib/lidarr/plugins/Brainarr.Plugin.dll
```

---

## Connection Problems

### ❌ "Cannot connect to Ollama"

**Symptoms:**
- Test fails with "Cannot connect to provider"
- Ollama is installed but not detected

**Solutions:**

1. **Verify Ollama is running:**
   ```bash
   # Check if Ollama is running
   ollama list
   
   # If not running, start it
   ollama serve
   ```

2. **Check the correct URL:**
   ```bash
   # Test connection
   curl http://localhost:11434/api/tags
   ```

3. **For Docker users:**
   ```yaml
   # Use host networking or proper container name
   Ollama URL: http://host.docker.internal:11434
   # or
   Ollama URL: http://ollama:11434
   ```

4. **Check firewall:**
   ```bash
   # Allow local connections
   sudo ufw allow from 127.0.0.1 to any port 11434
   ```

### ❌ "Cannot connect to LM Studio"

**Solutions:**

1. **Ensure server is started:**
   - Open LM Studio
   - Go to "Local Server" tab
   - Click "Start Server"
   - Verify it shows "Server Running on port 1234"

2. **Load a model:**
   - Models tab → Download a model
   - Select the model
   - Click "Load" button

3. **Test connection:**
   ```bash
   curl http://localhost:1234/v1/models
   ```

### ❌ "API Key Invalid" (Cloud Providers)

**For OpenAI:**
```bash
# Test your API key
curl https://api.openai.com/v1/models \
  -H "Authorization: Bearer YOUR_API_KEY"
```

**For Anthropic:**
```bash
curl https://api.anthropic.com/v1/messages \
  -H "x-api-key: YOUR_API_KEY" \
  -H "anthropic-version: 2023-06-01"
```

**For Google Gemini:**
```bash
curl "https://generativelanguage.googleapis.com/v1beta/models?key=YOUR_API_KEY"
```

---

## No Recommendations Generated

### ❌ "Empty recommendation list"

**Diagnostic Steps:**

1. **Check library size:**
   ```sql
   # Minimum requirements:
   - At least 20 artists
   - At least 50 albums
   - Multiple genres represented
   ```

2. **Verify provider is working:**
   - Click "Test" button
   - Should show "✅ Connected successfully"
   - Should list available models

3. **Check cache:**
   - Recommendations are cached for 60 minutes
   - Clear cache by waiting or restarting Lidarr

4. **Review settings:**
   ```yaml
   # Ensure these are set:
   Max Recommendations: 10 or higher
   Discovery Mode: Similar (for testing)
   Sampling Strategy: Balanced or Comprehensive
   ```

5. **Check logs for errors:**
   ```bash
   grep -i "brainarr\|recommendation" /var/log/lidarr/lidarr.txt | tail -50
   ```

### ❌ "Various Artists keeps appearing"

**This is fixed in v1.0.0+**, but if it persists:

1. **Enable MusicBrainz integration:**
   ```yaml
   # In Lidarr Settings → Metadata
   Enable MusicBrainz: Yes
   ```

2. **Check recommendation logs:**
   ```bash
   grep "Various Artists" /var/log/lidarr/lidarr.txt
   ```

3. **Temporary workaround:**
   - Add "Various Artists" to your exclusion list
   - Settings → Import Lists → Brainarr → Exclusions

---

## Poor Quality Recommendations

### ❌ "Recommendations don't match my taste"

**Improve recommendation quality:**

1. **Switch to Comprehensive sampling:**
   ```yaml
   Sampling Strategy: Comprehensive
   # Provides 20,000 tokens of context
   ```

2. **Use Music DJ Expert mode:**
   - Automatically activated when using Comprehensive mode
   - Or when specifying Moods/Eras/Genres

3. **Be specific with preferences:**
   ```yaml
   Target Genres: Progressive Rock, Psychedelic Rock
   Music Moods: Dark, Experimental
   Music Eras: 70s Classic Rock
   ```

4. **Ensure adequate library size:**
   - < 50 artists: Use "Similar" mode
   - 50-200 artists: Use "Adjacent" mode
   - 200+ artists: Can use "Exploratory" mode

5. **Try a different AI model:**
   ```yaml
   # Better models for music:
   Ollama: qwen2.5:latest (best for music)
   OpenAI: gpt-4o (not mini)
   Anthropic: claude-3-5-sonnet (not haiku)
   ```

### ❌ "Same artists recommended repeatedly"

**Solutions:**

1. **Clear the cache:**
   ```bash
   # Restart Lidarr to clear all caches
   sudo systemctl restart lidarr
   ```

2. **Check if artists already exist:**
   ```yaml
   # The plugin should filter these, but verify:
   - Go to Artist index
   - Search for recommended artists
   - They shouldn't already be in your library
   ```

3. **Increase variety:**
   ```yaml
   Discovery Mode: Adjacent or Exploratory
   Max Recommendations: 30
   ```

---

## Performance Issues

### ❌ "Recommendations take too long"

**Speed optimizations:**

1. **Use faster providers:**
   ```yaml
   # Fastest to slowest:
   1. Groq (ultra-fast)
   2. Ollama (local, no network)
   3. Gemini Flash
   4. OpenAI GPT-4o-mini
   5. Others
   ```

2. **Reduce token usage:**
   ```yaml
   Sampling Strategy: Minimal (8,000 tokens)
   Max Recommendations: 10
   ```

3. **Use caching effectively:**
   ```yaml
   # Already enabled by default
   Cache Duration: 60 minutes
   ```

### ❌ "High memory usage"

**For Ollama:**
```bash
# Check model size
ollama list

# Use smaller models:
ollama pull llama3.2:3b  # 3B parameters
ollama pull phi3:mini     # Very small
```

**For Lidarr:**
```yaml
# Reduce concurrent operations
Settings → General → Advanced
Maximum Import List Sync: 1
```

---

## Cost Management

### ❌ "High API costs" (Cloud Providers)

**Cost reduction strategies:**

1. **Switch to cheaper providers:**
   ```yaml
   # From most to least expensive:
   OpenAI GPT-4o > Anthropic Claude > OpenAI GPT-4o-mini > 
   Perplexity > Groq > DeepSeek > Gemini > Ollama (FREE)
   ```

2. **Use budget models:**
   ```yaml
   OpenAI: gpt-4o-mini (10x cheaper than gpt-4o)
   Anthropic: claude-3-5-haiku (cheaper than sonnet)
   Gemini: gemini-1.5-flash (free tier available)
   ```

3. **Optimize settings:**
   ```yaml
   Sampling Strategy: Minimal
   Max Recommendations: 10
   Schedule: Weekly (not daily)
   ```

4. **Monitor usage:**
   ```bash
   # Check logs for token usage
   grep "token usage" /var/log/lidarr/lidarr.txt
   ```

---

## Error Messages

### Common Error Codes

| Error | Meaning | Solution |
|-------|---------|----------|
| `401 Unauthorized` | Invalid API key | Check your API key |
| `429 Too Many Requests` | Rate limited | Wait or upgrade plan |
| `500 Internal Server Error` | Provider issue | Try again later |
| `503 Service Unavailable` | Provider down | Use failover provider |
| `Context length exceeded` | Prompt too long | Use Minimal sampling |
| `Model not found` | Invalid model name | Check available models with Test |

### Provider-Specific Errors

**Ollama:**
```
"model 'llama3' not found"
Solution: ollama pull llama3
```

**OpenAI:**
```
"You exceeded your current quota"
Solution: Add credits to your account
```

**Anthropic:**
```
"credit_balance_insufficient"
Solution: Add credits or switch to cheaper model
```

---

## Advanced Debugging

### Enable Debug Logging

1. **In Lidarr:**
   ```yaml
   Settings → General → Log Level: Debug
   ```

2. **Monitor logs in real-time:**
   ```bash
   tail -f /var/log/lidarr/lidarr.txt | grep -i brainarr
   ```

### Test Provider Directly

**Create a test script:**
```bash
#!/bin/bash
# test_provider.sh

# For Ollama
curl http://localhost:11434/api/generate \
  -d '{
    "model": "qwen2.5",
    "prompt": "Recommend one album similar to Pink Floyd",
    "stream": false
  }'

# For OpenAI
curl https://api.openai.com/v1/chat/completions \
  -H "Authorization: Bearer YOUR_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gpt-4o-mini",
    "messages": [{"role": "user", "content": "Recommend one album"}]
  }'
```

### Database Checks

**Verify Brainarr in database:**
```sql
# SQLite
sqlite3 /var/lib/lidarr/lidarr.db
SELECT * FROM ImportLists WHERE Implementation = 'Brainarr';
```

### Memory Profiling

**Check resource usage:**
```bash
# Overall Lidarr memory
ps aux | grep -i lidarr

# During recommendation fetch
top -p $(pgrep lidarr)
```

---

## Still Having Issues?

### Before Reporting a Bug

1. **Update to latest version**
2. **Check existing issues:** [GitHub Issues](https://github.com/yourusername/brainarr/issues)
3. **Collect information:**
   - Brainarr version
   - Lidarr version
   - Provider being used
   - Error messages from logs
   - Settings configuration

### Getting Help

1. **GitHub Discussions:** [Community Support](https://github.com/yourusername/brainarr/discussions)
2. **Issue Tracker:** [Report Bugs](https://github.com/yourusername/brainarr/issues/new)
3. **Discord:** [Join our Discord](https://discord.gg/brainarr)

### Providing Useful Bug Reports

**Good bug report includes:**
```markdown
**Version:** Brainarr 1.0.0, Lidarr 1.3.2.3  
**Provider:** Ollama with qwen2.5
**Issue:** No recommendations generated
**Error:** "Empty response from provider"
**Logs:** [attached]
**Settings:** [screenshot]
```

---

**Remember:** Most issues are configuration-related. Double-check your settings and test your provider connection first!