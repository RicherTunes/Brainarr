# 🔧 Troubleshooting Guide

Comprehensive troubleshooting guide based on Brainarr's actual error handling and common issues from the codebase.

## 🚨 **Common Error Messages**

### **Provider Connection Issues**

#### **"Connection timeout" / "Provider unavailable"**

**Symptoms:**
- Test button fails with timeout error
- Manual import gets stuck or fails
- Health check shows provider as unhealthy

**Root Causes & Solutions:**

**For Local Providers (Ollama/LM Studio):**
```bash
# Check if service is running
curl http://localhost:11434/api/tags          # Ollama
curl http://localhost:1234/v1/models          # LM Studio

# Start services if needed
systemctl start ollama                        # Linux Ollama
brew services start ollama                    # macOS Ollama
# Or restart LM Studio application
```

**For Cloud Providers:**
```bash
# Test connectivity
curl -H "Authorization: Bearer YOUR_API_KEY" https://api.openai.com/v1/models

# Check DNS resolution
nslookup api.openai.com
ping api.openai.com

# Verify firewall/proxy settings
curl -v https://api.anthropic.com/v1/messages
```

**Network Solutions:**
```bash
# Configure proxy if needed (Linux)
export https_proxy=http://proxy.example.com:8080
export http_proxy=http://proxy.example.com:8080

# Windows proxy
netsh winhttp set proxy proxy.example.com:8080

# Docker network fix
docker run --network host ...  # For local providers
```

### ✅ Cross‑Provider Parity Checklist

Use this quick checklist to confirm any provider behaves like LM Studio/Ollama in Brainarr:

- Connectivity: Send a minimal “Reply with OK” chat request
  - OpenAI/OpenRouter/Groq/DeepSeek/Perplexity: POST chat/completions with max_tokens=5
  - Anthropic: POST /v1/messages with x-api-key + anthropic-version
  - Gemini: POST generateContent with one short text part
- Auth header shape: Bearer <key> (Anthropic: x-api-key; Gemini: ?key=)
- JSON output format: either a JSON array or an object with `recommendations` array
  - Brainarr also extracts arrays embedded in text responses
- Error handling: Non-OK HTTP => empty result (logged), not a hard crash
- Model selection: Verify availability and exact model string mapping below
- Rate limiting: If 429 appears, reduce frequency or switch provider/model
- Encoding/BOM: Brainarr strips BOM; ensure proxies don’t alter JSON

---

#### **"Invalid API key" / "Authentication failed"**

**For Each Provider:**

**OpenAI**: `sk-proj-...` (new format) or `sk-...` (legacy)
```bash
# Test key validity
curl -H "Authorization: Bearer sk-..." https://api.openai.com/v1/models
```

**Anthropic**: `sk-ant-api03-...` format required
```bash
# Test Anthropic key
curl -H "x-api-key: sk-ant-..." https://api.anthropic.com/v1/messages
```

**Gemini**: `AIza...` format
```bash
# Test Gemini key
curl "https://generativelanguage.googleapis.com/v1beta/models?key=AIza..."
```

**DeepSeek**: `sk-...` format
```bash  
# Test DeepSeek key
curl -H "Authorization: Bearer sk-..." https://api.deepseek.com/v1/models
```

**Groq**: `gsk_...` format  
```bash
# Test Groq key
curl -H "Authorization: Bearer gsk_..." https://api.groq.com/openai/v1/models
```

**Common Key Issues:**
- **Expired Keys**: Regenerate in provider dashboard
- **Usage Limits**: Check quota/billing status
- **Wrong Format**: Verify key follows provider's format
- **Scope Restrictions**: Ensure key has chat/completions permissions

---

### **Model-Related Errors**

#### **"Model not found" / "Model not available"**

**For Ollama:**
```bash
# List available models
ollama list

# Download missing model
ollama pull qwen2.5:latest
ollama pull llama3.1:8b

# Check model status
ollama show qwen2.5:latest
```

