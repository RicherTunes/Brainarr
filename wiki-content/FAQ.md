# Frequently Asked Questions (FAQ)

Common questions and answers about Brainarr.

## General Questions

### What is Brainarr?
Brainarr is an AI-powered import list plugin for Lidarr that generates intelligent music recommendations based on your existing library. It supports 8 different AI providers, from privacy-focused local options to powerful cloud services.

### How does Brainarr work?
1. **Analyzes your music library** - Examines your collection patterns, genres, and preferences
2. **Generates intelligent prompts** - Creates optimized prompts for AI providers
3. **Gets AI recommendations** - Queries your chosen AI provider for suggestions
4. **Integrates with Lidarr** - Adds recommendations as import list items for automatic downloading

### Is Brainarr free?
**The plugin itself is completely free**. However:
- **Local providers** (Ollama, LM Studio) are free forever
- **Cloud providers** may have costs (some offer free tiers)
- See [Provider Setup Overview](Provider-Setup-Overview) for cost details

### What's the difference between album and artist recommendations?
- **Album Mode**: Recommends specific albums (e.g., "Pink Floyd - Dark Side of the Moon")  
- **Artist Mode**: Recommends entire artists (Lidarr imports all their albums)
- Configure in **Recommendation Mode** setting

## Installation & Setup

### How do I install Brainarr?
See the complete [Installation Guide](Installation-Guide). Basic steps:
1. Download latest release or build from source
2. Copy to Lidarr plugins directory
3. Restart Lidarr
4. Configure in Settings → Import Lists

### Which AI provider should I choose?
Depends on your priorities:
- **Privacy**: Ollama or LM Studio (100% local)
- **Free**: Google Gemini (free tier)
- **Cheapest**: DeepSeek ($0.50-2/month)
- **Fastest**: Groq (500+ tokens/second)
- **Best quality**: OpenAI GPT-4 or Anthropic Claude

See [Provider Setup Overview](Provider-Setup-Overview) for detailed comparison.

### Can I use multiple providers?
Yes! You can create multiple Brainarr import lists, each configured with different providers, to compare results.

### Do I need technical skills to use Brainarr?
**Basic usage**: No technical skills required - follow the setup guides
**Advanced features**: Some familiarity with Lidarr configuration helpful
**Local providers**: Basic command-line knowledge useful but not required

## Privacy & Security

### Is my music library data private?
Depends on your provider choice:
- **Local providers** (Ollama, LM Studio): 100% private, data never leaves your server
- **Cloud providers**: Library metadata sent to AI service for analysis
- Most cloud providers don't store conversation data permanently

### What data is sent to AI providers?
For cloud providers, Brainarr sends:
- **Library statistics** (genre distribution, artist counts)
- **Sample artists and genres** (not complete library)
- **Metadata only** - no personal information or file paths

### How do I maximize privacy?
1. **Use local providers** (Ollama or LM Studio)
2. **Enable minimal library sampling**
3. **Use generic tags** instead of personal identifiers
4. **Review data before transmission** (enable debug logging)

## Performance & Costs

### How much do cloud providers cost?
Monthly estimates for typical usage:
- **Free**: Gemini free tier, Ollama, LM Studio
- **Ultra-low**: DeepSeek ($0.50-2.00)
- **Low**: Groq ($1-5)
- **Medium**: OpenRouter ($5-20), Perplexity ($5-20)
- **High**: OpenAI ($10-50), Anthropic ($15-60)

See [Cost Optimization](Cost-Optimization) for ways to reduce expenses.

### How can I reduce costs?
1. **Use local providers** (completely free)
2. **Enable caching** (reduces duplicate requests)
3. **Use cheaper providers** (DeepSeek, Gemini)
4. **Reduce recommendation frequency**
5. **Lower max recommendations per sync**

### Why are recommendations slow?
- **Local providers**: Limited by your hardware
- **Cloud providers**: Network latency and provider load
- **Large libraries**: More data to analyze
- **Complex prompts**: More processing required

**Solutions**: Use faster providers (Groq), enable caching, reduce library sampling depth.

### How much RAM do local providers need?
- **Minimum**: 8GB system RAM
- **Recommended**: 16GB for smooth operation
- **High-end models**: 32GB+ for 70B parameter models

## Recommendations & Quality

### Why aren't I getting good recommendations?
Common causes:
1. **Wrong discovery mode** - Try "Adjacent" instead of "Similar"
2. **Small library** - Needs 10+ artists for good analysis
3. **Inappropriate provider** - Try higher-quality provider
4. **Poor library analysis** - Increase sampling depth

### How many recommendations should I request?
**Beginners**: 5-10 recommendations
**Experienced**: 10-20 recommendations  
**Power users**: 20-50 recommendations
**Start small** and increase if you like the results.

### Can I customize the recommendation criteria?
Yes, through several settings:
- **Discovery Mode**: Similar/Adjacent/Exploratory
- **Recommendation Mode**: Albums vs Artists
- **Library Sampling**: How much of your library to analyze
- **Provider choice**: Different providers have different "personalities"

