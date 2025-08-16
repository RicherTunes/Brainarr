# Brainarr Troubleshooting Guide

## Table of Contents
1. [Common Issues](#common-issues)
2. [Provider-Specific Issues](#provider-specific-issues)
3. [Performance Issues](#performance-issues)
4. [Configuration Issues](#configuration-issues)
5. [API and Connection Issues](#api-and-connection-issues)
6. [Library Analysis Issues](#library-analysis-issues)
7. [Diagnostic Tools](#diagnostic-tools)
8. [Error Messages Reference](#error-messages-reference)
9. [Advanced Debugging](#advanced-debugging)

## Common Issues

### Issue: No recommendations generated

**Symptoms:**
- Import list returns empty
- No new artists/albums added
- Test succeeds but fetch returns nothing

**Solutions:**

1. **Check Library Size**
   ```bash
   # Minimum 10 artists required for analysis
   sqlite3 /config/lidarr.db "SELECT COUNT(*) FROM Artists;"
   ```
   - If < 10 artists: Add more music to your library first

2. **Verify Provider Response**
   - Enable debug logging in Settings → General → Log Level → Debug
   - Check logs: `/config/logs/lidarr.debug.txt`
   - Look for: `"Successfully got X recommendations from provider"`

3. **Check Cache**
   - Recommendations are cached for configured duration
   - Force refresh: Settings → Import Lists → Brainarr → Clear Cache (if available)
   - Or wait for cache expiry (default: 60 minutes)

4. **Validate Discovery Mode**
   - Similar mode: Requires good genre data in library
   - Exploratory mode: May return very different genres
   - Try different mode if current isn't working

### Issue: "Provider not detected" or "No models available"

**Symptoms:**
- Dropdown shows no models
- Test fails with "connection refused"
- Auto-detection fails

**Solutions:**

1. **For Ollama:**
   ```bash
   # Check if Ollama is running
   systemctl status ollama
   # or
   ps aux | grep ollama
   
   # Test Ollama API directly
   curl http://localhost:11434/api/tags
   
   # If not running, start it
   ollama serve
   
   # Pull a model if none available
   ollama pull llama3.2
   ```

2. **For LM Studio:**
   - Open LM Studio GUI
   - Go to Developer → Local Server
   - Ensure server is started
   - Verify URL matches configuration (usually `http://localhost:1234`)

3. **Click Test Button**
   - Always click Test after configuration changes
   - This populates model dropdown
   - Validates connection before saving

### Issue: High API costs with cloud providers

**Symptoms:**
- Unexpected charges on API dashboard
- High token usage reported
- Frequent API calls

**Solutions:**

1. **Enable Caching**
   ```yaml
   Cache Duration: 1440  # 24 hours
   ```

2. **Reduce Recommendation Frequency**
   ```yaml
   Max Recommendations: 10  # Lower from 20-50
   Sync Interval: Weekly  # Not daily
   ```

3. **Switch to Budget Providers**
   - DeepSeek: 10-20x cheaper than GPT-4
   - Gemini: Free tier available
   - Ollama: Completely free (local)

4. **Monitor Token Usage**
   - Check provider dashboard for usage
   - Enable token logging in debug mode
   - Set up billing alerts

## Provider-Specific Issues

### Ollama Issues

#### "Failed to parse Ollama response"
```bash
# Check Ollama version
ollama version

# Update if needed
curl -fsSL https://ollama.com/install.sh | sh

# Try different model
ollama pull mistral
```

#### Slow responses or timeouts
```bash
# Check model size vs available RAM
free -h

# Use smaller model
ollama pull gemma2:2b  # 2B parameter model

# Increase timeout in settings
Response Timeout: 120  # seconds
```

### OpenAI/Anthropic Issues

#### "Invalid API key"
- Verify key starts with correct prefix:
  - OpenAI: `sk-...`
  - Anthropic: `sk-ant-...`
- Check key permissions on provider dashboard
- Regenerate key if needed

#### Rate limiting errors
```
Error: "Rate limit exceeded"
```
- Add delays between requests
- Upgrade to higher tier
- Use fallback provider

### Local Provider Memory Issues

#### Out of memory errors
```bash
# Check available memory
free -h

# Reduce model size
# Instead of 70B model, use 7B or 13B

# For Ollama, set memory limit
OLLAMA_MAX_LOADED_MODELS=1 ollama serve
```

## Performance Issues

### Slow recommendation generation

**Diagnosis:**
```bash
# Check response times in logs
grep "recommendations from" /config/logs/lidarr.debug.txt | tail -20
```

**Solutions:**

1. **Use Faster Provider**
   - Groq: 10x faster inference
   - Gemini Flash: Optimized for speed
   - Smaller local models

2. **Optimize Library Analysis**
   - Reduce library profile size
   - Use sampling instead of full analysis
   ```yaml
   Library Sample Size: 100  # artists
   ```

3. **Enable Parallel Processing**
   - Multiple providers can be configured for fallback
   - Faster providers prioritized

### High memory usage

**Monitor memory:**
```bash
# During recommendation fetch
top -p $(pgrep -f Lidarr)
```

**Solutions:**
1. Clear recommendation cache regularly
2. Reduce max recommendations
3. Disable recommendation history

## Configuration Issues

### Field validation errors

#### "Provider requires API key"
- Some providers need API keys even for local models
- Set dummy key if running locally: `local-key`

#### "Invalid model selection"
- Model must match provider
- Click Test to refresh model list
- Clear browser cache if dropdown stuck

### Settings not saving

1. **Check Lidarr permissions**
   ```bash
   ls -la /config/
   # Should be writable by lidarr user
   ```

2. **Browser issues**
   - Clear browser cache
   - Try incognito mode
   - Different browser

3. **Database lock**
   ```bash
   # Restart Lidarr
   systemctl restart lidarr
   ```

## API and Connection Issues

### Connection refused

**For local providers:**
```bash
# Check if service is listening
netstat -tlnp | grep -E "11434|1234"

# Test connectivity
curl -I http://localhost:11434
```

**For Docker users:**
```yaml
# Use host networking or proper port mapping
services:
  lidarr:
    network_mode: host
    # OR
    extra_hosts:
      - "host.docker.internal:host-gateway"
```

### SSL/TLS errors

**For self-signed certificates:**
```bash
# Disable SSL verification (development only)
export NODE_TLS_REJECT_UNAUTHORIZED=0
```

**For proxy issues:**
```bash
# Set proxy environment variables
export HTTP_PROXY=http://proxy:8080
export HTTPS_PROXY=http://proxy:8080
export NO_PROXY=localhost,127.0.0.1
```

### Timeout errors

**Increase timeouts:**
```yaml
Connection Timeout: 30  # seconds
Response Timeout: 120   # seconds
```

**For slow connections:**
- Use local providers
- Reduce prompt complexity
- Enable response streaming

## Library Analysis Issues

### "Insufficient library data"

**Minimum requirements:**
- 10+ artists
- 20+ albums
- Genre tags populated

**Fix missing genres:**
```sql
-- Check genre coverage
sqlite3 /config/lidarr.db "
  SELECT COUNT(*) as artists_without_genres 
  FROM Artists 
  WHERE Genres IS NULL OR Genres = '[]';
"
```

**Force metadata refresh:**
1. Select all artists
2. Mass Editor → Refresh & Scan

### Library fingerprint errors

**Symptoms:**
- Same recommendations repeatedly
- Cache not updating with library changes

**Solutions:**
1. Clear cache after major library changes
2. Restart Lidarr to rebuild fingerprint
3. Check logs for fingerprint generation errors

## Diagnostic Tools

### Enable debug logging

1. **Lidarr Settings:**
   ```
   Settings → General → Log Level → Debug
   ```

2. **View logs:**
   ```bash
   tail -f /config/logs/lidarr.debug.txt | grep -i brainarr
   ```

### Test provider connectivity

**Built-in test:**
1. Settings → Import Lists → Brainarr
2. Click "Test"
3. Check response in UI

**Manual test:**
```bash
# Ollama
curl http://localhost:11434/api/generate \
  -d '{"model": "llama3", "prompt": "Hello"}'

# OpenAI
curl https://api.openai.com/v1/models \
  -H "Authorization: Bearer YOUR_KEY"
```

### Monitor rate limiting

```bash
# Check rate limit headers in responses
grep -i "rate" /config/logs/lidarr.debug.txt
```

### Database queries

```sql
-- Check import list status
sqlite3 /config/lidarr.db "
  SELECT * FROM ImportLists 
  WHERE Implementation = 'Brainarr';
"

-- Check recent imports
sqlite3 /config/lidarr.db "
  SELECT * FROM History 
  WHERE Data LIKE '%brainarr%' 
  ORDER BY Date DESC 
  LIMIT 10;
"
```

## Error Messages Reference

### Provider Errors

| Error Message | Cause | Solution |
|--------------|-------|----------|
| `"Provider X is unhealthy"` | Provider failed health checks | Check provider status, verify API key |
| `"All providers failed"` | No fallback succeeded | Check all configured providers |
| `"Circuit breaker open"` | Too many failures | Wait for cooldown period (5 min) |
| `"Rate limit exceeded"` | API quota hit | Wait or upgrade plan |
| `"Model not found"` | Model removed/renamed | Update model selection |
| `"Context length exceeded"` | Prompt too long | Reduce library sample size |

### Configuration Errors

| Error Message | Cause | Solution |
|--------------|-------|----------|
| `"Invalid API key format"` | Malformed key | Check key format for provider |
| `"Provider requires configuration"` | Missing settings | Complete all required fields |
| `"Validation failed"` | Invalid settings combo | Check field dependencies |

### Library Analysis Errors

| Error Message | Cause | Solution |
|--------------|-------|----------|
| `"Library analysis failed"` | Database query error | Check Lidarr database integrity |
| `"Insufficient diversity"` | Too few genres | Add more varied music |
| `"Sampling failed"` | No valid artists | Check artist metadata |

## Advanced Debugging

### Enable provider request logging

```csharp
// In BrainarrSettings.cs (development only)
LogProviderRequests = true;
LogTokenUsage = true;
```

### Analyze token usage

```bash
# Extract token usage from logs
grep "Token usage" /config/logs/lidarr.debug.txt | \
  awk '{sum+=$NF} END {print "Total tokens:", sum}'
```

### Profile performance

```bash
# Time recommendation generation
time curl -X POST http://localhost:8686/api/v1/importlist/action/fetch \
  -H "X-Api-Key: YOUR_LIDARR_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name": "Brainarr"}'
```

### Check provider health metrics

```bash
# Via Lidarr API
curl http://localhost:8686/api/v1/importlist/schema \
  -H "X-Api-Key: YOUR_LIDARR_API_KEY" | \
  jq '.[] | select(.implementation == "Brainarr")'
```

### Memory profiling

```bash
# Monitor memory during fetch
while true; do
  ps aux | grep Lidarr | grep -v grep | awk '{print $6}'
  sleep 1
done
```

## Getting Help

If issues persist after trying these solutions:

1. **Collect diagnostic info:**
   ```bash
   # System info
   uname -a
   dotnet --version
   
   # Lidarr version
   grep Version /config/config.xml
   
   # Recent errors
   grep -i error /config/logs/lidarr.txt | tail -50
   ```

2. **Check documentation:**
   - [Provider Setup Guide](PROVIDER_GUIDE.md)
   - [Architecture Documentation](ARCHITECTURE.md)
   - [User Setup Guide](USER_SETUP_GUIDE.md)

3. **Report issues:**
   - Include diagnostic info
   - Specify provider and model
   - Attach relevant log excerpts
   - Describe expected vs actual behavior

## Preventive Measures

1. **Regular maintenance:**
   - Update providers monthly
   - Clear cache weekly
   - Monitor API usage

2. **Backup configuration:**
   ```bash
   cp /config/config.xml /backup/config.xml.bak
   ```

3. **Test changes:**
   - Always use Test button
   - Start with small batches
   - Monitor first sync closely

4. **Set up monitoring:**
   - API usage alerts
   - Error rate monitoring
   - Health check automation