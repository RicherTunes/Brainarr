# Migration Guide: Switching to Brainarr from Other Import Lists

## Overview
This guide helps you migrate from other Lidarr import lists to Brainarr's AI-powered recommendations while preserving your existing setup.

## Migration Paths

### From Spotify Import List

#### What You're Gaining
- No more Spotify API rate limits
- Recommendations based on YOUR actual library (not just followed playlists)
- Multiple discovery modes beyond "similar artists"
- Cost savings (no Spotify Premium required)

#### Migration Steps

1. **Export Current Configuration**
```bash
# Note your current Spotify settings
- Playlist IDs
- Followed artists
- Update interval
```

2. **Analyze Your Library First**
- Brainarr will analyze your existing collection
- No need to manually input artist preferences
- Automatically detects your genre preferences

3. **Configure Brainarr for Similar Results**
```yaml
Provider: OpenRouter (for variety) or DeepSeek (for cost)
Discovery Mode: Similar
Library Sample Size: 20
Max Recommendations: 50
Confidence Threshold: 0.7
```

4. **Disable Spotify Import** (after confirming Brainarr works)
- Settings → Import Lists → Spotify → Disable
- Keep it disabled (not deleted) for 1 week as backup

### From Last.fm Import List

#### What You're Gaining
- Recommendations beyond just play counts
- Discovery of artists you haven't scrobbled
- No dependency on Last.fm API availability
- Intelligent album selection (not just popular ones)

#### Migration Steps

1. **Map Your Last.fm Preferences**
```yaml
# Your top tags → Brainarr will auto-detect
# Your top artists → Already in your library
# Recommended artists → AI will find similar + more
```

2. **Equivalent Brainarr Configuration**
```yaml
Provider: Gemini (free tier) or Anthropic (quality)
Discovery Mode: Adjacent
Include Monitored Only: Yes
Library Analysis Depth: Deep
Max Recommendations: 30
```

3. **Preserve Discovery Patterns**
- Set update interval same as Last.fm (e.g., 24 hours)
- Enable caching to reduce API calls
- Use confidence threshold 0.6 for broader discovery

### From Headphones Import List

#### What You're Gaining
- Modern, maintained solution (Headphones is deprecated)
- Better handling of artist name variations
- Intelligent album type selection
- No need for separate Headphones installation

#### Migration Steps

1. **Export Headphones Artists**
```python
# From Headphones database
SELECT ArtistName FROM artists WHERE Status = 'Active'
```

2. **Import Process**
```yaml
# First run - Conservative settings
Provider: Ollama (local, safe)
Discovery Mode: Similar
Max Recommendations: 10
Confidence Threshold: 0.8

# After validation - Expand
Max Recommendations: 50
Confidence Threshold: 0.6
```

### From MusicBrainz Collections

#### What You're Gaining
- Automatic discovery without manual collection management
- AI understands context beyond just tags
- Dynamic recommendations that evolve with your library

#### Migration Steps

1. **Document Your Collections**
- Series collections → Set Discovery Mode: Similar
- Genre collections → Auto-detected by Brainarr
- Label collections → Use prompt customization

2. **Replicate Collection Logic**
```yaml
# For series (e.g., "Now That's What I Call Music")
Custom Prompt Addition: "Include compilation albums and various artist collections"

# For label focus (e.g., "Sub Pop Records")
Custom Prompt Addition: "Focus on independent and alternative labels"
```

### From Manual Lists (CSV/Text)

#### What You're Gaining
- No more manual list maintenance
- Automatic discovery of related artists
- Intelligent pruning of already-owned albums

#### Migration Steps

1. **Import Existing Lists** (Optional)
```bash
# Keep your manual list as excluded artists
Settings → Import Lists → Exclusions
```

2. **Let AI Learn Your Preferences**
- Brainarr analyzes your existing library
- Understands patterns you might not have noticed
- Provides reasons for each recommendation

## Configuration Comparison Table

| Previous Import List | Brainarr Equivalent | Provider | Mode | Settings |
|---------------------|-------------------|----------|------|----------|
| Spotify Discover Weekly | AI Discovery | OpenRouter | Exploratory | Weekly update, 30 items |
| Last.fm Recommended | AI Similar | DeepSeek | Similar | 0.7 confidence |
| Last.fm User Library | Library Analysis | Any | Adjacent | Include monitored |
| MusicBrainz Series | Targeted Prompt | Gemini | Similar | Custom prompt |
| Manual Artist List | Base Library | Ollama | Similar | High confidence |

