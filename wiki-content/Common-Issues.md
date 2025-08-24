# Common Issues & Solutions

Quick solutions to frequently encountered problems with Brainarr.

## 🚨 Critical Issues (Fix Immediately)

### Plugin Not Appearing in Import Lists

**Symptoms**: Brainarr option missing when creating new import list

**Solutions**:
1. **Check File Permissions**
   ```bash
   # Linux
   sudo chown -R lidarr:lidarr /var/lib/lidarr/plugins/Brainarr
   sudo chmod -R 755 /var/lib/lidarr/plugins/Brainarr
   
   # Windows (PowerShell as Admin)
   icacls "C:\ProgramData\Lidarr\plugins\Brainarr" /grant "Users:(OI)(CI)F"
   ```

2. **Verify File Structure**
   ```
   plugins/Brainarr/
   ├── Brainarr.Plugin.dll
   ├── plugin.json
   ├── (other .dll files)
   ```

3. **Full Lidarr Restart** - Stop service, wait 30 seconds, start service

4. **Check Logs**
   - Location: Lidarr data directory → `logs/lidarr.txt`
   - Look for: Plugin loading errors, .NET runtime issues

**If still failing**: Reinstall plugin with correct permissions

---

### Connection Test Fails

**Symptoms**: "Test" button returns connection errors

#### Local Providers (Ollama/LM Studio)

**Solutions**:
1. **Verify Service Running**
   ```bash
   # Ollama
   curl http://localhost:11434/api/tags
   
   # LM Studio  
   curl http://localhost:1234/v1/models
   ```

2. **Check URL Format**
   - Ollama: `http://localhost:11434` (not https)
   - LM Studio: `http://localhost:1234`
   - Include `http://` prefix

3. **Firewall Issues**
   ```bash
   # Linux - allow local ports
   sudo ufw allow 11434
   sudo ufw allow 1234
   
   # Windows - check Windows Defender Firewall
   ```

#### Cloud Providers

**Solutions**:
1. **Verify API Key**
   - Check for extra spaces or characters
   - Ensure key hasn't expired
   - Test key with provider's official tools

2. **Check Network Connectivity**
   ```bash
   # Test provider endpoints
   curl -I https://api.openai.com/v1/models
   curl -I https://api.anthropic.com/v1/messages
   ```

3. **Proxy/Corporate Firewall**
   - Configure proxy settings if required
   - Whitelist provider domains

---

### No Recommendations Generated

**Symptoms**: Fetch completes but returns 0 recommendations

**Solutions**:
1. **Library Size Check**
   - Minimum: 10 artists required
   - Recommended: 25+ artists for quality results

2. **Configuration Issues**
   ```yaml
   # Check these settings
   Max Recommendations: > 0
   Discovery Mode: Not blank
   Provider Model: Selected
   ```

3. **Provider-Specific Issues**
   - **Local**: Ensure model is loaded and running
   - **Cloud**: Check API quota/rate limits
   - **All**: Enable debug logging for detailed error info

4. **Cache Issues**
   - Clear cache if getting stale results
   - Reduce cache duration temporarily

**Debug Steps**:
1. Enable debug logging in Brainarr settings
2. Run manual fetch
3. Check logs for specific error messages
4. Test with minimal configuration first

---

## ⚠️ Configuration Issues

### Settings Not Saving

**Symptoms**: Configuration reverts after clicking Save

**Solutions**:
1. **Browser Console Errors**
   - Press F12, check Console tab for JavaScript errors
   - Refresh page and retry

2. **Validation Errors**
   - Ensure all required fields completed
   - Check API key format
   - Verify URL formatting

3. **Lidarr Permissions**
   ```bash
   # Linux
   sudo chown -R lidarr:lidarr /var/lib/lidarr/
   
   # Check database permissions
   ls -la /var/lib/lidarr/lidarr.db
   ```

4. **Database Issues**
   - Stop Lidarr
   - Backup `lidarr.db`
   - Restart Lidarr

---

### Model Selection Empty

**Symptoms**: Model dropdown shows no options