**For Cloud Providers:**
Check model availability:
```bash
# OpenAI models
curl -H "Authorization: Bearer $OPENAI_KEY" https://api.openai.com/v1/models

# Anthropic - use exact model names from Brainarr
# claude-3-5-haiku-latest, claude-3-5-sonnet-latest, claude-3-opus-20240229

# Gemini - verify model name format
# gemini-1.5-flash, gemini-1.5-pro, gemini-2.0-flash-exp
```

**Model Name Mapping (from code):**
```
OpenAI:    gpt-4o-mini, gpt-4o, gpt-4-turbo, gpt-3.5-turbo
Anthropic: claude-3-5-haiku-latest, claude-3-5-sonnet-latest, claude-3-opus-20240229
Gemini:    gemini-1.5-flash, gemini-1.5-pro, gemini-2.0-flash-exp
Groq:      llama-3.3-70b-versatile, llama-3.2-90b-vision-preview, mixtral-8x7b-32768
DeepSeek:  deepseek-chat, deepseek-coder, deepseek-reasoner
```

### 🧪 Verifying Provider Responses (JSON Formatting)

Brainarr expects structured recommendations. Accepted shapes:

- Pure array (preferred)
  - `[{"artist":"…","album":"…","genre":"…","confidence":0.9,"reason":"…"}]`
- Object with `recommendations` array
  - `{ "recommendations": [ { … } ] }`
- Array embedded in narrative text
  - Brainarr extracts the first JSON array and parses it

If a provider replies with narrative-only text or invalid JSON, Brainarr returns an empty list and logs a warning. Try:
- Using models better at structured output (gpt‑4o‑mini, claude‑3.5‑haiku, gemini‑1.5‑flash)
- Lowering temperature to 0.6–0.8
- Enforcing “JSON array only” in advanced prompt

Minimum fields parsed per item:
- artist (required)
- album (optional for artist‑only flows)
- genre (defaults to “Unknown” if missing)
- confidence (numeric or numeric string; defaults if missing)
- reason (optional)

---

### **Recommendation Generation Issues**

#### **"No recommendations generated" / Empty Results**

**Diagnostic Steps:**

**1. Check Library Size:**
```bash
# Minimum library requirements
# At least 10 albums across 5+ artists for meaningful analysis
```

**2. Verify Provider Response:**
Enable debug logging and check for:
- API rate limiting (HTTP 429)
- Quota exceeded (HTTP 402/403)  
- Service unavailable (HTTP 503)
- Invalid JSON response format

**3. Check Brainarr Processing:**
```
Log Pattern: "Generated X unique recommendations"
- If X = 0: Provider returned empty/invalid response
- If missing: Request failed before processing
```

**Solutions by Cause:**

**Empty Library:**
- Add more music to Lidarr first
- Use "Similar" discovery mode with small libraries
- Reduce max recommendations to 5-10

**Rate Limiting:**
- Reduce request frequency in Import Lists settings
- Switch to provider with higher limits
- Configure multiple providers for load balancing

**Invalid Responses:**
- Try different model (some models better for structured output)
- Switch provider temporarily  
- Check provider service status pages

---

#### **"Rate limit exceeded" / HTTP 429 Errors**

**Understanding Rate Limits (from codebase):**

**Default Limits:**
- **Local Providers**: 30 requests/minute (no external limits)
- **Cloud Providers**: 10 requests/minute (conservative default)
- **Burst Size**: 5 requests initially, then rate limiting kicks in

**Solutions:**

**Immediate:**
```bash
# Wait for rate limit reset (usually 1 minute)
# Then retry manual import
```

**Sustainable:**
- Reduce Import List refresh frequency
- Use lighter/cheaper models for periodic runs; premium models for on‑demand
- Configure multiple providers and allow fallback
- Check provider quotas/billing and upgrade if needed

---

### 🧰 Provider‑Specific Quick Checks