## Validation Process

### Week 1: Parallel Running
1. Keep old import list active but paused
2. Run Brainarr with conservative settings
3. Compare recommendations
4. Note any missing favorites

### Week 2: Tuning
1. Adjust confidence thresholds
2. Try different providers
3. Experiment with discovery modes
4. Fine-tune prompt additions

### Week 3: Cutover
1. Disable (don't delete) old import lists
2. Increase Brainarr recommendations
3. Monitor for 1 week
4. Delete old lists after confirmation

## Preserving Special Cases

### Genre-Specific Lists
```yaml
# Add to Custom Prompt
"Focus heavily on [death metal/jazz fusion/synthwave]"
```

### Regional Preferences
```yaml
# Add to Custom Prompt
"Include artists from [Japan/Nordic countries/Latin America]"
```

### Era Preferences
```yaml
# Add to Custom Prompt
"Emphasize music from [1970s/1990s/2020s]"
```

### Label Preferences
```yaml
# Add to Custom Prompt  
"Prioritize releases from independent labels like [Warp/Ninja Tune/Stones Throw]"
```

## Common Migration Issues

### Issue: Missing Niche Artists
**Solution**: Lower confidence threshold to 0.5, use Exploratory mode

### Issue: Too Many Mainstream Suggestions
**Solution**: Use prompt addition: "Avoid mainstream pop, focus on underground"

### Issue: Wrong Genre Balance
**Solution**: Manually adjust prompt: "70% electronic, 30% rock"

### Issue: Getting Albums I Already Own
**Solution**: Enable "Skip Library Duplicates" in advanced settings

## Rollback Plan

If you need to revert:

1. **Re-enable old import list**
   - Settings → Import Lists → [Your old list] → Enable

2. **Export Brainarr recommendations**
   - Useful for manual review
   - Can be imported as manual list if needed

3. **Gradual transition**
   - Run both in parallel
   - Slowly reduce old list frequency
   - Increase Brainarr recommendations

## Performance Comparison

| Metric | Traditional Lists | Brainarr |
|--------|------------------|----------|
| API Calls | Continuous | On-demand + cached |
| Recommendations Quality | Static algorithms | AI-powered analysis |
| Library Awareness | None | Full analysis |
| Customization | Limited | Unlimited via prompts |
| Cost | Often requires premium | Free local options |
| Maintenance | Regular updates needed | Self-improving |

## Advanced Migration Tips

### 1. Preserve Discovery Velocity
```yaml
# Match your previous discovery rate
Update Interval: [Same as before]
Max Recommendations: [Same as before]
Cache Duration: 12 hours
```

### 2. Maintain Genre Balance
```yaml
# Custom prompt for genre distribution
"Maintain genre balance: 40% [primary genre], 30% [secondary], 30% discovery"
```

### 3. Respect Collection Boundaries
```yaml
# For curated collections
Discovery Mode: Similar  # Not exploratory
Confidence Threshold: 0.75  # Higher threshold
Album Types: Studio Albums  # Specific types
```

### 4. Migration Metrics

Track success with:
- Artist addition rate (should match or exceed previous)
- Genre distribution (should align with preferences)
- User satisfaction (are you discovering music you enjoy?)
- Error rate (should be lower than traditional APIs)

## FAQ

**Q: Will I lose my existing import history?**
A: No, your library and history remain untouched.

**Q: Can I run multiple import lists simultaneously?**
A: Yes, Brainarr works alongside other import lists.

**Q: How long before I see quality recommendations?**
A: Immediately, but they improve as the AI learns your library.

**Q: What if Brainarr recommends artists I hate?**
A: Add them to Lidarr's exclusion list, adjust prompts.

**Q: Can I migrate my exclusion lists?**
A: Yes, Lidarr exclusions apply to all import lists including Brainarr.

## Support

For migration help:
- Check existing recommendations quality
- Review logs for any errors
- Adjust settings based on results
- Report issues on GitHub

Remember: Migration is reversible - your old import lists can be re-enabled anytime!