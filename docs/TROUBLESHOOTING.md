# Brainarr Troubleshooting Guide

## Table of Contents
- [Quick Diagnosis](#quick-diagnosis)
- [Common Issues](#common-issues)
- [Provider-Specific Issues](#provider-specific-issues)
- [Error Codes Reference](#error-codes-reference)
- [Debug Logging](#debug-logging)
- [Performance Issues](#performance-issues)
- [Network Issues](#network-issues)
- [Advanced Troubleshooting](#advanced-troubleshooting)

## Quick Diagnosis

### Diagnostic Checklist

1. **Test Connection**: Settings → Import Lists → Brainarr → Test
2. **Check Logs**: System → Logs → Filter by "Brainarr"
3. **Verify Provider**: Is your AI provider running/configured?
4. **Check Network**: Can you reach the provider endpoint?
5. **Validate API Key**: Is your API key valid and has credits?

### Quick Fixes

| Symptom | Likely Cause | Quick Fix |
|---------|-------------|-----------|
| "No models found" | Provider not running | Start Ollama/LM Studio |
| "Connection refused" | Wrong URL/port | Check provider URL |
| "Invalid API key" | Wrong key | Regenerate API key |
| "No recommendations" | Empty library | Add more artists first |
| "Rate limit exceeded" | Too many requests | Wait or upgrade tier |

## Common Issues

### Issue: No AI Provider Configured

**Symptoms:**
- Error: "No AI provider configured"
- Empty recommendation list
- Test fails immediately

**Solutions:**
1. Select a provider in settings
2. For local providers:
   ```bash
   # Start Ollama
   ollama serve
   
   # Pull a model
   ollama pull qwen2.5
   ```
3. For cloud providers:
   - Enter API key
   - Select model
4. Click Test to verify

### Issue: No Recommendations Generated

**Symptoms:**
- Test passes but no recommendations
- "No recommendations received from AI provider"
- Empty import list

**Diagnosis:**
```bash
# Check library size
curl http://localhost:8686/api/v1/artist -H "X-Api-Key: YOUR_API_KEY" | jq length

# Should return > 10 for good recommendations
```

**Solutions:**
1. **Small Library**: Add at least 10-20 artists
2. **Wrong Discovery Mode**: Try "Adjacent" or "Exploratory"
3. **Model Issues**: Switch to a different model
4. **Prompt Issues**: Reduce MaxRecommendations to 5
5. **Clear Cache**: Restart Lidarr to clear recommendation cache

### Issue: Duplicate Recommendations

**Symptoms:**
- Same albums recommended repeatedly
- Already owned albums suggested

**Solutions:**
1. Enable iterative strategy (automatic in v1.0.0+)
2. Clear cache:
   ```bash
   # Linux
   rm -rf /var/lib/lidarr/.cache/brainarr/
   
   # Windows
   del %ProgramData%\Lidarr\.cache\brainarr\*
   ```
3. Increase library sampling to "Comprehensive"
4. Switch discovery mode to "Exploratory"

### Issue: High API Costs

**Symptoms:**
- Unexpected charges on cloud providers
- Rapid credit depletion

**Solutions:**
1. **Switch to Local**:
   ```bash
   # Install Ollama (free, private)
   curl -fsSL https://ollama.com/install.sh | sh
   ollama pull qwen2.5
   ```

2. **Use Budget Providers**:
   - DeepSeek: $0.14/M tokens (200x cheaper than GPT-4)
   - Gemini: Free tier available
   - Groq: Low-cost, fast inference

3. **Optimize Settings**:
   - Enable caching (60 min default)
   - Reduce MaxRecommendations
   - Increase sync interval
   - Use "Minimal" sampling strategy

4. **Monitor Usage**:
   ```sql
   -- Check API calls in last 24h
   SELECT COUNT(*) FROM ImportListStatus 
   WHERE Provider = 'Brainarr' 
   AND LastInfoSync > datetime('now', '-1 day');
   ```

## Provider-Specific Issues

### Ollama Issues

#### Models Not Detected

**Error:** "No suitable models found"

**Fix:**
```bash
# List available models
ollama list

# If empty, pull models
ollama pull qwen2.5
ollama pull llama3.2
ollama pull mistral

# Verify models loaded
curl http://localhost:11434/api/tags
```

#### Connection Refused

**Error:** "Connection refused at http://localhost:11434"

**Fix:**
```bash
# Check if Ollama is running
ps aux | grep ollama

# Start Ollama
ollama serve

# Check port is listening
netstat -an | grep 11434

# Test API directly
curl http://localhost:11434/api/tags
```

#### Slow Responses

**Fix:**
```bash
# Use smaller model
ollama pull qwen2.5:7b  # Instead of larger models

# Check system resources
htop  # or top

# Increase Ollama resources
OLLAMA_NUM_PARALLEL=4 ollama serve
```

### LM Studio Issues

#### Model Not Loaded

**Error:** "No models loaded in LM Studio"

**Fix:**
1. Open LM Studio UI
2. Go to Models tab
3. Download a model (recommend Llama 3 8B)
4. Go to Server tab
5. Load the model
6. Start server
7. Verify at http://localhost:1234/v1/models

#### Server Not Running

**Error:** "Cannot connect to LM Studio"

**Fix:**
1. Open LM Studio
2. Go to Developer → Local Server
3. Select model
4. Click "Start Server"
5. Note the URL (usually http://localhost:1234)
6. Test: `curl http://localhost:1234/v1/models`

### OpenAI Issues

#### Invalid API Key

**Error:** "401 Unauthorized"

**Fix:**
1. Go to https://platform.openai.com/api-keys
2. Create new key
3. Copy entire key (starts with "sk-")
4. Paste in Brainarr settings
5. Ensure billing is enabled

#### Rate Limits

**Error:** "429 Too Many Requests"

**Fix:**
1. Wait 1 minute and retry
2. Upgrade to higher tier
3. Switch to GPT-4o-mini (higher limits)
4. Enable rate limiting in settings

### Anthropic Issues

#### Region Restrictions

**Error:** "Service not available in your region"

**Fix:**
1. Use VPN to supported region
2. Switch to OpenRouter (proxy service)
3. Use alternative provider

### Gemini Issues

#### Free Tier Limits

**Error:** "Quota exceeded"

**Limits:**
- 15 requests/minute
- 1,500 requests/day

**Fix:**
1. Wait until next day (resets at midnight PST)
2. Upgrade to paid tier
3. Reduce request frequency

### DeepSeek Issues

#### Account Credits

**Error:** "Insufficient balance"

**Fix:**
1. Log in to platform.deepseek.com
2. Add credits (minimum $1)
3. Check usage dashboard
4. Consider switching to free Gemini

## Error Codes Reference

### Local Provider Errors

| Code | Provider | Meaning | Solution |
|------|----------|---------|----------|
| OLLAMA_001 | Ollama | Connection refused | Start Ollama service |
| OLLAMA_002 | Ollama | Model not found | Pull model: `ollama pull model` |
| OLLAMA_003 | Ollama | Timeout | Use smaller model or increase timeout |
| LMS_001 | LM Studio | Server not running | Start LM Studio server |
| LMS_002 | LM Studio | No model loaded | Load model in LM Studio UI |
| LMS_003 | LM Studio | Invalid response | Restart LM Studio |

### Cloud Provider Errors

| Code | Provider | Meaning | Solution |
|------|----------|---------|----------|
| OAI_401 | OpenAI | Invalid API key | Check API key format |
| OAI_429 | OpenAI | Rate limit | Wait or upgrade tier |
| OAI_500 | OpenAI | Server error | Retry later |
| ANT_401 | Anthropic | Authentication failed | Verify API key |
| ANT_429 | Anthropic | Rate limit | Reduce request frequency |
| GEM_403 | Gemini | Quota exceeded | Wait for reset or upgrade |
| GEM_404 | Gemini | Model not found | Use supported model |
| DSK_402 | DeepSeek | Payment required | Add account credits |
| GRQ_503 | Groq | Service unavailable | Check status.groq.com |

### System Errors

| Code | Component | Meaning | Solution |
|------|-----------|---------|----------|
| SYS_001 | Cache | Cache corrupted | Clear cache directory |
| SYS_002 | Database | Query failed | Check Lidarr database |
| SYS_003 | Network | DNS resolution failed | Check DNS settings |
| SYS_004 | Permissions | Access denied | Check file permissions |

## Debug Logging

### Enable Debug Mode

1. **Lidarr Settings**:
   - System → General → Log Level → Debug
   - Save and restart

2. **View Logs**:
   ```bash
   # Linux
   tail -f /var/log/lidarr/lidarr.txt | grep -i brainarr
   
   # Docker
   docker logs -f lidarr | grep -i brainarr
   
   # Windows
   Get-Content "C:\ProgramData\Lidarr\logs\lidarr.txt" -Wait | Select-String "brainarr"
   ```

3. **Log Locations**:
   - Linux: `/var/log/lidarr/`
   - Windows: `C:\ProgramData\Lidarr\logs\`
   - Docker: `/config/logs/`
   - macOS: `~/.config/Lidarr/logs/`

### What to Look For

```log
# Successful provider initialization
[Info] Brainarr: Initializing Ollama provider at http://localhost:11434

# Model detection
[Debug] Brainarr: Auto-detecting models for Ollama
[Info] Brainarr: Found 3 Ollama models: qwen2.5, llama3.2, mistral

# Successful recommendation
[Info] Brainarr: Fetched 10 unique recommendations from Ollama (Local)

# Cache hit
[Debug] Brainarr: Cache hit for key: ollama_10_hash123
[Info] Brainarr: Returning 10 cached recommendations

# Errors
[Error] Brainarr: Failed to connect to provider: Connection refused
[Warn] Brainarr: Provider Ollama is unhealthy, returning empty list
```

### Useful Debug Commands

```bash
# Test provider directly
curl -X POST http://localhost:11434/api/generate \
  -d '{"model": "qwen2.5", "prompt": "Hello"}'

# Check Lidarr API
curl http://localhost:8686/api/v1/system/status \
  -H "X-Api-Key: YOUR_API_KEY"

# Monitor network traffic
tcpdump -i any -n host localhost and port 11434

# Check process memory
ps aux | grep -E "lidarr|ollama" | awk '{print $2, $11, $4"%"}'
```

## Performance Issues

### Slow Recommendations

**Diagnosis:**
```bash
# Check response times in logs
grep "responseTime" /var/log/lidarr/lidarr.txt | tail -20
```

**Solutions:**

1. **Local Providers**:
   - Use smaller models (7B instead of 70B)
   - Upgrade RAM (16GB+ recommended)
   - Use SSD for model storage
   - Enable GPU acceleration

2. **Cloud Providers**:
   - Switch to Groq (10x faster)
   - Use streaming responses
   - Reduce prompt size (Minimal sampling)
   - Cache results longer

3. **System Optimization**:
   ```bash
   # Increase Lidarr memory
   export LIDARR_MEMORY_LIMIT=2G
   
   # Optimize database
   sqlite3 /var/lib/lidarr/lidarr.db "VACUUM;"
   ```

### High Memory Usage

**Solutions:**
1. Reduce cache size
2. Use smaller AI models
3. Restart Lidarr periodically
4. Monitor with: `htop -p $(pgrep -f lidarr)`

### Database Lock Issues

**Error:** "database is locked"

**Fix:**
```bash
# Stop Lidarr
systemctl stop lidarr

# Check database integrity
sqlite3 /var/lib/lidarr/lidarr.db "PRAGMA integrity_check;"

# Backup database
cp /var/lib/lidarr/lidarr.db /var/lib/lidarr/lidarr.db.backup

# Restart
systemctl start lidarr
```

## Network Issues

### Firewall Blocking

**Test:**
```bash
# Test local providers
telnet localhost 11434  # Ollama
telnet localhost 1234   # LM Studio

# Test cloud endpoints
curl -I https://api.openai.com
curl -I https://api.anthropic.com
```

**Fix:**
```bash
# Allow local connections
sudo ufw allow from 127.0.0.1 to any port 11434
sudo ufw allow from 127.0.0.1 to any port 1234

# For Docker
docker network inspect bridge
```

### Proxy Configuration

**For corporate networks:**
```bash
# Set proxy for Lidarr
export HTTP_PROXY=http://proxy.company.com:8080
export HTTPS_PROXY=http://proxy.company.com:8080
export NO_PROXY=localhost,127.0.0.1
```

### DNS Issues

**Test:**
```bash
nslookup api.openai.com
dig api.anthropic.com
```

**Fix:**
```bash
# Use Google DNS
echo "nameserver 8.8.8.8" | sudo tee /etc/resolv.conf
```

## Advanced Troubleshooting

### Database Queries

```sql
-- Check import list status
SELECT * FROM ImportListStatus WHERE ProviderId = 
  (SELECT Id FROM ImportLists WHERE Implementation = 'Brainarr');

-- View recent imports
SELECT * FROM History 
WHERE Data LIKE '%Brainarr%' 
ORDER BY Date DESC LIMIT 10;

-- Check failed imports
SELECT * FROM ImportListStatus 
WHERE LastInfoSync < datetime('now', '-1 day')
AND DisabledTill IS NOT NULL;
```

### Reset Plugin

**Complete reset:**
```bash
# Stop Lidarr
systemctl stop lidarr

# Remove plugin data
rm -rf /var/lib/lidarr/plugins/Brainarr/
rm -rf /var/lib/lidarr/.cache/brainarr/

# Remove from database
sqlite3 /var/lib/lidarr/lidarr.db \
  "DELETE FROM ImportLists WHERE Implementation = 'Brainarr';"

# Reinstall plugin
cp -r /path/to/brainarr/plugin /var/lib/lidarr/plugins/

# Start Lidarr
systemctl start lidarr
```

### Memory Profiling

```bash
# Monitor memory usage
watch -n 1 'ps aux | grep -E "lidarr|ollama" | grep -v grep'

# Detailed memory map
pmap -x $(pgrep -f lidarr)

# Check for memory leaks
valgrind --leak-check=full lidarr
```

### Network Debugging

```bash
# Capture API traffic
tcpdump -i any -w brainarr.pcap \
  'host api.openai.com or host localhost'

# Analyze with Wireshark
wireshark brainarr.pcap

# Monitor connections
ss -tunap | grep -E "lidarr|11434|1234"
```

## Getting Help

### Before Asking for Help

1. **Collect Information**:
   ```bash
   # System info
   uname -a
   lidarr --version
   dotnet --version
   
   # Plugin version
   cat /var/lib/lidarr/plugins/Brainarr/plugin.json
   
   # Recent logs
   tail -n 100 /var/log/lidarr/lidarr.txt > brainarr-debug.log
   ```

2. **Try Basic Fixes**:
   - Restart Lidarr
   - Test provider connection
   - Clear cache
   - Check logs

3. **Document Steps**:
   - What were you trying to do?
   - What happened instead?
   - What error messages appeared?
   - What have you tried?

### Where to Get Help

1. **Documentation**:
   - This troubleshooting guide
   - [API Reference](API_REFERENCE.md)
   - [Provider Guide](PROVIDER_GUIDE.md)

2. **Community**:
   - GitHub Issues (include debug logs)
   - Lidarr Discord #custom-plugins
   - Reddit r/lidarr

3. **Provider Support**:
   - Ollama: github.com/ollama/ollama/issues
   - OpenAI: platform.openai.com/support
   - Anthropic: support.anthropic.com

## Prevention Tips

1. **Regular Maintenance**:
   - Update providers monthly
   - Clear cache weekly
   - Review logs for warnings
   - Monitor API usage

2. **Best Practices**:
   - Start with local providers
   - Test settings before saving
   - Use appropriate sampling strategy
   - Enable caching
   - Set reasonable sync intervals

3. **Monitoring**:
   - Set up log alerts for errors
   - Monitor API costs daily
   - Track recommendation quality
   - Review provider health metrics