### Why do I keep getting the same recommendations?
1. **Caching enabled** - Recommendations cached for configured duration
2. **Small provider variety** - Try different discovery modes
3. **Limited library diversity** - Expand your collection first
4. **Insufficient randomization** - Some providers are more deterministic

**Solutions**: Clear cache, try different providers, adjust discovery mode.

## Technical Issues

### Brainarr doesn't appear in Import Lists
1. **Check file permissions** - Ensure Lidarr can read plugin files
2. **Verify file structure** - All plugin files in correct directory
3. **Restart Lidarr** - Full restart required after installation
4. **Check Lidarr logs** - Look for plugin loading errors

### Test connection fails
**Local providers**:
1. Ensure service is running (ollama serve, LM Studio server)
2. Check URL format (http://localhost:11434)
3. Verify firewall isn't blocking connection

**Cloud providers**:
1. Check API key format and validity
2. Verify network connectivity
3. Check provider service status

### No recommendations generated
1. **Library too small** - Need 10+ artists minimum
2. **Wrong configuration** - Verify all required settings
3. **Provider issues** - Check provider status and logs
4. **Rate limiting** - May need to wait between requests

### Getting error messages
1. **Check Lidarr logs** for detailed error information
2. **Enable debug logging** for more verbose output
3. **Verify configuration** matches provider requirements
4. **Test provider independently** before using with Brainarr

## Advanced Usage

### Can I run Brainarr in Docker?
Yes! Mount the plugin directory as a volume:
```yaml
volumes:
  - ./brainarr-plugin:/config/plugins/Brainarr
```
See [Docker Installation](Installation-Guide#docker-installation) for details.

### How do I backup my configuration?
Brainarr settings are stored in Lidarr's database:
1. **Backup Lidarr database** (lidarr.db)
2. **Export settings** via Lidarr's backup feature
3. **Document API keys** separately and securely

### Can I automate provider switching?
Not directly, but you can:
1. Create multiple import lists with different providers
2. Enable/disable them programmatically via Lidarr API
3. Use external scripts to manage configuration

### How do I contribute to Brainarr?
See [Contributing Guide](Contributing-Guide) for:
- Code contributions
- Bug reports
- Feature requests
- Documentation improvements
- Testing and feedback

## Troubleshooting

### Where are the log files?
- **Lidarr logs**: Lidarr data directory → `logs/`
- **Plugin logs**: Integrated with Lidarr logs
- **Enable debug logging** for more detailed information

### How do I report a bug?
1. **Search existing issues** on GitHub first
2. **Gather information**: Lidarr version, OS, provider, logs
3. **Create GitHub issue** with detailed description
4. **Include configuration** (without API keys)

### Performance is poor
1. **Check system resources** (RAM, CPU usage)
2. **Try smaller models** for local providers
3. **Use faster providers** (Groq for cloud)
4. **Reduce library sampling depth**
5. **Enable caching** to avoid repeated requests

## Getting Help

### Support Resources
1. **[Troubleshooting Guide](Troubleshooting-Guide)** - Systematic problem solving
2. **[Common Issues](Common-Issues)** - Known problems and solutions
3. **[Provider Troubleshooting](Provider-Troubleshooting)** - Provider-specific help
4. **GitHub Issues** - Report bugs and get community help

### Before Asking for Help
Please check:
- [ ] Followed appropriate setup guide completely
- [ ] Tried solutions in Common Issues and Troubleshooting Guide
- [ ] Checked Lidarr logs for error messages
- [ ] Verified provider is working independently
- [ ] Tested with default/simple configuration first

### How to Get the Best Help
When asking for help, include:
1. **Brainarr version** and Lidarr version
2. **Operating system** and installation method
3. **Provider type** and configuration (without API keys)
4. **Error messages** from logs
5. **Steps to reproduce** the issue
6. **What you expected** vs what happened

## Common Misconceptions

### "Brainarr will download everything automatically"
**Reality**: Brainarr only **recommends** music. Lidarr handles the actual downloading based on your search and quality settings.

### "Local providers are worse quality"
**Reality**: Local providers can be excellent quality, especially larger models. They're often more consistent than cloud providers.

### "I need expensive hardware for local providers"  
**Reality**: 8GB RAM can run smaller models effectively. Start small and upgrade if needed.

### "Cloud providers are always better"
**Reality**: Depends on use case. Local providers offer privacy, reliability, and no ongoing costs.

### "More recommendations = better results"
**Reality**: Quality over quantity. 5-10 good recommendations often better than 50 poor ones.

## Feature Requests & Roadmap

### Upcoming Features
- AWS Bedrock support
- Azure OpenAI integration
- Cost monitoring dashboard
- A/B testing framework
- Enhanced library analysis

### How to Request Features
1. **Search existing issues** to avoid duplicates
2. **Create GitHub issue** with "Feature Request" label  
3. **Describe use case** and expected behavior
4. **Consider contributing** if you have development skills

**Still have questions?** Check the [Troubleshooting Guide](Troubleshooting-Guide) or create a GitHub issue for help!