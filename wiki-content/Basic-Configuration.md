# Basic Configuration

Essential configuration settings to get Brainarr up and running quickly.

## Quick Setup Checklist

- [ ] Brainarr plugin installed and visible in Import Lists
- [ ] AI provider chosen and configured
- [ ] Basic import settings configured
- [ ] Test connection successful
- [ ] First recommendations generated

## Core Settings

### Import List Configuration

Navigate to **Settings → Import Lists → Add (+) → Brainarr**

#### Basic Settings
| Setting | Recommended Value | Description |
|---------|-------------------|-------------|
| **Name** | "AI Music Recommendations" | Display name for this import list |
| **Enable** | ✅ Yes | Activate this import list |
| **Enable Automatic Add** | ✅ Yes | Automatically add recommendations |
| **Monitor** | "All Albums" | Monitor all recommended albums |
| **Search on Add** | ✅ Yes | Search for albums immediately |
| **Root Folder** | `/music` (your path) | Where to save music files |
| **Quality Profile** | "Any" | Quality settings for downloads |
| **Metadata Profile** | "Standard" | Metadata fetching preferences |
| **Tags** | `ai-recommendations` | Tag recommendations for easy identification |

#### Schedule Settings
| Setting | Recommended Value | Description |
|---------|-------------------|-------------|
| **Interval** | "7 days" | How often to fetch new recommendations |
| **Time** | "2:00 AM" | When to run automatic fetches |
| **Max Recommendations** | "10" | Number of recommendations per fetch |

### Provider Configuration

Choose your AI provider based on your priorities:

