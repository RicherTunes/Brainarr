# Brainarr Comprehensive Troubleshooting Guide

## Table of Contents
1. [Quick Health Check](#quick-health-check)
2. [Installation Issues](#installation-issues)
3. [Provider Configuration Issues](#provider-configuration-issues)
4. [Recommendation Issues](#recommendation-issues)
5. [Performance Issues](#performance-issues)
6. [Error Messages Reference](#error-messages-reference)
7. [Debug Procedures](#debug-procedures)
8. [Log Analysis](#log-analysis)

---

## Quick Health Check

Run this diagnostic script to check your Brainarr installation:

```bash
#!/bin/bash
echo "=== Brainarr Health Check ==="

# Check plugin installation
echo -n "Plugin installed: "
if [ -f "/var/lib/lidarr/plugins/Brainarr/plugin.json" ]; then
    echo "✓"
    echo "Version: $(grep version /var/lib/lidarr/plugins/Brainarr/plugin.json | cut -d'"' -f4)"
else
    echo "✗ - Plugin not found"
fi

# Check Lidarr service
echo -n "Lidarr running: "
if systemctl is-active --quiet lidarr; then
    echo "✓"
else
    echo "✗ - Service not running"
fi

# Check local providers
echo -n "Ollama available: "
if curl -s http://localhost:11434/api/tags >/dev/null 2>&1; then
    echo "✓"
    echo "  Models: $(curl -s http://localhost:11434/api/tags | jq -r '.models[].name' | tr '\n' ' ')"
else
    echo "✗ - Not running"
fi

echo -n "LM Studio available: "
if curl -s http://localhost:1234/v1/models >/dev/null 2>&1; then
    echo "✓"
else
    echo "✗ - Not running"
fi

# Check logs for errors
echo -n "Recent errors: "
ERROR_COUNT=$(grep -i "brainarr.*error" /var/log/lidarr/lidarr.txt 2>/dev/null | tail -10 | wc -l)
echo "$ERROR_COUNT in last 10 log entries"

echo "=== Check Complete ==="
```

---

## Installation Issues

### Plugin Not Appearing in Lidarr

#### Symptoms
- Brainarr doesn't appear in Settings → Import Lists → Add New
- No Brainarr option in dropdown menu
- Plugin.json exists but plugin not loaded

#### Root Causes & Solutions

**1. Incorrect Installation Path**
```bash
# Verify correct path for your system:

# Linux Standard
PLUGIN_PATH="/var/lib/lidarr/plugins/Brainarr"

# Docker
PLUGIN_PATH="/config/plugins/Brainarr"

# Windows
PLUGIN_PATH="C:\ProgramData\Lidarr\plugins\Brainarr"

# Verify files exist
ls -la "$PLUGIN_PATH"
```

**2. Missing Dependencies**
```bash
# Check for required assemblies
ls "$PLUGIN_PATH"/*.dll | wc -l
# Should be at least 5-10 DLL files

# Key required files:
# - Lidarr.Plugin.Brainarr.dll (main plugin)
# - Newtonsoft.Json.dll (JSON handling)
# - FluentValidation.dll (validation)
```

**3. Permission Issues**
```bash
# Fix permissions (Linux/Docker)
sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins/Brainarr
sudo chmod -R 755 /var/lib/lidarr/plugins/Brainarr
```

**4. .NET Version Mismatch**
```bash
# Check Lidarr's .NET version
dotnet --list-runtimes | grep Microsoft.NETCore.App

# Plugin requires .NET 6.0+
# If missing, install:
sudo apt-get install dotnet-runtime-6.0
```

**5. Plugin.json Validation**
```json
{
  "name": "Brainarr",
  "version": "1.0.0",
  "description": "Multi-provider AI-powered music discovery",
  "author": "Brainarr Team",
  "minimumVersion": "4.0.0.0",
  "entryPoint": "Lidarr.Plugin.Brainarr.dll"
}
```

---

## Provider Configuration Issues

### Local Provider Issues (Ollama/LM Studio)

#### Ollama Not Detected

**Diagnosis:**
```bash
# Test Ollama API
curl -v http://localhost:11434/api/tags

# Check Ollama service
systemctl status ollama

# Check Ollama logs
journalctl -u ollama -n 50
```

**Solutions:**

1. **Install Ollama:**
```bash
curl -fsSL https://ollama.com/install.sh | sh
systemctl enable --now ollama
```

2. **Pull Required Model:**
```bash
# Recommended models
ollama pull qwen2.5:latest  # Best overall
ollama pull llama3.2       # Good alternative
ollama pull mistral        # Lightweight option
```

3. **Configure Firewall:**
```bash
# Allow local access
sudo ufw allow from 127.0.0.1 to any port 11434
```

4. **Docker Network Issues:**
```yaml
# docker-compose.yml
services:
  lidarr:
    network_mode: host  # Allow localhost access
```

#### LM Studio Not Detected

**Diagnosis:**
```bash
# Test LM Studio API
curl http://localhost:1234/v1/models

# Check if server is running
lsof -i :1234
```

**Solutions:**

1. **Start LM Studio Server:**
   - Open LM Studio application
   - Load a model (any GGUF format)
   - Click "Start Server" in Local Server tab
   - Verify URL shows `http://localhost:1234`

2. **Model Not Loaded:**
   - Ensure a model is selected and loaded
   - Check model compatibility (GGUF format required)
   - Verify sufficient RAM for model

### Cloud Provider Issues

#### API Key Invalid

**Diagnosis:**
```bash
# Test API key (example for OpenAI)
curl https://api.openai.com/v1/models \
  -H "Authorization: Bearer YOUR_API_KEY"
```

**Common Issues:**

1. **Wrong API Key Format:**
   - OpenAI: Should start with `sk-`
   - Anthropic: Should start with `sk-ant-`
   - Gemini: 39-character string

2. **API Key Permissions:**
   - Ensure key has model access permissions
   - Check organization/project settings
   - Verify billing is active

3. **Rate Limiting:**
```yaml
# In Brainarr settings
Rate Limit: 10  # Reduce if hitting limits
Retry Attempts: 3
Retry Delay: 2000  # milliseconds
```

---

## Recommendation Issues

### No Recommendations Generated

#### Diagnostic Steps

1. **Check Library Size:**
```bash
# Via API (replace YOUR_API_KEY)
curl -H "X-Api-Key: YOUR_API_KEY" \
  http://localhost:8686/api/v1/artist | jq length

# Minimum required: 10 artists
```

2. **Verify Provider Response:**
```bash
# Enable debug logging
echo "LogLevel=Debug" >> /var/lib/lidarr/config.xml
systemctl restart lidarr

# Watch logs during fetch
tail -f /var/log/lidarr/lidarr.txt | grep -i brainarr
```

3. **Test Prompt Generation:**
   - Check if library analysis completes
   - Verify prompt isn't too large (token limit)
   - Look for "Built library-aware prompt" in logs

#### Solutions by Cause

**Insufficient Library Data:**
```yaml
# Lower requirements in settings
Minimum Artists: 5  # Default is 10
Discovery Mode: Exploratory  # More diverse results
```

**Token Limit Exceeded:**
```yaml
# Reduce context size
Sampling Strategy: Minimal  # Less library context
Max Recommendations: 10  # Fewer results requested
```

**Provider Timeout:**
```yaml
# Increase timeouts
Request Timeout: 60  # seconds
Max Retry Attempts: 5
```

### Poor Recommendation Quality

#### Symptoms
- Recommendations don't match taste
- Too many mainstream suggestions
- Repeated artists/albums

#### Solutions

1. **Adjust Discovery Mode:**
```yaml
Similar: Close to your library
Adjacent: Related genres
Exploratory: New territories
```

2. **Fine-tune Prompting:**
```yaml
Include Genres: Yes
Include Decades: Yes
Exclude Mainstream: Yes  # For niche recommendations
```

3. **Change Provider/Model:**
   - GPT-4/Claude: Best quality
   - Gemini: Good balance
   - Local models: May need prompt adjustment

---

## Performance Issues

### Slow Recommendation Generation

#### Diagnosis
```bash
# Check response times in logs
grep "recommendation.*took" /var/log/lidarr/lidarr.txt

# Monitor system resources
htop  # During recommendation fetch
```

#### Solutions

1. **Enable Caching:**
```yaml
Cache Duration: 60  # minutes
Cache Size: 100  # entries
```

2. **Optimize Provider:**
   - Use Groq for fastest inference
   - Use smaller models (GPT-4o-mini vs GPT-4o)
   - Use local providers to avoid network latency

3. **Reduce Library Context:**
```yaml
Sampling Strategy: Minimal
Max Artists in Context: 50
```

### High API Costs

#### Cost Reduction Strategies

1. **Switch to Budget Providers:**
   - DeepSeek: 10-20x cheaper than GPT-4
   - Gemini: Free tier available
   - Local models: Completely free

2. **Optimize Usage:**
```yaml
Cache Duration: 120  # Longer caching
Fetch Interval: Weekly  # Less frequent
Max Recommendations: 10  # Fewer per fetch
```

3. **Use Token Limits:**
```yaml
Max Tokens: 1000  # Reduce response size
Temperature: 0.7  # Less creative = cheaper
```

---

## Error Messages Reference

### Common Error Messages and Solutions

| Error Message | Cause | Solution |
|--------------|-------|----------|
| "No AI provider configured" | Provider not selected | Select provider in settings |
| "Failed to connect to provider" | Network/URL issue | Check provider URL and firewall |
| "Invalid API key" | Wrong or expired key | Regenerate API key |
| "Rate limit exceeded" | Too many requests | Reduce rate limit in settings |
| "Model not found" | Model doesn't exist | Check available models for provider |
| "Token limit exceeded" | Prompt too large | Use Minimal sampling strategy |
| "Library too small" | <10 artists | Add more music or reduce minimum |
| "Cache corrupted" | Cache file error | Delete cache file and restart |
| "Timeout waiting for response" | Slow provider | Increase timeout or switch provider |
| "Invalid recommendation format" | Parser error | Check logs for malformed JSON |

---

## Debug Procedures

### Enable Verbose Logging

1. **Lidarr Configuration:**
```xml
<!-- /var/lib/lidarr/config.xml -->
<Config>
  <LogLevel>Debug</LogLevel>
  <AnalyticsEnabled>False</AnalyticsEnabled>
</Config>
```

2. **Brainarr-Specific Debug:**
```yaml
# In Brainarr settings
Debug Mode: Enabled
Log Provider Requests: Yes
Log Token Usage: Yes
Log Cache Operations: Yes
```

### Capture Debug Information

```bash
#!/bin/bash
# debug_brainarr.sh

echo "Starting Brainarr debug capture..."

# Create debug directory
DEBUG_DIR="/tmp/brainarr_debug_$(date +%Y%m%d_%H%M%S)"
mkdir -p "$DEBUG_DIR"

# Capture configuration
cp -r /var/lib/lidarr/plugins/Brainarr "$DEBUG_DIR/"
cp /var/lib/lidarr/config.xml "$DEBUG_DIR/"

# Capture recent logs
grep -i brainarr /var/log/lidarr/lidarr.txt | tail -1000 > "$DEBUG_DIR/brainarr.log"

# Test provider connectivity
echo "=== Provider Tests ===" > "$DEBUG_DIR/provider_tests.txt"
curl -s http://localhost:11434/api/tags >> "$DEBUG_DIR/provider_tests.txt" 2>&1
curl -s http://localhost:1234/v1/models >> "$DEBUG_DIR/provider_tests.txt" 2>&1

# System information
echo "=== System Info ===" > "$DEBUG_DIR/system_info.txt"
uname -a >> "$DEBUG_DIR/system_info.txt"
dotnet --list-runtimes >> "$DEBUG_DIR/system_info.txt"
free -h >> "$DEBUG_DIR/system_info.txt"

# Create archive
tar -czf "brainarr_debug_$(date +%Y%m%d_%H%M%S).tar.gz" -C /tmp "$(basename $DEBUG_DIR)"

echo "Debug information saved to: brainarr_debug_$(date +%Y%m%d_%H%M%S).tar.gz"
```

---

## Log Analysis

### Key Log Patterns to Watch

```bash
# Successful operations
grep "Successfully got.*recommendations" /var/log/lidarr/lidarr.txt

# Errors
grep -E "ERROR.*Brainarr|Brainarr.*ERROR" /var/log/lidarr/lidarr.txt

# Performance metrics
grep -E "took [0-9]+ ms" /var/log/lidarr/lidarr.txt

# Cache hits/misses
grep -E "cache (hit|miss)" /var/log/lidarr/lidarr.txt

# Provider health
grep "provider health" /var/log/lidarr/lidarr.txt
```

### Log Correlation

Use correlation IDs to track requests:
```bash
# Find correlation ID for a request
CORRELATION_ID=$(grep "Starting recommendation request" /var/log/lidarr/lidarr.txt | tail -1 | grep -o "CorrelationId: [^ ]*")

# Track full request flow
grep "$CORRELATION_ID" /var/log/lidarr/lidarr.txt
```

---

## Getting Help

If issues persist after following this guide:

1. **Collect Debug Information:**
   - Run the debug script above
   - Include Lidarr version
   - Include Brainarr version
   - Include provider being used

2. **Check Documentation:**
   - [Provider Guide](PROVIDER_GUIDE.md)
   - [Architecture](ARCHITECTURE.md)
   - [API Reference](API_REFERENCE.md)

3. **Search Existing Issues:**
   - Check GitHub issues for similar problems
   - Review closed issues for solutions

4. **Report New Issue:**
   - Use debug information template
   - Include reproduction steps
   - Specify expected vs actual behavior
   - Attach relevant log excerpts

---

## Prevention Best Practices

1. **Regular Maintenance:**
   - Update providers monthly
   - Clear cache if stale
   - Review logs for warnings

2. **Monitor Performance:**
   - Track API costs
   - Monitor response times
   - Check cache hit rates

3. **Backup Configuration:**
   - Export Brainarr settings
   - Document API keys securely
   - Keep provider URLs updated

4. **Test Changes:**
   - Use Test button after changes
   - Verify one provider before adding more
   - Start with small recommendation counts