# Brainarr Complete Troubleshooting Guide

## Table of Contents
1. [Quick Diagnostics](#quick-diagnostics)
2. [Installation Issues](#installation-issues)
3. [Provider Connection Issues](#provider-connection-issues)
4. [Recommendation Generation Issues](#recommendation-generation-issues)
5. [Performance Issues](#performance-issues)
6. [API Key Issues](#api-key-issues)
7. [Error Messages Reference](#error-messages-reference)
8. [Debug Procedures](#debug-procedures)
9. [Log Analysis](#log-analysis)
10. [Platform-Specific Issues](#platform-specific-issues)

---

## Quick Diagnostics

### Automated Health Check Script

Save and run this diagnostic script:

```bash
#!/bin/bash
# brainarr-health-check.sh

echo "=== Brainarr Comprehensive Health Check ==="
echo "Timestamp: $(date)"
echo ""

# Function to check status
check_status() {
    if [ $1 -eq 0 ]; then
        echo "✅ $2"
    else
        echo "❌ $2"
        echo "   Fix: $3"
    fi
}

# 1. Check plugin installation
echo "1. Plugin Installation:"
if [ -f "/var/lib/lidarr/plugins/Brainarr/plugin.json" ]; then
    VERSION=$(grep '"version"' /var/lib/lidarr/plugins/Brainarr/plugin.json | cut -d'"' -f4)
    check_status 0 "Plugin installed (v$VERSION)" ""
else
    check_status 1 "Plugin not found" "Copy plugin files to /var/lib/lidarr/plugins/Brainarr/"
fi

# 2. Check Lidarr service
echo ""
echo "2. Lidarr Service:"
if systemctl is-active --quiet lidarr 2>/dev/null || pgrep -f Lidarr >/dev/null; then
    check_status 0 "Lidarr is running" ""
else
    check_status 1 "Lidarr not running" "Run: sudo systemctl start lidarr"
fi

# 3. Check Lidarr version
echo ""
echo "3. Lidarr Version:"
LIDARR_VERSION=$(curl -s http://localhost:8686/api/v1/system/status 2>/dev/null | jq -r .version)
if [ ! -z "$LIDARR_VERSION" ]; then
    if [[ "$LIDARR_VERSION" > "4.0.0" ]]; then
        check_status 0 "Lidarr $LIDARR_VERSION (compatible)" ""
    else
        check_status 1 "Lidarr $LIDARR_VERSION (too old)" "Upgrade to Lidarr 4.0.0+"
    fi
fi

# 4. Check local AI providers
echo ""
echo "4. Local AI Providers:"

# Ollama
if curl -s http://localhost:11434/api/tags >/dev/null 2>&1; then
    MODELS=$(curl -s http://localhost:11434/api/tags | jq -r '.models[].name' 2>/dev/null | wc -l)
    check_status 0 "Ollama running ($MODELS models available)" ""
else
    echo "⚠️  Ollama not running (optional)"
fi

# LM Studio
if curl -s http://localhost:1234/v1/models >/dev/null 2>&1; then
    check_status 0 "LM Studio running" ""
else
    echo "⚠️  LM Studio not running (optional)"
fi

# 5. Check plugin in Lidarr
echo ""
echo "5. Plugin Registration:"
IMPORT_LISTS=$(curl -s -H "X-Api-Key: YOUR_API_KEY" http://localhost:8686/api/v1/importlist 2>/dev/null)
if echo "$IMPORT_LISTS" | grep -q "Brainarr"; then
    check_status 0 "Brainarr registered in Lidarr" ""
else
    check_status 1 "Brainarr not registered" "Restart Lidarr and check logs"
fi

# 6. Check recent errors
echo ""
echo "6. Recent Errors:"
ERROR_COUNT=$(grep -i "brainarr.*error" /var/log/lidarr/lidarr.txt 2>/dev/null | tail -20 | wc -l)
WARNING_COUNT=$(grep -i "brainarr.*warn" /var/log/lidarr/lidarr.txt 2>/dev/null | tail -20 | wc -l)
echo "   Errors: $ERROR_COUNT | Warnings: $WARNING_COUNT (last 20 entries)"

if [ $ERROR_COUNT -gt 0 ]; then
    echo "   Recent errors:"
    grep -i "brainarr.*error" /var/log/lidarr/lidarr.txt | tail -3
fi

# 7. Check disk space
echo ""
echo "7. Disk Space:"
DISK_USAGE=$(df -h /var/lib/lidarr 2>/dev/null | awk 'NR==2 {print $5}' | sed 's/%//')
if [ ! -z "$DISK_USAGE" ] && [ "$DISK_USAGE" -lt 90 ]; then
    check_status 0 "Sufficient disk space (${DISK_USAGE}% used)" ""
else
    check_status 1 "Low disk space" "Free up disk space"
fi

echo ""
echo "=== Health Check Complete ==="
echo "For detailed troubleshooting, see: docs/TROUBLESHOOTING_COMPLETE.md"
```

### Quick Manual Checks

```bash
# Check if plugin is loaded
grep -i brainarr /var/log/lidarr/lidarr.txt | tail -20

# Check plugin files
ls -la /var/lib/lidarr/plugins/Brainarr/

# Test local providers
curl -s http://localhost:11434/api/tags | jq '.'  # Ollama
curl -s http://localhost:1234/v1/models | jq '.'   # LM Studio

# Check Lidarr API
curl -H "X-Api-Key: YOUR_API_KEY" http://localhost:8686/api/v1/system/status
```

---

## Installation Issues

### Issue: Plugin Not Appearing in Lidarr

#### Symptoms
- Brainarr doesn't appear in Settings → Import Lists → Add New
- No Brainarr option in dropdown menu
- Plugin files exist but not loaded

#### Root Causes & Solutions

**1. Incorrect Installation Path**

Verify the correct path for your platform:

| Platform | Plugin Path |
|----------|------------|
| Linux Standard | `/var/lib/lidarr/plugins/Brainarr/` |
| Docker | `/config/plugins/Brainarr/` |
| Windows | `C:\ProgramData\Lidarr\plugins\Brainarr\` |
| macOS | `~/Library/Application Support/Lidarr/plugins/Brainarr/` |

```bash
# Verify installation
PLUGIN_PATH="/var/lib/lidarr/plugins/Brainarr"  # Adjust for your platform
ls -la "$PLUGIN_PATH"

# Should contain these files:
# - plugin.json
# - Lidarr.Plugin.Brainarr.dll
# - Various dependency DLLs
```

**2. Missing or Corrupt plugin.json**

```bash
# Validate plugin.json
cat "$PLUGIN_PATH/plugin.json" | python -m json.tool

# Expected content:
{
  "name": "Brainarr",
  "version": "1.0.0",
  "description": "Multi-provider AI-powered music discovery...",
  "author": "Brainarr Team",
  "minimumVersion": "4.0.0.0",
  "entryPoint": "Lidarr.Plugin.Brainarr.dll"
}
```

**3. Permission Issues**

```bash
# Fix permissions (Linux/Docker)
sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins/Brainarr
sudo chmod 755 /var/lib/lidarr/plugins/Brainarr
sudo chmod 644 /var/lib/lidarr/plugins/Brainarr/*

# Verify
ls -la /var/lib/lidarr/plugins/Brainarr/
```

**4. Incompatible Lidarr Version**

```bash
# Check Lidarr version (minimum 4.0.0 required)
curl http://localhost:8686/api/v1/system/status | jq '.version'

# Update Lidarr if needed
# Follow official Lidarr update guide
```

**5. Missing .NET Dependencies**

```bash
# Check .NET runtime version
dotnet --list-runtimes | grep Microsoft.NETCore.App

# Should show 6.0 or higher
# If not, install .NET 6.0 runtime:
# https://dotnet.microsoft.com/download/dotnet/6.0
```

---

## Provider Connection Issues

### Ollama Connection Failed

#### Symptoms
- "Failed to connect to Ollama" error
- Test connection fails for Ollama
- No models detected

#### Solutions

```bash
# 1. Verify Ollama is running
systemctl status ollama
# or
ollama serve  # Run manually

# 2. Check Ollama API endpoint
curl http://localhost:11434/api/tags

# 3. List available models
ollama list

# 4. Pull a model if none available
ollama pull llama3.2:latest
ollama pull qwen2.5:14b

# 5. Test model generation
curl -X POST http://localhost:11434/api/generate -d '{
  "model": "llama3.2:latest",
  "prompt": "Test",
  "stream": false
}'

# 6. Check firewall
sudo ufw status
# Allow if needed:
sudo ufw allow 11434/tcp
```

### LM Studio Connection Failed

#### Solutions

```bash
# 1. Verify LM Studio server is running
# In LM Studio UI: Click "Start Server"

# 2. Check endpoint
curl http://localhost:1234/v1/models

# 3. Verify model is loaded
# In LM Studio: Load a model before starting server

# 4. Check server settings
# Default should be:
# - Host: localhost
# - Port: 1234
# - Enable CORS: Yes
```

### Cloud Provider Connection Issues

#### OpenAI Connection Failed

```bash
# Test API key
curl https://api.openai.com/v1/models \
  -H "Authorization: Bearer YOUR_API_KEY"

# Common issues:
# - Invalid API key format
# - Expired API key
# - Rate limit exceeded
# - Billing issues
```

#### Anthropic Connection Failed

```bash
# Test API key
curl https://api.anthropic.com/v1/messages \
  -H "x-api-key: YOUR_API_KEY" \
  -H "anthropic-version: 2023-06-01" \
  -H "content-type: application/json" \
  -d '{"model":"claude-3-haiku-20240307","messages":[{"role":"user","content":"test"}],"max_tokens":10}'
```

---

## Recommendation Generation Issues

### No Recommendations Generated

#### Diagnosis Checklist

1. **Library Size Check**
```bash
# Check artist count (minimum 10 required)
curl -H "X-Api-Key: YOUR_API_KEY" http://localhost:8686/api/v1/artist | jq '. | length'
```

2. **Provider Health Check**
```bash
# In Brainarr settings, click "Test" button
# Check logs for specific errors
tail -f /var/log/lidarr/lidarr.txt | grep -i brainarr
```

3. **Configuration Validation**
```yaml
# Review settings:
Discovery Mode: Similar  # Try: Adjacent, Exploratory
Max Recommendations: 20  # Increase to 50
Minimum Confidence: 0.5  # Lower to 0.3
Include Genres: All      # Or specify genres
Exclude Genres: None     # Remove restrictions
```

4. **Cache Check**
```bash
# Clear cache if stale recommendations
# Restart Lidarr to clear internal cache
systemctl restart lidarr
```

### Poor Quality Recommendations

#### Improvement Steps

1. **Adjust Discovery Mode**
   - **Similar**: Conservative, stays close to your taste
   - **Adjacent**: Moderate exploration of related genres
   - **Exploratory**: Aggressive discovery of new music

2. **Improve Library Context**
```yaml
# Add more context in settings:
Library Context: Detailed
Include Play Counts: Yes
Include Ratings: Yes
Analysis Depth: Deep
```

3. **Provider Selection**
   - **Best Quality**: Anthropic Claude, OpenAI GPT-4
   - **Best Speed**: Groq, Local providers
   - **Best Value**: DeepSeek, Gemini

4. **Fine-tune Prompts**
```yaml
# Advanced settings:
Custom Prompt Prefix: "Focus on progressive rock and jazz fusion"
Exclude Artists: "Mainstream pop artists"
Era Preference: "1970s-1980s"
```

---

## Performance Issues

### Slow Recommendation Generation

#### Diagnosis

```bash
# Monitor generation time
time curl -X POST http://localhost:8686/api/v1/command \
  -H "X-Api-Key: YOUR_API_KEY" \
  -d '{"name":"BrainarrFetchRecommendations"}'

# Check provider response times in logs
grep "Provider response time" /var/log/lidarr/lidarr.txt
```

#### Solutions

1. **Use Faster Providers**
   - Local: Ollama with smaller models (7B-14B)
   - Cloud: Groq (10x faster inference)

2. **Enable Caching**
```yaml
Cache Duration: 120 minutes
Cache Size: 1000 entries
```

3. **Optimize Model Selection**
```bash
# For Ollama, use faster models:
ollama pull llama3.2:3b  # Fastest
ollama pull qwen2.5:7b   # Good balance
```

4. **Reduce Context Size**
```yaml
Library Analysis: Basic  # Instead of Detailed
Max Artists to Analyze: 50  # Instead of All
```

### High Memory Usage

```bash
# Monitor memory
ps aux | grep -i lidarr
htop  # Interactive monitor

# Solutions:
# 1. Reduce cache size
# 2. Lower max recommendations
# 3. Use smaller AI models
# 4. Increase system RAM
```

---

## API Key Issues

### API Key Not Saving

```yaml
# Symptoms:
- API key field empty after save
- "Invalid API key" after entering valid key
- Settings don't persist

# Solutions:
1. Check Lidarr config permissions:
   sudo chown -R lidarr:lidarr /var/lib/lidarr/config

2. Verify database write access:
   sqlite3 /var/lib/lidarr/lidarr.db "PRAGMA integrity_check"

3. Check for special characters in API key
   - Remove any trailing spaces
   - Ensure no line breaks
```

### API Key Security

```bash
# Verify API keys are encrypted in database
sqlite3 /var/lib/lidarr/lidarr.db \
  "SELECT * FROM Config WHERE Key LIKE '%Brainarr%ApiKey%'"

# Should show encrypted/obfuscated values, not plain text
```

---

## Error Messages Reference

### Common Error Messages and Solutions

| Error Message | Cause | Solution |
|--------------|-------|----------|
| "Provider not available" | Provider offline or misconfigured | Check provider status and settings |
| "Rate limit exceeded" | Too many API requests | Wait and reduce request frequency |
| "Invalid API key" | Wrong or expired API key | Verify API key in provider dashboard |
| "No models available" | Local provider has no models | Download models (e.g., `ollama pull llama3.2`) |
| "Timeout waiting for response" | Slow provider or network | Increase timeout or use faster provider |
| "Failed to parse response" | Provider returned invalid JSON | Check provider compatibility/version |
| "Insufficient library data" | Too few artists | Add more artists (minimum 10) |
| "Connection refused" | Service not running | Start the provider service |
| "SSL/TLS error" | Certificate issues | Check system certificates and time |
| "Out of memory" | Insufficient RAM | Reduce cache size or add RAM |

---

## Debug Procedures

### Enable Debug Logging

1. **Lidarr Debug Mode**
```yaml
# Settings → General → Logging
Log Level: Debug
```

2. **Provider-Specific Debugging**
```yaml
# Brainarr Settings → Advanced
Debug Mode: Enabled
Log API Requests: Yes
Log API Responses: Yes (careful - may log sensitive data)
```

3. **Live Log Monitoring**
```bash
# Watch Brainarr-specific logs
tail -f /var/log/lidarr/lidarr.txt | grep -i brainarr

# Watch with highlighting
tail -f /var/log/lidarr/lidarr.txt | grep --color=always -E "brainarr|error|warn"
```

### Provider Testing

```bash
# Test each provider individually
# Create test script: test-provider.sh

#!/bin/bash
PROVIDER=$1
API_KEY=$2

case $PROVIDER in
  ollama)
    curl -X POST http://localhost:11434/api/generate \
      -d '{"model":"llama3.2","prompt":"List 3 rock albums","stream":false}'
    ;;
  openai)
    curl https://api.openai.com/v1/chat/completions \
      -H "Authorization: Bearer $API_KEY" \
      -H "Content-Type: application/json" \
      -d '{"model":"gpt-4o-mini","messages":[{"role":"user","content":"List 3 rock albums"}]}'
    ;;
  anthropic)
    curl https://api.anthropic.com/v1/messages \
      -H "x-api-key: $API_KEY" \
      -H "anthropic-version: 2023-06-01" \
      -H "content-type: application/json" \
      -d '{"model":"claude-3-haiku-20240307","messages":[{"role":"user","content":"List 3 rock albums"}],"max_tokens":100}'
    ;;
esac
```

---

## Log Analysis

### Key Log Patterns to Search

```bash
# Startup issues
grep "Loading plugin.*Brainarr" /var/log/lidarr/lidarr.txt

# Configuration issues
grep "Brainarr.*configuration" /var/log/lidarr/lidarr.txt

# Provider issues
grep "Provider.*failed\|unavailable\|error" /var/log/lidarr/lidarr.txt

# Recommendation generation
grep "Generating recommendations\|recommendations generated" /var/log/lidarr/lidarr.txt

# Performance metrics
grep "response time\|duration\|took.*ms" /var/log/lidarr/lidarr.txt
```

### Log Analysis Script

```bash
#!/bin/bash
# analyze-brainarr-logs.sh

LOG_FILE="/var/log/lidarr/lidarr.txt"
TEMP_FILE="/tmp/brainarr-analysis.txt"

echo "=== Brainarr Log Analysis ==="
echo "Analyzing: $LOG_FILE"
echo ""

# Extract Brainarr logs
grep -i brainarr "$LOG_FILE" > "$TEMP_FILE"

# Count by severity
echo "Log Levels:"
echo "  Errors: $(grep -c ERROR "$TEMP_FILE")"
echo "  Warnings: $(grep -c WARN "$TEMP_FILE")"
echo "  Info: $(grep -c INFO "$TEMP_FILE")"
echo "  Debug: $(grep -c DEBUG "$TEMP_FILE")"
echo ""

# Provider statistics
echo "Provider Mentions:"
for provider in ollama lmstudio openai anthropic gemini groq deepseek perplexity openrouter; do
  COUNT=$(grep -ic "$provider" "$TEMP_FILE")
  if [ $COUNT -gt 0 ]; then
    echo "  $provider: $COUNT"
  fi
done
echo ""

# Common errors
echo "Common Issues:"
echo "  Connection failures: $(grep -c "connection.*failed\|refused" "$TEMP_FILE")"
echo "  Timeouts: $(grep -c "timeout\|timed out" "$TEMP_FILE")"
echo "  API errors: $(grep -c "api.*error\|401\|403\|429" "$TEMP_FILE")"
echo "  Parse errors: $(grep -c "parse.*error\|invalid.*json" "$TEMP_FILE")"
echo ""

# Recent errors (last 5)
echo "Recent Errors:"
grep ERROR "$TEMP_FILE" | tail -5

# Cleanup
rm "$TEMP_FILE"
```

---

## Platform-Specific Issues

### Docker

```bash
# Volume permission issues
docker exec lidarr ls -la /config/plugins/Brainarr/

# Fix permissions
docker exec lidarr chown -R abc:abc /config/plugins/Brainarr

# Network issues (can't reach local Ollama)
# Use host networking or proper container links
docker run -d --network host lidarr
```

### Windows

```powershell
# Permission issues
icacls "C:\ProgramData\Lidarr\plugins\Brainarr" /grant "NT AUTHORITY\SYSTEM:(OI)(CI)F"

# Service issues
Get-Service Lidarr | Restart-Service

# Firewall issues
New-NetFirewallRule -DisplayName "Ollama" -Direction Inbound -LocalPort 11434 -Protocol TCP -Action Allow
```

### Synology NAS

```bash
# Different paths
PLUGIN_PATH="/volume1/@appstore/lidarr/var/plugins/Brainarr"

# Permission fix
synopkg stop Lidarr
chown -R lidarr:lidarr "$PLUGIN_PATH"
synopkg start Lidarr
```

### Unraid

```bash
# Plugin path in appdata
PLUGIN_PATH="/mnt/user/appdata/lidarr/plugins/Brainarr"

# Fix permissions
chown -R nobody:users "$PLUGIN_PATH"
chmod -R 755 "$PLUGIN_PATH"
```

---

## Getting Help

### Before Asking for Help

1. Run the health check script
2. Collect relevant logs (last 100 lines)
3. Document your configuration
4. List steps to reproduce issue

### Information to Provide

```markdown
**Environment:**
- OS: [e.g., Ubuntu 22.04, Windows 11, Docker]
- Lidarr Version: [e.g., 4.0.0.1234]
- Brainarr Version: [e.g., 1.0.0]
- Provider: [e.g., Ollama, OpenAI]

**Issue Description:**
[Clear description of the problem]

**Steps to Reproduce:**
1. [First step]
2. [Second step]
3. [Observed result]

**Expected Behavior:**
[What should happen]

**Logs:**
```
[Relevant log entries]
```

**Health Check Output:**
```
[Output from health check script]
```
```

### Support Channels

1. **GitHub Issues**: https://github.com/YourRepo/Brainarr/issues
2. **Documentation**: /docs folder in repository
3. **Community Forum**: Lidarr forums
4. **Discord**: Lidarr Discord server

---

## Prevention & Best Practices

### Regular Maintenance

```bash
# Weekly tasks
- Check for Brainarr updates
- Review error logs
- Verify provider connectivity
- Update AI models (for local providers)

# Monthly tasks
- Clean up old cache entries
- Review API usage and costs
- Update provider API keys if needed
- Check disk space

# Quarterly tasks
- Review and optimize settings
- Update Lidarr
- Audit security settings
- Backup configuration
```

### Monitoring Script

```bash
#!/bin/bash
# monitor-brainarr.sh - Run via cron

# Check if Brainarr is responding
if ! grep -q "Brainarr.*Fetch completed" /var/log/lidarr/lidarr.txt; then
  echo "Warning: No recent Brainarr activity" | mail -s "Brainarr Alert" admin@example.com
fi

# Check error rate
ERROR_COUNT=$(grep -c "Brainarr.*ERROR" /var/log/lidarr/lidarr.txt)
if [ $ERROR_COUNT -gt 10 ]; then
  echo "High error rate: $ERROR_COUNT errors" | mail -s "Brainarr Errors" admin@example.com
fi
```

---

## Quick Reference Card

### Essential Commands

```bash
# Service Management
systemctl status/start/stop/restart lidarr
docker restart lidarr

# Log Viewing
tail -f /var/log/lidarr/lidarr.txt | grep -i brainarr
journalctl -u lidarr -f

# Provider Testing
curl http://localhost:11434/api/tags           # Ollama
curl http://localhost:1234/v1/models           # LM Studio

# Configuration
sqlite3 /var/lib/lidarr/lidarr.db ".tables"    # View DB
cat /var/lib/lidarr/plugins/Brainarr/plugin.json

# Permissions Fix
chown -R lidarr:lidarr /var/lib/lidarr/plugins/Brainarr
chmod 755 /var/lib/lidarr/plugins/Brainarr
```

### File Locations

| Item | Linux | Windows | Docker |
|------|-------|---------|--------|
| Plugin | `/var/lib/lidarr/plugins/Brainarr/` | `C:\ProgramData\Lidarr\plugins\Brainarr\` | `/config/plugins/Brainarr/` |
| Logs | `/var/log/lidarr/lidarr.txt` | `C:\ProgramData\Lidarr\logs\lidarr.txt` | `/config/logs/lidarr.txt` |
| Config DB | `/var/lib/lidarr/lidarr.db` | `C:\ProgramData\Lidarr\lidarr.db` | `/config/lidarr.db` |
| Lidarr Config | `/var/lib/lidarr/config.xml` | `C:\ProgramData\Lidarr\config.xml` | `/config/config.xml` |