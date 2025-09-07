# Brainarr Troubleshooting Guide

## Quick Diagnostics

Run this command to check your Brainarr installation:

```bash
# Check if plugin is loaded
grep -i brainarr /var/log/lidarr/lidarr.txt | tail -20

# Check plugin directory
ls -la /var/lib/lidarr/plugins/RicherTunes/Brainarr/

# Test local providers
curl -s http://localhost:11434/api/tags | jq  # Ollama
curl -s http://localhost:1234/v1/models | jq   # LM Studio
```

## Common Issues and Solutions

### 🔴 Plugin Not Appearing in Lidarr

#### Symptoms
- Brainarr doesn't appear in Settings > Import Lists
- No Brainarr option when adding new import list

#### Solutions

1. **Verify Installation Path**
```bash
# Linux/Docker
ls -la /var/lib/lidarr/plugins/RicherTunes/Brainarr/
# Should contain: plugin.json and Lidarr.Plugin.Brainarr.dll

# Windows
dir C:\\ProgramData\\Lidarr\\plugins\\RicherTunes\\Brainarr\
```

2. **Check Plugin Structure**
```text
Brainarr/
├── plugin.json
├── Lidarr.Plugin.Brainarr.dll
└── [dependency DLLs]
```

3. **Validate plugin.json**
```bash
cat /var/lib/lidarr/plugins/RicherTunes/Brainarr/plugin.json | python -m json.tool
```

4. **Restart Lidarr**
```bash
sudo systemctl restart lidarr
# or
docker restart lidarr
```

5. **Check Lidarr Version**
```bash
# Minimum required: 2.14.1.4716
curl http://localhost:8686/api/v1/system/status | jq .version
```

---

### 🔴 No Recommendations Generated

#### Symptoms
- "No results found" message
- Empty recommendation list
- Test succeeds but no albums returned

#### Solutions

1. **Check Library Size**
   - Minimum 10 artists required
   - Add more artists to your library

2. **Verify Provider Connection**
   - Click "Test" in Brainarr settings
   - Check provider-specific status below

3. **Review Settings**
```yaml
Discovery Mode: Similar  # Try changing to Adjacent or Exploratory
Max Recommendations: 20  # Increase if too low
Minimum Confidence: 0.5  # Lower for more results
```

4. **Check Logs**
```bash
tail -f /var/log/lidarr/lidarr.txt | grep -E "Brainarr|recommendation"
```

---

### 🔴 Provider Connection Failed

#### Ollama Issues

**Test Connection:**
```bash
curl http://localhost:11434/api/tags
```

**Solutions:**
```bash
# Install Ollama
curl -fsSL https://ollama.ai/install.sh | sh

# Start Ollama service
systemctl start ollama

# Pull a model
ollama pull llama3

# Verify model is available
ollama list
```

**Docker Users:**
```bash
# Ensure network connectivity
docker network inspect bridge
# Add --network=host or use container name
```

#### LM Studio Issues

**Solutions:**
1. Launch LM Studio application
2. Load a model (e.g., Llama 3 7B)
3. Start local server (top-right corner)
4. Verify at http://localhost:1234

#### OpenAI/Anthropic/Cloud Issues

**Test API Key:**
```bash
# OpenAI
curl https://api.openai.com/v1/models \
  -H "Authorization: Bearer YOUR_API_KEY"

# Anthropic
curl https://api.anthropic.com/v1/messages \
  -H "x-api-key: YOUR_API_KEY" \
  -H "anthropic-version: 2023-06-01"
```

**Common Fixes:**
- Remove quotes from API key in settings
- Check for trailing spaces
- Verify API key has correct permissions
- Check account has credits/quota

---

### 🔴 High API Costs

#### Symptoms
- Unexpected charges on cloud provider accounts
- Rapid credit depletion

#### Solutions

1. **Switch to Local Providers**
```yaml
Provider: Ollama  # Free, local
Model: llama3
```

2. **Use Budget Models**
```yaml
# OpenAI
Model: gpt-3.5-turbo  # 10x cheaper than GPT-4

# Anthropic
Model: claude-3-haiku  # Cheapest Claude model

# Gemini
Model: gemini-1.5-flash  # Free tier available
```