LM Studio
- Ensure app is running; list models at http://localhost:1234/v1/models
- If responses are narrative only, enforce JSON array in prompts

Ollama
- Ensure model is pulled/loaded: `ollama list` / `ollama pull <model>`
- Warm up model or increase timeout slightly for cold starts

OpenAI/OpenRouter/Groq/DeepSeek/Perplexity (OpenAI‑compatible)
- Verify Authorization header and model name
- Arrays embedded in text are extracted automatically

Anthropic
- Include `anthropic-version` header; verify exact Claude model name
- If content is blocked, try Haiku variant and lower temperature

Gemini
- `responseMimeType` of JSON preferred (Brainarr can parse text parts)
- If safety blocks trigger, adjust phrasing or use safer models

---

### 🪵 Logging & Security Notes

- Logs scrub API keys, emails, IPs, credit cards, and common sensitive patterns
- Enable debug logging only during local troubleshooting; avoid sharing raw payloads
- Never post secrets or API keys to logs or issues


**Long-term Configuration:**
1. **Settings** → **Import Lists** → **Brainarr** → **Advanced**
2. **Refresh Interval**: Increase to 12-24 hours
3. **Multiple Providers**: Configure failover to different provider

**Provider-Specific Limits:**
```
OpenAI:     Varies by plan (typically 40,000 TPM for free tier)
Anthropic:  15,000 tokens/minute on free tier
Gemini:     15 requests/minute free tier  
Groq:       30 requests/minute free tier
DeepSeek:   Higher limits, very affordable
```

---

## 🧠 **AI-Specific Issues**

### **Hallucination Detection**

**Symptoms:**
- Recommendations include non-existent albums
- Impossible combinations (e.g., "Live Studio Recording")
- Future dates for past artists  
- Nonsensical album titles

**Brainarr's Built-in Protection:**
```
Automatic Detection:
✅ Non-existent album patterns
✅ Contradictory combinations  
✅ Temporal inconsistencies
✅ AI-generated phrases
✅ Excessive descriptors
```

**Manual Solutions:**
1. **Enable Strict Validation**: More aggressive filtering
2. **Switch Provider**: Different AI models have different hallucination rates
3. **Add Custom Filters**: Define patterns to reject
4. **Lower Creativity**: Reduce temperature settings for more conservative results

### **JSON Parsing Errors**

**Error**: "Failed to parse recommendation response"

**Causes & Solutions:**

**Mixed Content Response (DeepSeek/Groq):**
- Brainarr extracts JSON from text automatically
- Update to latest version for improved parsing

**Citation Markers (Perplexity):**
- Automatic removal of `[1]`, `[2]` citation markers
- No user action needed with v1.0.3+

**Malformed JSON:**
```bash
# Enable debug logging to see raw responses
# Check for provider-specific formatting issues
# Try different model if parsing continues to fail
```

---

## 🔍 **Diagnostic Tools**

### **Built-in Health Monitoring**

**Provider Health Status:**
- **Healthy**: ✅ Recent successful operations
- **Degraded**: ⚠️ Some failures but still operational  
- **Unhealthy**: ❌ Multiple consecutive failures

**Health Check Triggers:**
- Automatic every 5 minutes
- Manual via System → Tasks → Check Health
- After every failed operation

### **Log Analysis**

**Key Log Patterns:**

**Successful Flow:**
```
[Info] Starting recommendation generation for library analysis...
[Info] Using provider: [ProviderName] with model: [ModelName]
[Info] Library profile: X albums, Y artists, top genres: [genres]
[Info] Generated X unique recommendations (filtered Y duplicates)
[Info] Cached recommendations with key: brainarr_recs_[hash]
```

**Connection Issues:**
```
[Error] Provider [Name] health check failed: Connection timeout
[Warn] Falling back to secondary provider: [BackupProvider]
[Error] All configured providers are unavailable
```

**Parsing Issues:**
```
[Warn] Failed to parse JSON response, attempting text extraction
[Debug] Raw provider response: [response content]  
[Info] Extracted X recommendations from text response
```

