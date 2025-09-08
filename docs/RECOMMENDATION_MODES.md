# Recommendation Modes Documentation

## Overview

Brainarr v1.0+ introduces **Recommendation Modes** that control whether the AI recommends specific albums or entire artist discographies. This powerful feature allows you to tailor recommendations to your collection building strategy.

## Available Modes

### Specific Albums Mode

**Value**: `SpecificAlbums` (0)
**Default**: Yes
**Use Case**: Targeted library additions

When using Specific Albums mode, the AI recommends individual albums that match your preferences. This mode is ideal for:

- Curated collection building
- Discovering standout albums without committing to full discographies
- Users with limited storage space
- Exploring new genres with specific acclaimed albums

**Example Output**:

- Pink Floyd - "The Dark Side of the Moon"
- Radiohead - "OK Computer"
- Miles Davis - "Kind of Blue"

### Artists Mode

**Value**: `Artists` (1)
**Use Case**: Comprehensive library building

When using Artists mode, the AI recommends entire artists. Lidarr will then import ALL albums from these artists. This mode is ideal for:

- Building comprehensive collections quickly
- Discovering artists you'll love completely
- Users who prefer complete discographies
- Establishing a core library of favorite artists

**Example Output**:

- Pink Floyd (imports all 15 studio albums)
- Radiohead (imports all 10 studio albums)
- Miles Davis (imports extensive catalog)

## Configuration

### Via Lidarr UI

1. Navigate to **Settings > Import Lists > Brainarr**
2. Find the **Recommendation Type** field
3. Select your preferred mode:
   - **Specific Albums** - For targeted additions
   - **Artists** - For comprehensive collection building
4. Save settings

### Configuration Impact

The recommendation mode significantly affects:

- **Import Volume**: Artists mode imports much more content
- **Storage Usage**: Full discographies require more space
- **Discovery Breadth**: Albums mode explores more variety
- **Collection Growth**: Artists mode builds collections faster

## How It Works

### Specific Albums Mode (Details)

When configured for specific albums, Brainarr:

1. Analyzes your library preferences
2. Identifies individual albums that match your taste
3. Returns album-specific recommendations with artist and album names
4. Lidarr imports only the recommended albums

**AI Prompt Structure**:

```text
Based on this music library, recommend 15 specific albums that would complement the collection.
Format: "Artist - Album Title"
```

### Artists Mode (Details)

When configured for artists, Brainarr:

1. Analyzes your library preferences
2. Identifies artists whose entire catalog matches your taste
3. Returns artist recommendations
4. Lidarr imports the artist and monitors all their albums

**AI Prompt Structure**:

```text
Based on this music library, recommend 10 artists whose complete discographies would enhance the collection.
Consider artists where most of their work aligns with these preferences.
```

## Best Practices

### When to Use Specific Albums Mode

- **Exploring New Genres**: Test waters with acclaimed albums before committing
- **Limited Storage**: Add only the best albums from each artist
- **Curated Collections**: Build a "best of the best" library
- **Mixed Preferences**: When you like some but not all of an artist's work

### When to Use Artists Mode

- **New Library Setup**: Quickly build a comprehensive collection
- **Favorite Artists**: When you know you'll enjoy their entire catalog
- **Complete Collections**: Building authoritative genre collections
- **Ample Storage**: When space isn't a constraint

## Integration with Other Settings

### Discovery Mode Synergy

Recommendation modes work with discovery modes:

| Discovery Mode | Albums Mode Effect | Artists Mode Effect |
|---------------|-------------------|---------------------|
| Similar | Specific albums very close to your taste | Artists similar to your favorites |
| Adjacent | Albums from related genres | Artists bridging your current genres |
| Exploratory | Standout albums from new territories | Artists to expand your horizons |

### Sampling Strategy Interaction

The sampling strategy affects both modes:

- **Minimal Sampling**: Quick recommendations, may miss nuances
- **Balanced Sampling**: Optimal context for both modes
- **Comprehensive Sampling**: Best quality recommendations

### Provider Considerations

Different providers excel at different modes:

| Provider | Albums Mode | Artists Mode | Notes |
|----------|------------|--------------|-------|
| GPT-4o | Excellent | Excellent | Great at both modes |
| Claude 3.5 | Excellent | Excellent | Nuanced recommendations |
| Gemini 1.5 | Very Good | Good | Better at specific albums |
| Local Models | Good | Fair | May struggle with artist context |

## Examples

### Scenario 1: New User Building Library

**Settings**:

- Mode: Artists
- Discovery: Similar
- Sampling: Balanced

**Result**: Recommends 10-15 artists similar to your seed artists, importing complete discographies for rapid library growth.

### Scenario 2: Exploring Jazz for Rock Fan

**Settings**:

- Mode: Specific Albums
- Discovery: Exploratory
- Sampling: Comprehensive

**Result**: Recommends specific acclaimed jazz albums that rock fans typically enjoy, without overwhelming with full jazz discographies.

### Scenario 3: Completionist Collector

**Settings**:

- Mode: Artists
- Discovery: Adjacent
- Sampling: Comprehensive

**Result**: Recommends artists that bridge your current genres, importing complete catalogs for comprehensive coverage.

## Troubleshooting

### Too Many Imports with Artists Mode

**Problem**: Artists mode importing too much content
**Solution**:

1. Switch to Specific Albums mode temporarily
2. Reduce Max Recommendations setting
3. Use Similar discovery mode for more targeted results

### Recommendations Not Specific Enough

**Problem**: Getting generic recommendations
**Solution**:

1. Increase Sampling Strategy to Comprehensive
2. Ensure library has enough content for analysis (50+ albums)
3. Try a more capable AI provider (GPT-4o or Claude 3.5)

### Mode Changes Not Taking Effect

**Problem**: Recommendations seem unchanged after switching modes
**Solution**:

1. Clear recommendation cache (restart Lidarr)
2. Ensure settings are saved
3. Check Lidarr logs for mode confirmation
4. Wait for next scheduled import list sync

## Technical Implementation

The RecommendationMode enum is defined in `BrainarrSettings.cs`:

```csharp
public enum RecommendationMode
{
    SpecificAlbums = 0,  // Recommend specific albums to import
    Artists = 1          // Recommend artists (Lidarr imports all their albums)
}
```

The mode is used in prompt generation to instruct the AI appropriately, ensuring recommendations match the expected format for Lidarr's import system.

## Future Enhancements

Potential future improvements to recommendation modes:

- **Mixed Mode**: Combine both in a single recommendation set
- **Filtered Artists**: Import artists but only specific album types
- **Smart Mode**: Automatically choose based on library size
- **Album Limits**: Import artists but cap albums per artist

## Related Documentation

- [Discovery Modes](../README.md#discovery-modes) - Control recommendation style
- [User Setup Guide](USER_SETUP_GUIDE.md) - Complete configuration guide
- [Provider Guide](PROVIDER_GUIDE.md) - Provider-specific capabilities
