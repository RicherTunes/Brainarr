# Is Brainarr Working? Verification Checklist

Quick checklist to verify your Brainarr installation is working correctly.

## Quick Health Check

Run through these checks in order. If any step fails, see the troubleshooting section below.

### 1. Plugin Loaded

- [ ] Go to **Settings > General > About**
- [ ] Look for "Brainarr" in the plugins list
- [ ] Version number should match your installed version

**Expected:** Brainarr appears in the plugins list with correct version.

### 2. Import List Visible

- [ ] Go to **Settings > Import Lists**
- [ ] Click **Add** (+ button)
- [ ] Search for "Brainarr"
- [ ] Brainarr should appear in the list

**Expected:** Brainarr appears as an available import list type.

### 3. Provider Connection

- [ ] Add a new Brainarr import list (or edit existing)
- [ ] Select your AI provider (e.g., Ollama, OpenAI, Gemini)
- [ ] Enter required credentials (API key, URL, etc.)
- [ ] Click **Test**

**Expected:** Green checkmark with "Settings validated" message.

### 4. Model Detection (Local Providers)

If using Ollama or LM Studio:

- [ ] Ensure local AI is running (`ollama serve` or LM Studio server)
- [ ] In Brainarr settings, click **Test**
- [ ] Check the Model dropdown populates with available models

**Expected:** Dropdown shows models from your local instance.

### 5. Recommendations Generated

- [ ] Save your Brainarr import list configuration
- [ ] Click **Sync** (or wait for scheduled sync)
- [ ] Check **Activity > Queue** for processing
- [ ] Check **Wanted > Missing** for new recommendations

**Expected:** New artists/albums appear based on your library analysis.

### 6. Log Verification

- [ ] Go to **System > Logs**
- [ ] Filter for "Brainarr" or "ImportList"
- [ ] Look for successful recommendation fetch messages

**Expected:** Logs show "Fetched X recommendations from Brainarr" or similar.

## Status Indicators

| Indicator | Meaning | Action |
|-----------|---------|--------|
| Green checkmark on Test | Provider connected successfully | None needed |
| Red X on Test | Connection failed | Check credentials/URL |
| Empty recommendations | AI returned no results | Check library size, adjust settings |
| Rate limit errors | Too many API calls | Wait or switch provider |
| Timeout errors | Provider slow/unavailable | Increase timeout or use fallback |

## Common Issues Quick Fixes

### "Provider not available"

1. For local providers: Verify service is running
2. For cloud providers: Check API key validity
3. Try the **Test** button again

### "No recommendations returned"

1. Ensure you have at least 10 artists in your library
2. Check Discovery Mode setting (try "Similar" first)
3. Verify the AI model is capable of music recommendations

### "Connection timeout"

1. Increase timeout in Advanced Settings (default: 60s)
2. For local AI: Check system resources
3. Try a faster model or provider

### "Rate limited"

1. Enable caching in settings
2. Reduce sync frequency
3. Consider a fallback provider

## Verification Commands

### Check Lidarr Logs (Docker)

```bash
docker logs lidarr 2>&1 | grep -i brainarr
```

### Check Ollama Status

```bash
curl http://localhost:11434/api/tags
```

### Check LM Studio Status

```bash
curl http://localhost:1234/v1/models
```

## Still Not Working?

1. **Enable Debug Logging**: Settings > General > Log Level = Debug
2. **Restart Lidarr**: Sometimes required after plugin updates
3. **Check GitHub Issues**: [Brainarr Issues](https://github.com/RicherTunes/Brainarr/issues)
4. **Review Troubleshooting Guide**: [TROUBLESHOOTING.md](TROUBLESHOOTING.md)

## Success Indicators

Your Brainarr is working correctly when:

- [ ] Plugin appears in Settings > General > About
- [ ] Test connection succeeds with green checkmark
- [ ] Recommendations appear after sync
- [ ] No error messages in System > Logs
- [ ] New artists/albums added to Wanted list

---

*Last updated: November 2025*