**Solutions**:
1. **Click Test First** - Model list populates after successful connection test

2. **Local Provider Issues**
   ```bash
   # Ollama - check available models
   ollama list
   
   # If empty, pull a model
   ollama pull llama3.2
   ```

3. **LM Studio Issues**
   - Ensure a model is loaded in LM Studio
   - Start local server in Developer tab
   - Verify server status shows "Running"

4. **Cloud Provider Issues**
   - Models are predefined, should appear after test
   - If empty, check API key validity

---

## 🐌 Performance Issues

### Slow Recommendation Generation

**Symptoms**: Takes several minutes to generate recommendations

**Solutions**:
1. **Optimize Library Sampling**
   ```yaml
   # Reduce processing time
   Library Sampling: Minimal
   Max Recommendations: 5-10
   ```

2. **Local Provider Optimization**
   - Use smaller models (7B instead of 70B)
   - Close unnecessary applications
   - Ensure adequate RAM available

3. **Cloud Provider Optimization**
   - Use faster providers (Groq, DeepSeek)
   - Enable caching to avoid repeated requests
   - Check provider status pages

4. **Network Issues**
   - Test internet speed and stability
   - Consider using local providers if network is slow

---

### High Memory Usage

**Symptoms**: System slows down during recommendation generation

**Solutions**:
1. **Local Provider RAM Usage**
   ```bash
   # Check memory usage
   free -h  # Linux
   vm_stat  # macOS
   
   # Use smaller models
   ollama pull mistral:7b  # Instead of llama3.1:70b
   ```

2. **System Optimization**
   - Close other applications
   - Increase swap file size
   - Consider RAM upgrade for large models

3. **Model Management**
   ```bash
   # Ollama - remove unused models
   ollama rm unused-model-name
   
   # Check model sizes
   ollama list
   ```

---

## 💰 Cost Issues

### Unexpected API Charges

**Symptoms**: Higher than expected bills from cloud providers

**Solutions**:
1. **Enable Rate Limiting**
   ```yaml
   Rate Limiting: Enabled
   Requests per Minute: 5-10
   ```

2. **Optimize Caching**
   ```yaml
   Cache Duration: 240 minutes (4 hours)
   Enable Caching: Yes
   ```

3. **Reduce Usage**
   ```yaml
   Max Recommendations: 5
   Fetch Interval: Weekly instead of daily
   Discovery Mode: Similar (less processing)
   ```

4. **Switch to Cheaper Providers**
   - DeepSeek: $0.14/M tokens (vs GPT-4 $30/M)
   - Gemini: Free tier available
   - Local providers: Completely free

5. **Monitor Usage**
   - Enable debug logging to see token usage
   - Check provider dashboards regularly
   - Set up usage alerts when available

---

## 🤖 Provider-Specific Issues

### Ollama Issues

**"Service not found"**
```bash
# Check if ollama is installed
ollama --version

# Start service manually
ollama serve

# Check if running
ps aux | grep ollama
```

**"Model not loaded"**
```bash
# List available models
ollama list

# Pull recommended model if missing
ollama pull llama3.2

# Check model status
ollama ps
```

---

### LM Studio Issues

**"Server not running"**
1. Open LM Studio application
2. Go to Developer tab
3. Ensure a model is selected
4. Click "Start Server"
5. Verify status shows "Running"

**"Model failed to load"**
1. Check available RAM vs model requirements
2. Try smaller model if insufficient memory
3. Close other applications
4. Restart LM Studio

---

### Cloud Provider Issues