#### For Privacy (Recommended)
**Provider**: 🏠 Ollama (Local, Private)
- **Cost**: Free
- **Privacy**: 100% local
- **Setup**: See [Local Providers Guide](Local-Providers#ollama)

#### For Getting Started
**Provider**: 🆓 Google Gemini (Free Tier)
- **Cost**: Free tier available
- **Privacy**: Cloud-based
- **Setup**: See [Cloud Providers Guide](Cloud-Providers#google-gemini)

#### For Best Quality
**Provider**: 🤖 OpenAI GPT-4 or 🧠 Anthropic Claude
- **Cost**: Pay-per-use
- **Privacy**: Cloud-based
- **Setup**: See [Cloud Providers Guide](Cloud-Providers)

## Recommendation Settings

### Discovery Mode
Choose how adventurous your recommendations should be:

| Mode | Description | Best For | Example |
|------|-------------|----------|---------|
| **Similar** | Very close to current taste | Building core collection | If you like Pink Floyd → More prog rock |
| **Adjacent** | Related genres and styles | Expanding musical horizons | If you like Metal → Hard rock, punk |
| **Exploratory** | New musical territories | Musical exploration | If you like Rock → Jazz, electronic, world |

**Recommendation**: Start with "Adjacent" for a good balance of familiar and new.

### Recommendation Mode
Choose between recommending albums or artists:

| Mode | Description | Result | Best For |
|------|-------------|--------|----------|
| **Specific Albums** | Recommends individual albums | Targeted additions | Curated collections |
| **Artists** | Recommends entire artist catalogs | Complete discographies | Comprehensive libraries |

**Recommendation**: Start with "Specific Albums" to avoid overwhelming your library.

### Library Sampling
Control how much of your library Brainarr analyzes:

| Setting | Processing Time | Accuracy | Best For |
|---------|----------------|----------|----------|
| **Minimal** | Fast | Good | Large libraries (1000+ albums) |
| **Balanced** | Medium | Better | Medium libraries (100-1000 albums) |
| **Comprehensive** | Slow | Best | Small libraries (<100 albums) |

**Recommendation**: Use "Balanced" unless you have performance issues.

## Advanced Settings

### Caching
| Setting | Recommended | Description |
|---------|-------------|-------------|
| **Cache Duration** | "60 minutes" | How long to cache recommendations |
| **Enable Caching** | ✅ Yes | Reduces API costs and improves speed |

### Rate Limiting
| Setting | Recommended | Description |
|---------|-------------|-------------|
| **Requests per Minute** | "10" | Limit API requests (cloud providers) |
| **Enable Rate Limiting** | ✅ Yes | Prevents API overuse charges |

### Debug Settings
| Setting | Default | When to Change |
|---------|---------|----------------|
| **Debug Logging** | ❌ No | Enable for troubleshooting |
| **Log API Requests** | ❌ No | Enable to debug provider issues |
| **Log Token Usage** | ❌ No | Enable to monitor costs |

## Testing Your Configuration

### Step 1: Test Connection
1. In Brainarr settings, click **Test**
2. Expected result: "Connection successful!"
3. Should also show available models (for local providers)

**If test fails**: See [Provider Troubleshooting](Provider-Troubleshooting)

### Step 2: Generate Test Recommendations
1. Click **Save** to save your settings
2. Go back to Import Lists page
3. Find your Brainarr entry and click **Fetch Now**
4. Check **Activity → History** for results

**If no recommendations**: See [Common Issues](Common-Issues#no-recommendations)

### Step 3: Verify Recommendations
1. Check that recommendations make sense for your library
2. Verify they're tagged with your specified tags
3. Confirm they're being added to the correct root folder

## Configuration Examples

### Example 1: Privacy-Focused Setup
```yaml
Provider: Ollama
Ollama URL: http://localhost:11434
Ollama Model: llama3.2
Discovery Mode: Adjacent
Recommendation Mode: Specific Albums
Max Recommendations: 5
Cache Duration: 120 minutes
```

### Example 2: Cost-Effective Cloud Setup
```yaml
Provider: Google Gemini
API Key: [your-free-api-key]
Model: gemini-1.5-flash
Discovery Mode: Similar
Recommendation Mode: Artists
Max Recommendations: 3
Cache Duration: 240 minutes
```

### Example 3: Premium Quality Setup
```yaml
Provider: OpenAI
API Key: [your-api-key]
Model: gpt-4o
Discovery Mode: Exploratory
Recommendation Mode: Specific Albums
Max Recommendations: 15
Cache Duration: 60 minutes
Rate Limit: 5 requests/minute
```

## Configuration Best Practices

### Start Small
- Begin with 5-10 recommendations
- Use "Similar" or "Adjacent" discovery mode
- Enable caching to reduce costs

### Monitor Performance
- Check Lidarr logs for errors
- Monitor API usage/costs (cloud providers)
- Adjust cache duration based on usage

### Iterative Improvement
1. **Week 1**: Use default settings
2. **Week 2**: Adjust discovery mode based on results
3. **Week 3**: Tune recommendation count
4. **Week 4**: Experiment with different providers

### Security Considerations
- Store API keys securely
- Use local providers for maximum privacy
- Enable rate limiting for cloud providers
- Regular key rotation for cloud providers

## Troubleshooting Configuration

### Settings Not Saving
1. Check Lidarr permissions
2. Restart Lidarr service
3. Check for validation errors in browser console

### Test Button Fails
1. Verify provider is running (local providers)
2. Check API key format (cloud providers)
3. Test network connectivity
4. Review debug logs

### Poor Recommendation Quality
1. Try different discovery modes
2. Increase library sampling depth
3. Switch to higher-quality provider
4. Check library analysis results

## Next Steps

After basic configuration:

1. **[Provider-Specific Optimization](Provider-Configuration-Overview)** - Fine-tune your chosen provider
2. **[Performance Tuning](Performance-Tuning)** - Optimize speed and costs
3. **[Advanced Configuration](Advanced-Configuration)** - Power user features
4. **[Troubleshooting Guide](Troubleshooting-Guide)** - Solve common issues

## Configuration Validation Checklist

### Required Settings ✅
- [ ] Provider selected and configured
- [ ] API key set (if using cloud provider)
- [ ] Root folder configured
- [ ] Quality profile selected
- [ ] Test connection successful

### Optional but Recommended ✅
- [ ] Tags configured for easy identification
- [ ] Caching enabled
- [ ] Rate limiting configured (cloud providers)
- [ ] Appropriate discovery mode selected
- [ ] Reasonable recommendation count set

### Testing ✅
- [ ] Test connection passes
- [ ] Manual fetch generates recommendations
- [ ] Recommendations appear in Activity history
- [ ] No errors in Lidarr logs
- [ ] Recommendations match expected quality/style

**Configuration complete?** Ready for **[Getting Your First Recommendations](Getting-Your-First-Recommendations)**!