# Brainarr Recommendation Modes

## Overview

Brainarr supports two distinct recommendation modes that control how music is imported into your Lidarr library. This feature allows you to choose between importing specific albums or entire artist discographies based on your collection preferences.

## Recommendation Modes

### 1. Specific Albums Mode (Default)

**Setting Value**: `SpecificAlbums`

In this mode, Brainarr recommends individual albums from various artists. This is ideal for users who want to curate their library with specific releases rather than complete discographies.

**Characteristics**:
- Recommends specific album titles with artist names
- More granular control over what gets imported
- Lower storage requirements
- Perfect for discovering standout albums
- Reduces clutter from less popular releases

**Example Recommendations**:
```
- Pink Floyd - The Dark Side of the Moon
- Radiohead - OK Computer
- Miles Davis - Kind of Blue
- The Beatles - Abbey Road
```

**Best For**:
- Users with limited storage space
- Collectors focusing on acclaimed albums
- Those who prefer quality over quantity
- Discovery of essential albums from new artists

### 2. Artists Mode

**Setting Value**: `Artists`

In this mode, Brainarr recommends entire artists, and Lidarr will import their complete discography according to your monitoring settings.

**Characteristics**:
- Recommends artist names only
- Imports all albums from recommended artists
- Comprehensive collection building
- Discovers full artistic evolution
- Higher storage requirements

**Example Recommendations**:
```
- Pink Floyd (imports all 15 studio albums)
- Radiohead (imports all 9 studio albums + EPs)
- Miles Davis (imports extensive catalog)
- The Beatles (imports complete discography)
```

**Best For**:
- Completionist collectors
- Users with ample storage
- Deep exploration of artist catalogs
- Building comprehensive genre collections

## Configuration

### In Lidarr UI

1. Navigate to **Settings** → **Import Lists** → **Brainarr**
2. Find the **Recommendation Type** field
3. Select your preferred mode:
   - **Specific Albums** - For individual album recommendations
   - **Artists** - For complete artist discographies

### Configuration Impact

The recommendation mode affects several aspects of the import process:

#### Library Analysis
- **Specific Albums**: Analyzes your existing albums to avoid duplicates
- **Artists**: Analyzes your artist collection to recommend new artists

#### Prompt Generation
- **Specific Albums**: Requests "album - artist" format recommendations
- **Artists**: Requests artist names with brief descriptions

#### Duplicate Detection
- **Specific Albums**: Checks against existing albums in library
- **Artists**: Checks against existing artists to avoid redundancy

#### AI Provider Behavior
- **Specific Albums**: AI provides detailed album recommendations with context
- **Artists**: AI focuses on artist discovery and genre exploration

## Use Cases

### Scenario 1: Building a Starter Collection
**Recommended Mode**: Specific Albums

Start with acclaimed albums from various artists to build a diverse foundation without overwhelming your storage.

### Scenario 2: Deep Genre Exploration
**Recommended Mode**: Artists

When exploring jazz, classical, or progressive rock, import complete artist catalogs to understand artistic evolution.

### Scenario 3: Storage-Conscious Discovery
**Recommended Mode**: Specific Albums

With limited NAS space, focus on highly-rated albums rather than complete discographies.

### Scenario 4: Favorite Artist Expansion
**Recommended Mode**: Artists

When you love an artist's style, use Artists mode to discover similar artists and import their full catalogs.

## Technical Implementation

### How It Works

1. **User Selection**: Choose mode in Brainarr settings
2. **Prompt Adjustment**: System modifies AI prompts based on mode
3. **Response Parsing**: Different parsing logic for each mode
4. **Deduplication**: Mode-specific duplicate checking
5. **Import Creation**: Lidarr import items created accordingly

### API Behavior

```csharp
// Specific Albums Mode
recommendations = [
    { Artist: "Pink Floyd", Album: "Wish You Were Here", Year: 1975 },
    { Artist: "Led Zeppelin", Album: "IV", Year: 1971 }
]

// Artists Mode
recommendations = [
    { Artist: "Pink Floyd", Album: null },
    { Artist: "Led Zeppelin", Album: null }
]
```