**OpenAI "Invalid API key"**
1. Verify key format: `sk-...` (starts with sk-)
2. Check for extra spaces or characters
3. Test key at [platform.openai.com](https://platform.openai.com)
4. Ensure billing is set up

**Anthropic "Rate limited"**  
1. Check usage limits at console.anthropic.com
2. Enable rate limiting in Brainarr
3. Increase cache duration
4. Reduce request frequency

**Google "Quota exceeded"**
1. Check quota at [console.cloud.google.com](https://console.cloud.google.com)
2. Wait for quota reset (usually daily)
3. Reduce max recommendations
4. Enable longer caching

---

## 📊 Quality Issues

### Poor Recommendation Quality

**Symptoms**: Recommendations don't match your taste

**Solutions**:
1. **Adjust Discovery Mode**
   - **Similar**: More conservative, closer to current taste
   - **Adjacent**: Balanced exploration
   - **Exploratory**: More adventurous

2. **Change Provider**
   - **Higher quality**: OpenAI GPT-4, Anthropic Claude
   - **Different personality**: Try multiple providers
   - **Local consistency**: Ollama with larger models

3. **Improve Library Analysis**
   ```yaml
   Library Sampling: Comprehensive
   Max Recommendations: 15-20
   ```

4. **Give More Context**
   - Ensure library has diverse metadata
   - Use descriptive tags in Lidarr
   - Have sufficient library size (25+ artists)

---

### Duplicate Recommendations

**Symptoms**: Same albums recommended repeatedly

**Solutions**:
1. **Clear Cache**
   - Reduce cache duration temporarily
   - Force fresh recommendations

2. **Adjust Provider Settings**
   ```yaml
   Discovery Mode: Exploratory
   Max Recommendations: Increase number
   ```

3. **Library Expansion**
   - Add more diverse artists to library
   - Ensure metadata is complete and accurate

---

## 🔧 Debug & Diagnostics

### Enable Debug Logging

1. **In Brainarr Settings**:
   ```yaml
   Debug Logging: Enabled
   Log API Requests: Enabled  
   Log Token Usage: Enabled
   ```

2. **Check Logs**:
   - Location: Lidarr data directory → `logs/`
   - Look for entries containing "Brainarr"
   - Note timestamps and error messages

3. **Common Debug Info**:
   ```
   [Debug] Brainarr: Library analysis found 45 artists
   [Debug] Brainarr: Generated prompt (1,234 tokens)
   [Info] Brainarr: Received 8 recommendations from provider
   [Error] Brainarr: Connection failed - timeout after 30s
   ```

### Log Analysis

**Connection Issues**: Look for "timeout", "refused", "unauthorized"
**Quality Issues**: Check "generated prompt", "library analysis"
**Performance Issues**: Note timestamps between operations

---

## 🆘 Emergency Fixes

### Complete Reset

If everything breaks:

1. **Stop Lidarr**
2. **Backup Configuration**
   ```bash
   cp /var/lib/lidarr/lidarr.db /var/lib/lidarr/lidarr.db.backup
   ```
3. **Remove Plugin**
   ```bash
   rm -rf /var/lib/lidarr/plugins/Brainarr
   ```
4. **Restart Lidarr**
5. **Fresh Installation** following [Installation Guide](Installation-Guide)

### Restore Defaults

To restore Brainarr to default settings:

1. Delete Brainarr import list in Lidarr
2. Recreate with basic configuration
3. Test with minimal settings first
4. Gradually add advanced features

---

## 📞 Getting Additional Help

### Before Asking for Help

Check completed:
- [ ] Followed relevant troubleshooting steps above
- [ ] Checked [FAQ](FAQ) for your specific question
- [ ] Enabled debug logging and reviewed logs
- [ ] Tested with minimal/default configuration
- [ ] Verified provider works independently

### How to Report Issues

Include in your report:
1. **Environment**: OS, Lidarr version, Brainarr version
2. **Configuration**: Provider type, key settings (no API keys)
3. **Problem**: Expected vs actual behavior
4. **Logs**: Relevant error messages with timestamps
5. **Steps**: How to reproduce the issue

### Support Resources

1. **[Troubleshooting Guide](Troubleshooting-Guide)** - Systematic problem solving
2. **[Provider Troubleshooting](Provider-Troubleshooting)** - Provider-specific issues
3. **[FAQ](FAQ)** - Common questions and answers
4. **GitHub Issues** - Community support and bug reports

**Most issues are configuration-related and can be solved by carefully following the setup guides!**