### **Manual Diagnostic Commands**

**Test Provider Connectivity:**
```bash
# Ollama
curl http://localhost:11434/api/tags

# LM Studio  
curl http://localhost:1234/v1/models

# Cloud providers (replace with your key)
curl -H "Authorization: Bearer YOUR_KEY" https://api.openai.com/v1/models
```

**Test Library Analysis:**
```bash
# Check if Lidarr API is accessible to Brainarr
curl http://localhost:8686/api/v1/artist
curl http://localhost:8686/api/v1/album
```

---

## 🛠️ **Advanced Troubleshooting**

### **Memory and Performance Issues**

**High Memory Usage:**
```bash
# Monitor Lidarr process
htop -p $(pgrep Lidarr)

# Reduce cache size in BrainarrSettings
# Lower Max Recommendations  
# Increase refresh interval
```

**Slow Recommendation Generation:**
```bash
# Check AI provider response times in logs
grep "Response time" /var/lib/lidarr/.config/Lidarr/logs/lidarr.txt

# Optimize settings:
# - Use faster models (gpt-4o-mini vs gpt-4o)
# - Reduce max recommendations  
# - Enable caching
# - Use local providers for speed
```

### **Concurrency and Threading Issues**

**Symptoms:**
- Duplicate recommendations despite deduplication
- Cache corruption or inconsistent results
- Random failures under load

**Solutions:**
```bash
# Reduce concurrency in Lidarr settings
# Increase cache duration to reduce simultaneous requests
# Use single provider instead of multiple for testing
# Check for resource conflicts (CPU/memory pressure)
```

### **Docker-Specific Issues**

**Plugin Not Loading in Docker:**
```bash
# Verify docker image supports plugins
docker run --rm ghcr.io/hotio/lidarr:pr-plugins-2.13.3.4692 --version

# Check mounted volumes
docker exec lidarr ls -la /config/plugins/

# Verify permissions
docker exec lidarr ls -la /config/plugins/Brainarr/
```

**Network Connectivity in Docker:**
```bash
# Test from inside container
docker exec lidarr curl http://localhost:11434/api/tags  # Ollama
docker exec lidarr curl https://api.openai.com           # External APIs

# Fix network issues
docker run --network host ...           # For local providers
docker run --add-host=host.docker.internal:host-gateway ...  # For host services
```

---

## 📞 **Getting Help**

### **Before Requesting Support**

**Collect Diagnostic Information:**
1. **Lidarr Version**: Help → About
2. **Brainarr Version**: Settings → General → Plugins  
3. **Error Logs**: System → Logs (last 50 lines)
4. **Configuration**: Provider and model settings (hide API keys)
5. **System Info**: OS, .NET version, hardware specs

### **Log Collection Script**
```bash
#!/bin/bash
echo "=== Brainarr Diagnostic Information ==="
echo "Date: $(date)"
echo "Lidarr Version: $(curl -s localhost:8686/api/v1/system/status | jq -r .version)"
echo "OS: $(uname -a)"
echo "Brainarr Logs (last 20 lines):"
tail -20 /var/lib/lidarr/.config/Lidarr/logs/lidarr.txt | grep -i brainarr
echo "=== End Diagnostic ==="
```

### **Support Channels**
1. **GitHub Issues**: https://github.com/RicherTunes/Brainarr/issues
   - Use for bugs, feature requests, and technical issues
   - Include diagnostic information and logs

2. **GitHub Discussions**: https://github.com/RicherTunes/Brainarr/discussions  
   - Use for general questions and community help
   - Share configuration tips and use cases

3. **Community Forums**: 
   - r/lidarr on Reddit
   - Lidarr Discord community
   - General Lidarr support channels

### **Bug Report Template**

When reporting issues, include:

```markdown
## Environment
- **Lidarr Version**: [version]
- **Brainarr Version**: [version]  
- **Platform**: [Windows/Linux/macOS/Docker]
- **Provider**: [AI provider name]
- **Model**: [specific model name]

## Issue Description
[Clear description of the problem]

## Steps to Reproduce
1. [Step 1]
2. [Step 2]  
3. [Error occurs]

## Expected Behavior
[What should happen]

## Actual Behavior
[What actually happens]

## Logs
```
[Relevant log entries with timestamps]
```

## Configuration
[Settings used, with API keys redacted]
```

---

## 🔄 **Self-Recovery Procedures**

### **Reset Plugin to Defaults**

**Soft Reset (Preserve API Keys):**
1. Settings → Import Lists → Brainarr
2. Reset recommendation count to 20
3. Set Discovery Mode to "Adjacent"
4. Clear custom filters
5. Test connection

**Hard Reset (Clean Slate):**
1. Delete Brainarr import list
2. Restart Lidarr  
3. Re-add Brainarr with fresh configuration
4. Reconfigure provider from scratch

### **Cache and Performance Reset**

**Clear Brainarr Cache:**
```bash
# The cache automatically expires after 60 minutes
# To force immediate reset:
# 1. Change any configuration setting
# 2. Save settings (this invalidates cache)
# 3. Run manual import

# Or restart Lidarr to clear all caches
sudo systemctl restart lidarr
```

**Performance Reset:**
```bash
# Reduce system load
# 1. Lower max recommendations to 10
# 2. Increase refresh interval to 24 hours
# 3. Use faster/smaller AI model
# 4. Enable caching if disabled
```

---

## 📊 **Performance Diagnostic**

### **Response Time Analysis**

**Acceptable Response Times:**
- **Local Providers**: 5-30 seconds (depends on hardware)
- **Groq**: 0.2-2 seconds (ultra-fast)  
- **Gemini**: 1-5 seconds (very fast)
- **Other Cloud**: 3-15 seconds (normal)

**Slow Response Troubleshooting:**
```bash
# Check system resources during request
top -p $(pgrep Lidarr)

# For local providers, check AI service:
top -p $(pgrep ollama)     # Ollama CPU usage
nvidia-smi                 # GPU usage if applicable

# For cloud providers, test network speed:
speedtest-cli              # Overall internet speed
curl -w "@curl-format.txt" https://api.openai.com/v1/models
```

### **Memory Usage Analysis**

**Normal Memory Usage:**
- **Brainarr Plugin**: 50-100MB additional to Lidarr
- **Cache Storage**: 10-50MB for recommendation cache
- **Local AI Models**: 4-32GB depending on model size

**High Memory Troubleshooting:**
```bash
# Monitor Lidarr memory usage
ps aux | grep Lidarr

# Check for memory leaks
# 1. Note memory usage after restart
# 2. Run several recommendation cycles  
# 3. Compare memory usage - should stabilize
# 4. If continuously growing, report as bug

# Reduce memory usage:
# - Lower max recommendations
# - Shorter cache duration
# - Use smaller AI models (local providers)
```

---

## 🔒 **Security Issues**

### **SSL/TLS Certificate Errors**

**Error**: "SSL connection could not be established"

**Solutions:**
```bash
# Update certificates (Linux)
sudo apt update && sudo apt install ca-certificates
sudo update-ca-certificates

# Windows  
# Update Windows to latest version
# Or temporarily disable SSL verification (NOT recommended for production)

# Docker
# Use updated base image with current certificates
docker pull ghcr.io/hotio/lidarr:pr-plugins-2.13.3.4692
```

### **API Key Security Warnings**