### Prompt Differences

**Specific Albums Prompt**:
```
Please recommend 20 specific albums with high artistic merit.
Format: "Album Title - Artist Name"
Focus on standout releases that define genres or showcase exceptional creativity.
```

**Artists Prompt**:
```
Please recommend 20 music artists worth exploring in depth.
Format: "Artist Name"
Focus on artists with consistent quality across their discography.
```

## Performance Considerations

### Token Usage
- **Specific Albums**: ~20% more tokens due to album details
- **Artists**: More efficient token usage, simpler responses

### API Costs
- **Specific Albums**: Slightly higher due to detailed responses
- **Artists**: Lower API costs with simpler queries

### Import Volume
- **Specific Albums**: 1 album per recommendation
- **Artists**: 10-50+ albums per artist recommendation

### Network Traffic
- **Specific Albums**: Lower Lidarr API calls
- **Artists**: Higher metadata fetching for full discographies

## Monitoring and Logs

### Debug Logging

Enable debug logging to see recommendation mode in action:

```log
[2024-01-15 10:23:45] DEBUG: Recommendation mode: Artists (User setting: Artists)
[2024-01-15 10:23:45] DEBUG: Building artist-focused prompt for AI provider
[2024-01-15 10:23:46] DEBUG: Parsing response for artist recommendations
[2024-01-15 10:23:46] INFO: Received 20 artist recommendations
```

### Metrics

Track recommendation effectiveness by mode:
- Success rate per mode
- Duplicate rate comparison
- User satisfaction metrics
- Storage impact analysis

## Frequently Asked Questions

### Q: Can I switch modes without losing existing recommendations?
**A**: Yes, changing modes only affects future recommendation cycles. Existing imports remain unchanged.

### Q: Which mode is better for discovering new music?
**A**: Both work well, but Specific Albums mode offers more diverse discovery across many artists, while Artists mode provides deeper exploration of fewer artists.

### Q: How does this affect my Lidarr monitoring settings?
**A**: In Artists mode, your Lidarr artist monitoring settings determine which albums get downloaded. Configure these settings in Lidarr to control what gets imported.

### Q: Can I use different modes for different import lists?
**A**: Yes, create multiple Brainarr import lists with different modes for varied discovery strategies.

### Q: Does the mode affect AI provider selection?
**A**: No, all providers support both modes. However, some providers may excel at one mode over another based on their training data.

## Best Practices

1. **Start with Specific Albums** if you're new to Brainarr
2. **Use Artists mode** for genres you want to explore deeply
3. **Create multiple import lists** with different modes for variety
4. **Monitor storage usage** when using Artists mode
5. **Adjust discovery mode** (Similar/Adjacent/Exploratory) based on your chosen recommendation mode
6. **Review recommendations** before automatic import in Artists mode
7. **Configure Lidarr monitoring** appropriately for Artists mode

## Troubleshooting

### Issue: Too many albums imported in Artists mode
**Solution**: Adjust Lidarr's artist monitoring settings to limit which albums are imported (e.g., only studio albums).

### Issue: Duplicate artists being recommended
**Solution**: Ensure your existing artists are properly indexed in Lidarr. Check logs for deduplication details.

### Issue: Mode setting not taking effect
**Solution**: Save settings and trigger a manual import list sync. Check debug logs for the active mode.

### Issue: Poor recommendation quality in one mode
**Solution**: Try different AI providers or adjust discovery mode settings. Some providers excel at artist discovery while others better understand album significance.

## Future Enhancements

Planned improvements for recommendation modes:

- **Hybrid Mode**: Mix of artists and specific albums
- **Filtered Artists Mode**: Import only top-rated albums from recommended artists
- **Smart Mode**: Automatically choose based on library analysis
- **Collection Mode**: Recommend themed collections (e.g., "Essential Jazz Albums")

---

*Last Updated: 2025-08-25*
*Feature Version: 1.0.3*