# Brainarr Troubleshooting Guide

## Table of Contents
- [Common Issues](#common-issues)
- [Provider-Specific Issues](#provider-specific-issues)
- [Build & Installation Issues](#build--installation-issues)
- [Runtime Issues](#runtime-issues)
- [Performance Issues](#performance-issues)
- [Debugging Guide](#debugging-guide)
- [Error Messages Reference](#error-messages-reference)
- [Getting Help](#getting-help)

## Common Issues

### Plugin Not Appearing in Lidarr

**Symptoms:**
- Brainarr doesn't show up in Import Lists
- No plugin option available in settings

**Solutions:**
1. **Verify Installation Path:**
   ```bash
   # Windows
   ls C:\ProgramData\Lidarr\plugins\Brainarr\
   
   # Linux
   ls /var/lib/lidarr/plugins/Brainarr/
   
   # Docker
   docker exec lidarr ls /config/plugins/Brainarr/
   ```

2. **Check Plugin Files:**
   Ensure these files exist:
   - `Lidarr.Plugin.Brainarr.dll`
   - `plugin.json`
   - All dependency DLLs

3. **Restart Lidarr:**
   ```bash
   # Linux
   sudo systemctl restart lidarr
   
   # Docker
   docker restart lidarr
   
   # Windows
   net stop Lidarr
   net start Lidarr
   ```

4. **Check Lidarr Version:**
   - Minimum required: 4.0.0
   - Check version in Lidarr UI → System → Status

### No Recommendations Generated

**Symptoms:**
- Test succeeds but no recommendations appear
- Empty recommendation list

**Causes & Solutions:**

1. **Small Library:**
   - Ensure at least 10 artists in library
   - Add more artists before requesting recommendations

2. **Incorrect Discovery Mode:**
   ```yaml
   # Try different modes:
   Discovery Mode: Adjacent  # Instead of Similar
   Max Recommendations: 20   # Increase count
   ```

3. **Provider Issues:**
   - Click "Test" to verify connection
   - Check provider logs (see Debugging section)
   - Try different AI model

4. **Cache Issues:**
   - Old cached results being returned
   - Clear cache or wait for expiry (default 60 minutes)

### API Key Errors

**Symptoms:**
- "Invalid API key" error
- "Authentication failed"
- "Unauthorized" response

**Solutions:**

1. **Verify API Key Format:**
   - No extra spaces or quotes
   - Complete key (check for truncation)
   - Correct provider selected

2. **Test API Key Directly:**
   ```bash
   # OpenAI
   curl https://api.openai.com/v1/models \
     -H "Authorization: Bearer YOUR_KEY"
   
   # Anthropic
   curl https://api.anthropic.com/v1/messages \
     -H "x-api-key: YOUR_KEY" \
     -H "anthropic-version: 2023-06-01"
   
   # DeepSeek
   curl https://api.deepseek.com/v1/models \
     -H "Authorization: Bearer YOUR_KEY"
   ```

3. **Check API Key Permissions:**
   - Ensure key has model access permissions
   - Verify spending limits not exceeded
   - Check if key is active/not expired

## Provider-Specific Issues

### Ollama Issues

#### "Connection refused" on localhost:11434

**Solutions:**
1. **Start Ollama Service:**
   ```bash
   # Check if running
   ps aux | grep ollama
   
   # Start service
   ollama serve
   
   # Or run in background
   nohup ollama serve &
   ```

2. **Check Port Binding:**
   ```bash
   # Verify port is listening
   netstat -an | grep 11434
   lsof -i :11434
   ```

3. **Docker Network Issues:**
   ```yaml
   # If Lidarr is in Docker, use host network
   Ollama URL: http://host.docker.internal:11434
   # Or use Docker bridge IP
   Ollama URL: http://172.17.0.1:11434
   ```

#### "No models found"

**Solutions:**
1. **Pull Models:**
   ```bash
   # List available models
   ollama list
   
   # Pull recommended models
   ollama pull qwen2.5
   ollama pull llama3.2
   ollama pull mistral
   ```

2. **Verify Model Loading:**
   ```bash
   # Test model directly
   ollama run qwen2.5 "Hello"
   ```

### LM Studio Issues

#### Server Not Responding

**Solutions:**
1. **Start LM Studio Server:**
   - Open LM Studio
   - Go to "Local Server" tab
   - Load a model
   - Click "Start Server"
   - Verify URL shows (default: http://localhost:1234)

2. **Check Model Loading:**
   - Ensure model is fully loaded (progress bar complete)
   - Try smaller model if RAM limited

### OpenRouter Issues

#### Rate Limiting

**Symptoms:**
- "Rate limit exceeded"
- 429 status codes

**Solutions:**
1. **Add Delays:**
   - Reduce request frequency
   - Enable rate limiting in settings

2. **Switch Models:**
   - Use less popular models
   - Try during off-peak hours

3. **Monitor Usage:**
   - Check dashboard at openrouter.ai/activity
   - Set spending limits

### DeepSeek Issues

#### Slow Response Times

**Solutions:**
1. **Use Correct Endpoint:**
   - Ensure using api.deepseek.com
   - Not beta or test endpoints

2. **Model Selection:**
   - Use `deepseek-chat` for best performance
   - Avoid specialized models unless needed

### Gemini Free Tier Limits

**Daily Limit Reached:**
```
Error: Quota exceeded for quota metric 'requests'
```

**Solutions:**
1. **Wait for Reset:**
   - Limits reset daily at midnight PST
   - Check current usage in Google AI Studio

2. **Optimize Usage:**
   - Reduce recommendation frequency
   - Lower max recommendations count
   - Enable caching

3. **Upgrade to Paid:**
   - Consider paid tier for higher limits

## Build & Installation Issues

### Build Errors

#### "Lidarr installation not found"

**Solution:**
```bash
# Windows PowerShell
$env:LIDARR_PATH = "C:\ProgramData\Lidarr\bin"
.\build.ps1

# Linux/Mac
export LIDARR_PATH="/opt/Lidarr"
./build.sh

# Or use setup flag
./build.sh --setup  # Clones and builds Lidarr
```

#### "Could not load file or assembly"

**Solutions:**
1. **Clean and Rebuild:**
   ```bash
   dotnet clean
   dotnet restore
   dotnet build -c Release
   ```

2. **Check .NET Version:**
   ```bash
   dotnet --version  # Should be 6.0 or higher
   ```

3. **Verify Dependencies:**
   - Check all NuGet packages restored
   - No version conflicts

### Permission Issues

#### "Access denied" during installation

**Solutions:**
1. **Windows:**
   - Run as Administrator
   - Check folder permissions

2. **Linux:**
   ```bash
   # Fix permissions
   sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins/
   sudo chmod -R 755 /var/lib/lidarr/plugins/Brainarr/
   ```

3. **Docker:**
   ```bash
   # Ensure correct user
   docker exec -u root lidarr chown -R abc:abc /config/plugins/
   ```

## Runtime Issues

### High Memory Usage

**Symptoms:**
- Lidarr consuming excessive RAM
- System slowdown during recommendations

**Solutions:**
1. **Limit Concurrent Requests:**
   - Reduce max recommendations
   - Use minimal sampling strategy

2. **Provider Selection:**
   - Use lighter models (Gemini Flash, GPT-3.5)
   - Avoid large context models

3. **Cache Configuration:**
   - Reduce cache duration
   - Clear old cache entries

### Timeout Errors

**Symptoms:**
- "Request timeout" errors
- Provider not responding

**Solutions:**
1. **Increase Timeouts:**
   - Default: 30 seconds
   - Can increase to 120 seconds in advanced settings

2. **Network Issues:**
   ```bash
   # Test connectivity
   ping api.openai.com
   curl -I https://api.anthropic.com
   ```

3. **Use Faster Providers:**
   - Groq for ultra-fast responses
   - Gemini Flash for quick results

## Performance Issues

### Slow Recommendation Generation

**Optimizations:**
1. **Enable Caching:**
   - Set cache duration to 60+ minutes
   - Reduces redundant API calls

2. **Provider Selection:**
   ```yaml
   # Speed priority (fastest to slowest):
   1. Groq (10x faster)
   2. Gemini Flash
   3. DeepSeek
   4. Local models (Ollama/LM Studio)
   5. OpenAI/Anthropic
   ```

3. **Sampling Strategy:**
   - Use "Minimal" for faster processing
   - Reduces context size

### Database Lock Issues

**Symptoms:**
- "Database is locked" errors
- Lidarr UI becomes unresponsive

**Solutions:**
1. **Reduce Frequency:**
   - Increase minimum refresh interval
   - Disable automatic sync temporarily

2. **Database Maintenance:**
   ```bash
   # Backup database first!
   sqlite3 lidarr.db "VACUUM;"
   sqlite3 lidarr.db "REINDEX;"
   ```

## Debugging Guide

### Enable Debug Logging

1. **In Lidarr UI:**
   - Settings → General → Log Level → Debug

2. **View Logs:**
   ```bash
   # Linux
   tail -f /var/log/lidarr/lidarr.txt | grep Brainarr
   
   # Windows
   Get-Content "C:\ProgramData\Lidarr\logs\lidarr.txt" -Tail 50 -Wait | Select-String "Brainarr"
   
   # Docker
   docker logs -f lidarr | grep Brainarr
   ```

### Common Log Patterns

**Successful Request:**
```
[Brainarr] Provider Ollama connected successfully
[Brainarr] Analyzing library: 250 artists, 1200 albums
[Brainarr] Built prompt with 2000 estimated tokens
[Brainarr] Got 20 recommendations from Ollama in 3456ms
[Brainarr] Returning 20 validated recommendations
```

**Failed Request:**
```
[Brainarr] ERROR: Provider OpenAI failed: Invalid API key
[Brainarr] WARN: Falling back to secondary provider
[Brainarr] ERROR: All providers failed, returning empty list
```

### Test Provider Directly

```csharp
// Add to BrainarrImportList.cs for testing
_logger.Debug($"Provider: {_provider.ProviderName}");
_logger.Debug($"URL: {Settings.BaseUrl}");
_logger.Debug($"Model: {Settings.SelectedModel}");
_logger.Debug($"Token estimate: {prompt.Length / 4}");
```

## Error Messages Reference

### Configuration Errors

| Error | Cause | Solution |
|-------|-------|----------|
| "Provider is required" | No provider selected | Select a provider in settings |
| "API Key is required" | Missing API key for cloud provider | Enter valid API key |
| "Invalid URL format" | Malformed provider URL | Use format: http://host:port |
| "Model is required" | No model selected | Click Test to populate models |
| "Invalid discovery mode" | Unknown mode value | Select from: Similar/Adjacent/Exploratory |

### Provider Errors

| Error | Cause | Solution |
|-------|-------|----------|
| "Connection refused" | Service not running | Start provider service |
| "401 Unauthorized" | Invalid API key | Check API key |
| "429 Too Many Requests" | Rate limit hit | Wait or upgrade plan |
| "500 Internal Server Error" | Provider issue | Try again later |
| "503 Service Unavailable" | Provider down | Use fallback provider |

### Processing Errors

| Error | Cause | Solution |
|-------|-------|----------|
| "Failed to parse response" | Invalid JSON from AI | Try different model |
| "No valid recommendations" | All filtered out | Adjust discovery mode |
| "Library too small" | <10 artists | Add more music first |
| "Token limit exceeded" | Prompt too large | Use minimal sampling |

## Getting Help

### Before Asking for Help

1. **Check This Guide:** Review all relevant sections
2. **Search Issues:** Check GitHub issues for similar problems
3. **Collect Information:**
   - Lidarr version
   - Brainarr version (from plugin.json)
   - Provider being used
   - Error messages from logs
   - Steps to reproduce

### Where to Get Help

1. **GitHub Issues:**
   - [Report bugs](https://github.com/brainarr/brainarr/issues)
   - Include all collected information
   - Use issue templates

2. **Debug Information to Include:**
   ```bash
   # System info
   uname -a  # Linux/Mac
   systeminfo  # Windows
   
   # .NET version
   dotnet --version
   
   # Lidarr version
   # (from UI → System → Status)
   
   # Plugin files
   ls -la /path/to/plugins/Brainarr/
   
   # Recent logs
   tail -n 100 /var/log/lidarr/lidarr.txt | grep -i error
   ```

3. **Configuration to Share:**
   - Provider type
   - Discovery mode
   - Sampling strategy
   - Cache settings
   - (Never share API keys!)

### Quick Fixes Checklist

- [ ] Lidarr version 4.0.0+
- [ ] .NET 6.0+ installed
- [ ] Plugin files in correct directory
- [ ] Lidarr restarted after installation
- [ ] Provider service running (if local)
- [ ] API key valid (if cloud)
- [ ] Test button shows success
- [ ] At least 10 artists in library
- [ ] Debug logging enabled
- [ ] Checked logs for errors

## Advanced Debugging

### Network Trace

```bash
# Capture API calls
tcpdump -i any -w brainarr.pcap host api.openai.com

# View with Wireshark
wireshark brainarr.pcap
```

### Database Queries

```sql
-- Check import list settings
SELECT * FROM ImportLists WHERE Implementation = 'Brainarr';

-- View import list status
SELECT * FROM ImportListStatus WHERE ProviderId IN 
  (SELECT Id FROM ImportLists WHERE Implementation = 'Brainarr');

-- Check recent imports
SELECT * FROM History WHERE EventType = 5 
  ORDER BY Date DESC LIMIT 20;
```

### Memory Profiling

```bash
# Monitor Lidarr memory
ps aux | grep Lidarr
top -p $(pgrep Lidarr)

# .NET memory dump
dotnet-dump collect -p $(pgrep Lidarr)
dotnet-dump analyze core_*.dmp
```

---

*Last updated: January 2025 | Version: 1.0.0*