**Best Practices (from Brainarr's security implementation):**
- ✅ **Validation**: Brainarr validates key formats before use
- ✅ **Sanitization**: Input sanitization prevents injection
- ✅ **Storage**: Keys stored securely in Lidarr's configuration
- ✅ **Logging**: Keys never logged or exposed in error messages

**If You Suspect Key Compromise:**
1. **Revoke**: Immediately revoke key in provider dashboard
2. **Generate New**: Create fresh API key  
3. **Update**: Enter new key in Brainarr settings
4. **Monitor**: Watch for unexpected usage in provider dashboard

---

## 🧪 **Testing and Validation Tools**

### **Manual Testing Procedures**

#### **Provider Connectivity Test**
```bash
# Test each configured provider
# From Lidarr: Settings → Import Lists → Brainarr → Test

# Manual API testing:
./test-provider.sh openai sk-your-key-here
./test-provider.sh anthropic sk-ant-your-key-here  
./test-provider.sh gemini AIza-your-key-here
```

#### **Recommendation Quality Test**
1. **Run Manual Import** with debug logging enabled
2. **Check Results**:
   - No duplicates with existing library
   - Reasonable genre/style matching
   - Real albums/artists (no hallucinations)
   - Appropriate for your library size

#### **Performance Benchmark**
```bash
# Time a full recommendation cycle
time curl -X POST localhost:8686/api/v1/importlist/[brainarr-id]/test

# Monitor system resources during generation
vmstat 1 10    # CPU and memory utilization
iostat 1 10    # Disk I/O patterns  
netstat -i     # Network interface statistics
```

### **Configuration Validation**

**Settings Validation (automatic):**
- **Recommendation Count**: 1-50 range enforced
- **Timeouts**: 30-120 seconds range
- **URLs**: Valid format and reachable
- **API Keys**: Format validation per provider
- **Temperature**: 0.0-1.0 range for applicable providers

**Custom Validation:**
```bash
# Verify Lidarr can access configured URLs
curl -f http://localhost:11434/api/tags    # Ollama
curl -f http://localhost:1234/v1/models    # LM Studio

# Test cloud provider endpoints  
curl -f https://api.openai.com/v1/models
curl -f https://api.anthropic.com/v1/messages
```

---

## 🏥 **Recovery Procedures**

### **Complete Plugin Recovery**

**If Brainarr is completely broken:**

**1. Emergency Disable:**
```bash
# Temporarily disable import list
# Settings → Import Lists → Brainarr → Disable
# This stops automatic runs while you troubleshoot
```

**2. Clean Reinstall:**
```bash
# Stop Lidarr
sudo systemctl stop lidarr

# Remove plugin completely  
rm -rf /var/lib/lidarr/.config/Lidarr/plugins/Brainarr/

# Download fresh copy
wget https://github.com/RicherTunes/Brainarr/releases/latest/download/Brainarr-v1.0.3.zip
unzip Brainarr-v1.0.3.zip -d /var/lib/lidarr/.config/Lidarr/plugins/

# Fix permissions
chown -R lidarr:lidarr /var/lib/lidarr/.config/Lidarr/plugins/

# Start Lidarr  
sudo systemctl start lidarr
```

**3. Reconfigure from Scratch:**
- Add Brainarr import list again
- Use known-working provider (Gemini recommended for testing)
- Start with minimal settings (10 recommendations, Similar mode)
- Test thoroughly before enabling automatic runs

### **Configuration Backup & Restore**

**Backup Current Settings:**
```bash
# Export Lidarr configuration (includes Brainarr settings)
cp /var/lib/lidarr/.config/Lidarr/config.xml ~/brainarr-backup-$(date +%Y%m%d).xml
```

**Restore Previous Working Configuration:**
```bash
# Stop Lidarr
sudo systemctl stop lidarr

# Restore configuration
cp ~/brainarr-backup-YYYYMMDD.xml /var/lib/lidarr/.config/Lidarr/config.xml

# Start Lidarr
sudo systemctl start lidarr
```

---

## 📈 **Performance Troubleshooting**

### **Optimization Based on Library Size**

**Small Libraries (< 100 albums):**
```
Max Recommendations: 5-10
Discovery Mode: Similar (avoids too much exploration)  
Refresh Interval: 24-48 hours
Sampling Strategy: Minimal
```

**Large Libraries (1000+ albums):**
```
Max Recommendations: 30-50  
Discovery Mode: Exploratory (more variety needed)
Refresh Interval: 12-24 hours
Sampling Strategy: Comprehensive
Use premium provider for better analysis
```

### **Resource Optimization**

**CPU-Constrained Systems:**
- Use cloud providers instead of local AI
- Reduce recommendation frequency
- Lower max recommendations per run
- Use faster cloud models (Gemini Flash, GPT-4o-mini)

**Memory-Constrained Systems:**  
- Reduce cache duration (30 minutes instead of 60)
- Lower max recommendations  
- Use smaller local models if applicable
- Monitor for memory leaks and report if found

**Network-Constrained Systems:**
- Use local providers when possible
- Reduce recommendation frequency
- Enable longer cache duration
- Use providers with lower response payload sizes

---

**Still having issues?** Create a detailed bug report at [GitHub Issues](https://github.com/RicherTunes/Brainarr/issues) with diagnostic information!

### Artist Images Fail To Download (403 from TheAudioDB)

Symptoms:
- Logs show repeated lines like:
  - HttpClient: HTTP Error - Res: HTTP/2.0 [HEAD] https://theaudiodb.com/...: 403.Forbidden
  - MediaCoverService: Couldn't download media cover ... HTTP request failed: [403:Forbidden] [HEAD]

Root cause:
- Some CDNs (including TheAudioDB) block HTTP/2 HEAD requests. Lidarr performs a HEAD to check Last-Modified/Content-Length before downloading artist images.

Impact:
- Artist banners/fanart/logos may not download. Album covers (from Cover Art Archive/imagecache) are typically unaffected.

Workarounds (without modifying Lidarr):
- Settings → Metadata: de-prioritize or disable TheAudioDB image sources; prefer Fanart.tv/Cover Art Archive where available.
- If using a reverse proxy, add a per-host rule for 	heaudiodb.com to rewrite HEAD to GET with Range: bytes=0-0 and pass through headers.
- Ensure no restrictive firewall/IDS is blocking HEAD to TheAudioDB.
- Keep Lidarr updated; upstream may add a fallback for hosts that reject HEAD.

Note:
- This is outside Brainarr’s code path; Brainarr does not control media cover fetching.


### Reading Brainarr Logs

When "Enable Debug Logging" is ON, Brainarr emits helpful diagnostics. You can also control per‑item logs via "Log Per‑Item Decisions".

- Tokens: Shows strategy/provider token limit and estimated prompt usage for each call/iteration.
  - Example: `Tokens => Strategy=Comprehensive, Provider=LMStudio, Limit≈40000, EstimatedUsed≈18350`
- Per‑Item Decisions:
  - Accepted (Debug only): `[Brainarr Debug] Accepted: Artist — Album (conf=0.92)`
  - Rejected (Always Info unless disabled): `[Brainarr] Rejected: Artist — Album because invalid_year`
- Iteration Summaries:
  - Per‑iteration: `Iteration 2: 7/12 unique (success rate: 58.3%)`
  - Tokens per iteration: `[Brainarr Debug] Iteration Tokens => …`
  - End‑of‑run: `[Brainarr] Iteration Summary => Iterations=2, OverallUnique=12/22 (54.5%), AvgTokens≈18250, LastRequest=30`

Typical reject reasons and what they mean:
- `missing artist` / `missing album (album mode)`: Model did not provide required fields.
- `already in library`: Item matched existing library; deduped.
- `duplicate in this session`: Already suggested earlier in the same run.
- `invalid_year`: Future/invalid year detected in the album title.
- `excessive_descriptions`: Too many qualifiers (deluxe/remaster etc.).
- `fictional_pattern:<term>`: Obvious hallucination patterns (e.g., "(ai version)").
- `ai_generated_pattern`: Heuristics suggest AI‑hallucinated naming.

To reduce log noise but keep overall visibility, disable "Log Per‑Item Decisions" — aggregate summaries and tokens will still be logged.