3. **Enable Caching**
```yaml
Cache Duration: 120  # minutes
```

4. **Reduce Frequency**
```yaml
Sync Interval: Every 14 days  # Instead of daily
Max Recommendations: 10  # Instead of 50
```

5. **Monitor Usage**
```bash
# Check Brainarr request count
grep "recommendations from" /var/log/lidarr/lidarr.txt | wc -l
```

---

### 🔴 Timeout Errors

#### Symptoms
- "Request timed out" errors
- Partial results
- Slow response times

#### Solutions

1. **Increase Timeout**
   - Settings > Brainarr > Advanced
   - Request Timeout: 120 seconds

2. **For Local Providers**
   - Check CPU/RAM usage
   - Consider GPU acceleration
   - Use smaller models (7B instead of 70B)

3. **For Cloud Providers**
   - Check internet connection
   - Try different region/endpoint
   - Reduce prompt complexity

---

### 🔴 Invalid/Duplicate Recommendations

#### Symptoms
- Same albums recommended repeatedly
- Artists already in library suggested
- Incorrect artist/album combinations

#### Solutions

1. **Clear Cache**
```bash
# Restart Lidarr to clear in-memory cache
systemctl restart lidarr
```

2. **Adjust Discovery Mode**
   - Similar: Close to existing taste
   - Adjacent: Related but different
   - Exploratory: Completely new genres

3. **Check Confidence Threshold**
   - Increase minimum confidence to 0.7+
   - Filters out uncertain recommendations

---

## Provider-Specific Issues

### Ollama

#### "Failed to connect to Ollama"
```bash
# Check if running
systemctl status ollama

# Check port
netstat -tlnp | grep 11434

# Test directly
ollama run llama3 "test"
```

#### "Model not found"
```bash
# List available models
ollama list

# Pull default model
ollama pull llama3

# Or pull specific model
ollama pull mistral
```

### LM Studio

#### "Connection refused on port 1234"
- Open LM Studio GUI
- Click "Local Server" tab
- Click "Start Server"
- Verify "Server Started" message

#### "No models available"
- Download model in LM Studio
- Wait for download to complete
- Select model in dropdown
- Reload model if needed

### OpenAI

#### "Invalid API key"
- Get key from https://platform.openai.com/api-keys
- Check key starts with "sk-"
- Verify billing is enabled

#### "Rate limit exceeded"
- Wait 1 minute and retry
- Upgrade to paid tier
- Use different model (gpt-3.5-turbo)

### Anthropic

#### "Credit limit reached"
- Check usage at https://console.anthropic.com
- Add payment method
- Use claude-3-haiku (cheaper)

### Google Gemini

#### "API key not valid"
- Get free key at https://aistudio.google.com/apikey
- Enable Gemini API in Google Cloud Console
- Check region availability

---

## Error Messages Explained

### BR001: Provider initialization failed
**Meaning:** The selected AI provider couldn't be initialized
**Fix:** Check provider-specific configuration and requirements

### BR002: API key validation failed
**Meaning:** The API key format is invalid or missing
**Fix:** Verify API key is entered correctly without quotes or spaces

### BR003: Connection timeout
**Meaning:** Provider didn't respond within timeout period
**Fix:** Increase timeout or check network/provider status

### BR004: Rate limit exceeded
**Meaning:** Too many requests to provider
**Fix:** Wait before retrying, enable caching, reduce frequency

### BR005: Invalid response format
**Meaning:** Provider returned unexpected data format
**Fix:** Update plugin, check provider API changes

### BR006: Model not found
**Meaning:** Specified model doesn't exist
**Fix:** Use provider's model list command to see available models

---

## Deployment Issues

### 🔴 Plugin Fails to Load After Update

#### Symptoms
- Plugin was working, stops after Lidarr update
- "Assembly version mismatch" errors
- Plugin crashes on startup

#### Solutions

1. **Rebuild Plugin Against New Lidarr Version**
```bash
# Clean and rebuild
./build.sh --clean
./build.sh --setup  # Re-download latest Lidarr
./build.sh --package
```

2. **Check Lidarr Version Compatibility**
```bash
# Verify minimum version requirement
grep minimumVersion /var/lib/lidarr/plugins/RicherTunes/Brainarr/plugin.json
# Should show: "2.14.1.4716" or higher (nightly)
```

