# Enhanced Library Analysis Documentation

## Overview

Brainarr v1.0+ includes comprehensive library analysis capabilities that extract rich metadata from Lidarr to provide AI models with deep context about your music collection. This results in dramatically improved recommendation quality.

## Key Features

### 1. Real Genre Extraction
- **Primary Source**: Extracts genres directly from `ArtistMetadata.Genres` and `Album.Genres`
- **Intelligent Fallback**: If no genre metadata exists, analyzes artist/album overviews for common genre keywords
- **Genre Distribution**: Calculates percentage distribution of genres in your collection
- **Smart Grouping**: Groups similar genres and identifies your collection's genre focus

### 2. Temporal Pattern Analysis
- **Release Decades**: Identifies which decades dominate your collection (e.g., "1970s", "1980s", "2010s")
- **Era Preferences**: Categorizes preferences as Classic (<1970), Golden Age (1970-1989), Modern (1990-2009), or Contemporary (2010+)
- **New Release Ratio**: Calculates percentage of albums from the last 2 years to gauge interest in current releases
- **Temporal Focus**: Determines if collection focuses on classic, current, or mixed eras

### 3. Collection Quality Metrics
- **Monitoring Ratio**: Percentage of artists/albums actively monitored
- **Collection Completeness**: Ratio of monitored to total items (indicates if you're a completionist)
- **Average Albums per Artist**: Measures collection depth (do you collect full discographies or singles?)
- **Quality Assessment**: Rates collection as Building, Moderate, High, or Very High quality

### 4. User Preference Signals
- **Album Type Distribution**: Tracks preference for Albums vs EPs vs Singles vs Live recordings
- **Secondary Types**: Identifies if you collect remixes, compilations, demos, or soundtracks
- **Discovery Trend**: Analyzes recent additions to determine if collection is:
  - Stable (few recent additions)
  - Steady growth (5-15% recent)
  - Actively growing (15-30% recent)
  - Rapidly expanding (>30% recent)
- **Collection Size**: Categorizes as starter (<50 artists), growing (50-199), established (200-499), extensive (500-999), or massive (1000+)

### 5. Collection Character Analysis
- **Genre Focus**: Determines if collection is specialized (>50% one genre), focused (>30% one genre), or eclectic
- **Collection Type**: Combines genre and temporal analysis (e.g., "specialized-classic" for a focused classic rock collection)
- **Smart Profiling**: Creates a comprehensive "Collection DNA" profile

## Enhanced Prompt Generation

The enhanced library analyzer generates rich prompts with multiple context sections:

### Collection Overview
```text
📊 COLLECTION OVERVIEW:
• Size: established (245 artists, 1,234 albums)
• Genres: Rock (35.2%), Electronic (22.1%), Jazz (15.8%), Metal (12.3%), Pop (8.6%)
• Collection type: eclectic-mixed
• Discovery focus: artists in related but unexplored genres
```

### Musical DNA
```text
🎵 MUSICAL DNA:
• Era focus: 2010s, 2000s, 1990s
• Era preference: Modern, Contemporary
• Album types: Album (980), EP (150), Single (104)
• New release interest: Moderate (18% recent)
```

### Collection Patterns
```text
📈 COLLECTION PATTERNS:
• Discovery trend: actively growing
• Collection quality: High (76% complete)
• Active tracking: 82% of collection
• Collection depth: 5.0 albums per artist
```

## Data Sources

### Artist Data
- `Artist.Name` - Artist names
- `Artist.Added` - When added to library
- `Artist.Monitored` - Monitoring status
- `ArtistMetadata.Genres` - Genre information
- `ArtistMetadata.Overview` - Artist descriptions

### Album Data
- `Album.Title` - Album titles
- `Album.ReleaseDate` - Release dates for temporal analysis
- `Album.AlbumType` - Primary album type
- `Album.SecondaryTypes` - Additional categorization
- `Album.Monitored` - Monitoring status
- `Album.Genres` - Album-specific genres
- `Album.Added` - When added to library

## Token-Aware Sampling

The system includes intelligent token management for different AI providers:

### Sampling Strategies
1. **Minimal** (2000 tokens) - For local models (Ollama, LM Studio)
2. **Balanced** (3000 tokens) - Default for most providers
3. **Comprehensive** (4000 tokens) - For premium providers (GPT-4, Claude)

### Smart Sampling Algorithm
- **Small Libraries** (<50 artists): Includes most data
- **Medium Libraries** (50-200 artists): Strategic sampling
  - 40% top artists by album count
  - 30% recently added
  - 30% random sampling
- **Large Libraries** (200+ artists): Token-constrained sampling
  - Prioritizes most prolific artists
  - Includes recent additions
  - Respects token budget limits

## Configuration

### Discovery Modes
- **Similar**: Recommendations very close to existing taste
- **Adjacent**: Explore related but new genres
- **Exploratory**: Discover completely new genres

### Quality Settings
```csharp
settings.EnableStrictValidation = true;  // More aggressive filtering
settings.EnableDebugLogging = true;      // Detailed analysis logs
settings.SamplingStrategy = SamplingStrategy.Comprehensive; // Maximum context
```

## Performance Considerations

### Caching
- Library profiles are cached for 6 hours by default
- Reduces repeated analysis overhead
- Cache duration configurable via `CacheDuration` setting

### Optimization
- Lazy loading of metadata only when needed
- Efficient LINQ queries for data aggregation
- Parallel processing where applicable

## API Integration

The enhanced library analyzer integrates seamlessly with all 8 AI providers:

### Local Providers
- **Ollama**: Uses minimal sampling to avoid context overflow
- **LM Studio**: Optimized prompts for local model constraints

### Cloud Providers
- **OpenAI/Anthropic**: Leverages comprehensive sampling
- **Budget Providers**: Balanced approach for cost optimization

## Troubleshooting

### No Genres Detected
- Check if Lidarr has fetched metadata for your artists/albums
- Ensure MusicBrainz integration is working
- Fallback genres will be used if no data available

### Incorrect Era Detection
- Verify album release dates are populated
- Check if albums have correct metadata
- Manual tagging may be needed for older releases

### Performance Issues
- Enable caching if disabled
- Reduce sampling strategy to Minimal or Balanced
- Check Lidarr database performance

## Future Enhancements

Planned improvements for future versions:
- Integration with Last.fm for play count data
- Spotify API integration for additional metadata
- Machine learning-based taste profiling
- Collaborative filtering with anonymous user data
- Real-time library change detection
- Advanced genre taxonomy mapping

## Technical Details

### Metadata Storage
All enhanced metadata is stored in the `LibraryProfile.Metadata` dictionary:
```csharp
profile.Metadata["GenreDistribution"] // Dictionary<string, double>
profile.Metadata["ReleaseDecades"]    // List<string>
profile.Metadata["PreferredEras"]     // List<string>
profile.Metadata["MonitoredRatio"]    // double
// ... and more
```

### Extensibility
The system is designed for easy extension:
1. Add new analysis methods to `LibraryAnalyzer`
2. Store results in `Metadata` dictionary
3. Update prompt builders to use new data
4. No breaking changes to existing code

## Examples

### Specialized Collection
A user with 90% metal albums will receive:
- Metal subgenre recommendations
- Similar intensity/style artists
- Era-appropriate suggestions

### Eclectic Collection
A user with diverse genres receives:
- Balanced recommendations across genres
- Bridge artists connecting genres
- Discovery-focused suggestions

### New Collector
A user with <50 artists receives:
- Foundation-building recommendations
- Popular/essential albums
- Gateway artists to new genres

## Conclusion

The enhanced library analysis system transforms Brainarr from a simple recommendation tool to an intelligent music discovery assistant that truly understands your musical taste and collection patterns. By leveraging the rich metadata available in Lidarr, recommendations are now contextual, personalized, and highly relevant.