3. **Clear Old Plugin Files**
```bash
# Backup and clean install
mv /var/lib/lidarr/plugins/RicherTunes/Brainarr /tmp/Brainarr.backup
# Re-deploy fresh plugin files
```

### 🔴 Permission Issues (Linux/Docker)

#### Symptoms
- "Access denied" errors in logs
- Plugin files not readable
- Can't write to cache directory

#### Solutions

1. **Fix File Permissions**
```bash
# Set correct ownership
sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins/RicherTunes/Brainarr
# Set correct permissions
sudo chmod -R 755 /var/lib/lidarr/plugins/RicherTunes/Brainarr
```

2. **Docker Volume Permissions**
```yaml
# docker-compose.yml
volumes:
  - ./plugins:/config/plugins
  - ./data:/config
environment:
  - PUID=1000  # Match host user
  - PGID=1000
```

### 🔴 Memory/Performance Issues

#### Symptoms
- Lidarr crashes when using Brainarr
- High memory usage
- Slow response times

#### Solutions

1. **Optimize Local Model Usage**
```bash
# Use quantized models for lower memory
ollama pull llama3:7b-q4_0  # 4-bit quantized
```

2. **Adjust Cache Settings**
```yaml
Cache Duration: 60  # Reduce from default 120
Max Cached Items: 100  # Limit cache size
```

3. **Monitor Resource Usage**
```bash
# Check memory usage
free -h
# Monitor during recommendation generation
htop
```

### 🔴 Network/Firewall Issues

#### Symptoms
- Works locally but not remotely
- Can't connect to local providers
- API calls blocked

#### Solutions

1. **Check Firewall Rules**
```bash
# Allow Ollama port
sudo ufw allow 11434/tcp
# Allow LM Studio port
sudo ufw allow 1234/tcp
```

2. **Docker Network Configuration**
```bash
# Use host network for local providers
docker run --network host lidarr
# Or use container names
Provider URL: http://ollama:11434
```

3. **Proxy Configuration**
```bash
# If behind corporate proxy
export HTTP_PROXY=http://proxy:8080
export HTTPS_PROXY=http://proxy:8080
```

### 🔴 Database/Migration Issues

#### Symptoms
- Settings don't save
- Plugin data corruption
- Migration errors on upgrade

#### Solutions

1. **Reset Plugin Settings**
```sql
-- Connect to Lidarr database
sqlite3 /var/lib/lidarr/lidarr.db
-- Clear Brainarr settings (backup first!)
DELETE FROM ImportLists WHERE Implementation = 'BrainarrImportList';
```

2. **Verify Database Integrity**
```bash
# Backup database first
cp /var/lib/lidarr/lidarr.db /tmp/lidarr.db.backup
# Check integrity
sqlite3 /var/lib/lidarr/lidarr.db "PRAGMA integrity_check;"
```

---

## Advanced Diagnostics

### Enable Debug Logging

1. **Lidarr Debug Mode**
```yaml
Settings > General > Log Level: Debug
```

2. **Check Detailed Logs**
```bash
# Filter Brainarr-specific debug logs
grep -i "brainarr\|recommendation\|provider" /var/log/lidarr/lidarr.debug.txt
```

3. **Provider Request/Response Logging**
```bash
# Monitor API calls (requires debug mode)
tail -f /var/log/lidarr/lidarr.debug.txt | grep "HTTP"
```

### Performance Profiling

```bash
# Time recommendation generation
time curl -X POST http://localhost:8686/api/v1/importlist/action/getBrainarrRecommendations \
  -H "X-Api-Key: YOUR_API_KEY"

# Check cache hit rate in logs
grep "Cache hit\|Cache miss" /var/log/lidarr/lidarr.txt | \
  awk '{print $NF}' | sort | uniq -c
```

### Manual Provider Testing

```bash
# Test provider directly (bypass Brainarr)
# Ollama
curl -X POST http://localhost:11434/api/generate \
  -d '{"model": "llama3", "prompt": "List 5 rock albums"}'

# OpenAI
curl https://api.openai.com/v1/chat/completions \
  -H "Authorization: Bearer $OPENAI_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"model": "gpt-3.5-turbo", "messages": [{"role": "user", "content": "List 5 rock albums"}]}'
```

---

## Getting Help

### Before Asking for Help

1. **Collect Information**
```bash
# System info
uname -a
dotnet --version
# Lidarr version
curl http://localhost:8686/api/v1/system/status
# Plugin files
ls -la /var/lib/lidarr/plugins/RicherTunes/Brainarr/
# Recent logs
tail -100 /var/log/lidarr/lidarr.txt | grep -i brainarr
```

2. **Try Common Fixes**
- Restart Lidarr
- Re-test provider connection
- Clear cache
- Check documentation

### Where to Get Help

- **GitHub Issues**: [github.com/Brainarr/brainarr/issues](https://github.com/Brainarr/brainarr/issues)
- **Discord**: Lidarr Discord #plugins channel
- **Documentation**: Check this guide and API_REFERENCE.md

### Reporting Issues

Include:
- Lidarr version
- Brainarr version
- Provider being used
- Error messages
- Debug logs
- Steps to reproduce

### BR007: Insufficient quota
**Meaning:** API quota/credits exhausted
**Fix:** Add credits, wait for quota reset, switch providers

### BR008: Provider health check failed
**Meaning:** Provider marked unhealthy after multiple failures
**Fix:** Resolve underlying issue, provider will auto-recover

---

## Debug Mode

Enable detailed logging for troubleshooting:

### In Lidarr UI
1. Settings > General > Log Level
2. Set to "Debug" or "Trace"
3. Save and restart

### In Configuration
```yaml
# Brainarr Settings
Log Provider Requests: Yes
Log Token Usage: Yes
Log Response Content: Yes
```

### View Logs
```bash
# Real-time log monitoring
tail -f /var/log/lidarr/lidarr.txt | grep -i brainarr

# Search for errors
grep -i "error.*brainarr" /var/log/lidarr/lidarr.txt

# View last 100 Brainarr entries
grep -i brainarr /var/log/lidarr/lidarr.txt | tail -100
```

---

## Performance Issues

### Slow Recommendations

**Local Providers:**
```bash
# Check system resources
htop  # or top

# For Ollama, try smaller model
ollama pull phi3
ollama run phi3

# Enable GPU (NVIDIA)
nvidia-smi  # Check GPU availability
```

**Cloud Providers:**
- Use Groq for 10x faster inference
- Switch to smaller models (mini/haiku versions)
- Enable caching to avoid repeated calls

### High Memory Usage

```bash
# Check Lidarr memory
ps aux | grep -i lidarr

# Restart to clear memory
systemctl restart lidarr

# Reduce cache duration in settings
```

---

## Network Issues

### Behind Proxy/Firewall

**Configure Proxy:**
```bash
# Set environment variables
export HTTP_PROXY=http://proxy.company.com:8080
export HTTPS_PROXY=http://proxy.company.com:8080
export NO_PROXY=localhost,127.0.0.1
```

**Firewall Rules:**
```bash
# Allow Ollama
sudo ufw allow 11434/tcp

# Allow LM Studio
sudo ufw allow 1234/tcp
```

### Docker Networking

```yaml
# docker-compose.yml
services:
  lidarr:
    network_mode: host  # Access to local providers
    # OR
    extra_hosts:
      - "host.docker.internal:host-gateway"
```

---

## Recovery Procedures

### Reset Brainarr Configuration

1. Stop Lidarr
2. Edit config:
```bash
# Find Brainarr section in config
vim /var/lib/lidarr/config.xml
# Remove <BrainarrSettings> section
```
3. Restart Lidarr
4. Reconfigure from scratch

### Clean Reinstall

```bash
# Stop Lidarr
systemctl stop lidarr

# Backup current plugin
mv /var/lib/lidarr/plugins/RicherTunes/Brainarr /tmp/Brainarr.backup

# Clean install
rm -rf /var/lib/lidarr/plugins/RicherTunes/Brainarr
# Extract fresh plugin files
unzip Brainarr-v1.0.0.zip -d /var/lib/lidarr/plugins/

# Restart
systemctl start lidarr
```

### Database Issues

```bash
# Backup database first!
cp /var/lib/lidarr/lidarr.db /var/lib/lidarr/lidarr.db.backup

# Check database integrity
sqlite3 /var/lib/lidarr/lidarr.db "PRAGMA integrity_check;"

# Remove Brainarr entries if corrupted
sqlite3 /var/lib/lidarr/lidarr.db "DELETE FROM ImportLists WHERE Implementation='Brainarr';"
```

---

## Getting Help

### Before Asking for Help

1. **Collect Information:**
```bash
# System info
uname -a
dotnet --version

# Lidarr version
curl http://localhost:8686/api/v1/system/status

# Plugin files
ls -la /var/lib/lidarr/plugins/RicherTunes/Brainarr/

# Recent logs
grep -i brainarr /var/log/lidarr/lidarr.txt | tail -50 > brainarr-debug.log
```

2. **Try Basic Fixes:**
   - Restart Lidarr
   - Test provider connection
   - Check API keys
   - Review settings

3. **Document the Issue:**
   - What were you trying to do?
   - What happened instead?
   - Error messages (exact text)
   - Steps to reproduce

### Support Channels

1. **GitHub Issues**
   - https://github.com/brainarr/brainarr/issues
   - Include debug logs
   - Use issue templates

2. **Discord/Forums**
   - Lidarr Discord #plugins channel
   - Include version numbers
   - Share configuration (hide API keys!)

3. **Documentation**
   - [API Reference](API_REFERENCE.md)
   - [Provider Guide](PROVIDER_GUIDE.md)
   - [Architecture](ARCHITECTURE.md)

---

## Monitoring and Maintenance

### Health Checks

Create a monitoring script:

```bash
#!/bin/bash
# brainarr-health.sh

echo "Checking Brainarr Health..."

# Check Lidarr is running
if systemctl is-active --quiet lidarr; then
    echo "✓ Lidarr is running"
else
    echo "✗ Lidarr is not running"
fi

# Check Ollama
if curl -s http://localhost:11434/api/tags > /dev/null; then
    echo "✓ Ollama is accessible"
else
    echo "✗ Ollama is not accessible"
fi

# Check recent recommendations
RECENT=$(grep "Successfully got.*recommendations" /var/log/lidarr/lidarr.txt | tail -1)
if [ -n "$RECENT" ]; then
    echo "✓ Recent: $RECENT"
else
    echo "✗ No recent recommendations"
fi

# Check for errors
ERRORS=$(grep -c "ERROR.*Brainarr" /var/log/lidarr/lidarr.txt)
echo "⚠ Error count: $ERRORS"
```

### Regular Maintenance

**Weekly:**
- Check provider health status
- Review recommendation quality
- Monitor API usage/costs

**Monthly:**
- Update local AI models
- Clear old cache entries
- Review and adjust settings
- Check for plugin updates

**Quarterly:**
- Audit API costs
- Evaluate provider performance
- Update documentation
- Backup configuration

---

## Advanced Debugging

### Enable Trace Logging

```csharp
// In code (for developers)
_logger.Trace($"Request: {JsonConvert.SerializeObject(request)}");
_logger.Trace($"Response: {response.Substring(0, 500)}");
```

### Packet Capture

```bash
# Capture API traffic
tcpdump -i any -w brainarr.pcap host api.openai.com

# Analyze with Wireshark
wireshark brainarr.pcap
```

### Performance Profiling

```bash
# Monitor provider response times
grep "Successfully got.*recommendations.*ms" /var/log/lidarr/lidarr.txt | \
  awk '{print $NF}' | \
  awk '{sum+=$1; count++} END {print "Avg response:", sum/count, "ms"}'
```

---

## FAQ

**Q: Can I use multiple providers simultaneously?**
A: Yes, configure provider chain in Advanced Settings for automatic failover.

**Q: Why are recommendations repeating?**
A: Clear cache and increase Discovery Mode to "Exploratory".

**Q: How do I reduce costs?**
A: Use local providers (Ollama/LM Studio), enable caching, reduce frequency.

**Q: Can Brainarr work offline?**
A: Yes, with local providers like Ollama or LM Studio.

**Q: How many artists do I need?**
A: Minimum 10, but 50+ gives better recommendations.

**Q: Which provider is best?**
A: Depends on priorities - Ollama for privacy, OpenAI for quality, DeepSeek for cost.
