# Brainarr Performance Tuning Guide

## Overview

This guide provides detailed strategies for optimizing Brainarr's performance, reducing costs, and improving response times across different deployment scenarios.

## Performance Metrics

### Key Performance Indicators

| Metric | Target | Measurement |
|--------|--------|-------------|
| Response Time | < 5 seconds | Time from request to first recommendation |
| Token Efficiency | < 2000 tokens/request | Average tokens per recommendation batch |
| Cache Hit Rate | > 60% | Percentage of cached responses |
| Provider Availability | > 99% | Uptime for primary provider |
| Memory Usage | < 200MB | Plugin memory footprint |
| CPU Usage | < 10% idle | CPU utilization during requests |

## Provider Performance Comparison

### Response Time Benchmarks

| Provider | Average Response | 95th Percentile | Cost/Request |
|----------|-----------------|-----------------|--------------|
| Ollama (local) | 2-5s | 8s | $0.00 |
| LM Studio (local) | 3-6s | 10s | $0.00 |
| Groq | 0.5-1s | 2s | $0.001 |
| Gemini Flash | 1-2s | 3s | $0.00 (free tier) |
| DeepSeek | 2-3s | 5s | $0.0002 |
| OpenAI GPT-3.5 | 1-3s | 5s | $0.002 |
| OpenAI GPT-4 | 3-5s | 8s | $0.03 |
| Anthropic Claude | 2-4s | 6s | $0.015 |
| Perplexity | 2-4s | 7s | $0.005 |

## Local Provider Optimization

### Ollama Performance

#### Model Selection

```bash
# Fastest models (< 2s response)
ollama pull phi3:3.8b
ollama pull llama3.2:1b
ollama pull gemma2:2b

# Balanced performance (2-5s)
ollama pull llama3:8b
ollama pull mistral:7b
ollama pull gemma2:9b

# Highest quality (5-10s)
ollama pull llama3:70b
ollama pull mixtral:8x7b
```

#### GPU Acceleration

```bash
# Check GPU availability
nvidia-smi

# Run Ollama with GPU
OLLAMA_GPU_LAYERS=35 ollama serve

# Set GPU memory limit
OLLAMA_GPU_MEMORY=8192 ollama serve
```

#### CPU Optimization

```bash
# Increase thread count
OLLAMA_NUM_THREADS=8 ollama serve

# Use AVX2 instructions
OLLAMA_USE_AVX2=1 ollama serve

# Limit concurrent requests
OLLAMA_MAX_LOADED_MODELS=1 ollama serve
```

#### Configuration Tuning

```yaml
# Brainarr Settings
Model: phi3  # Smaller, faster model
Request Timeout: 30  # Longer timeout for local
Max Tokens: 1000  # Reduce for speed
Temperature: 0.5  # Lower for consistency
```

### LM Studio Performance

#### Model Configuration

```json
{
  "n_gpu_layers": 35,
  "n_threads": 8,
  "n_batch": 512,
  "context_length": 2048,
  "temperature": 0.7,
  "top_p": 0.9,
  "repeat_penalty": 1.1
}
```

#### Hardware Optimization

- **RAM**: Minimum 8GB, recommended 16GB+
- **GPU**: NVIDIA with 6GB+ VRAM
- **CPU**: Modern multi-core (8+ threads)
- **Storage**: SSD for model loading

## Cloud Provider Optimization

### Token Optimization

#### Prompt Compression

```csharp
// Before - Verbose prompt (500 tokens)
var prompt = @"
I have a music library with the following artists and albums.
Please analyze my taste and recommend similar music.
My library contains: Pink Floyd, Led Zeppelin, The Beatles...
I prefer classic rock from the 1960s and 1970s...
";

// After - Compressed prompt (150 tokens)
var prompt = @"
Library: Pink Floyd, Led Zeppelin, Beatles
Genre: Classic Rock 60s-70s
Task: Recommend 10 similar albums (JSON)
";
```

#### Response Format Optimization

```json
// Verbose format (100 tokens per recommendation)
{
  "recommendation": {
    "artist_name": "King Crimson",
    "album_title": "In the Court of the Crimson King",
    "release_year": 1969,
    "genre_classification": "Progressive Rock",
    "similarity_score": 0.95,
    "explanation": "Similar to Pink Floyd..."
  }
}

// Compact format (20 tokens per recommendation)
{
  "a": "King Crimson",
  "b": "In the Court",
  "c": 0.95
}
```

### Model Selection Strategy

```yaml
# Cost-optimized chain
Primary: gemini-2.5-flash  # Free tier
Fallback 1: deepseek-chat  # Very cheap
Fallback 2: gpt-3.5-turbo  # Reliable

# Quality-optimized chain
Primary: gpt-4o  # Best quality
Fallback 1: claude-3-5-sonnet  # Excellent reasoning
Fallback 2: gemini-2.5-pro  # Good balance

# Speed-optimized chain
Primary: groq-llama3-70b  # Ultra-fast
Fallback 1: gemini-2.5-flash  # Fast
Fallback 2: gpt-3.5-turbo  # Consistent
```

## Caching Strategies

### In-Memory Cache Configuration

```csharp
// Optimal cache settings
public class CacheConfiguration
{
    // Cache duration based on usage patterns
    public int CacheDurationMinutes => DiscoveryMode switch
    {
        DiscoveryMode.Similar => 1440,      // 24 hours - stable
        DiscoveryMode.Adjacent => 720,      // 12 hours - semi-stable
        DiscoveryMode.Exploratory => 360,   // 6 hours - dynamic
        _ => 60                             // 1 hour default
    };

    // Cache size limits
    public int MaxCacheEntries = 100;
    public long MaxCacheSizeBytes = 50 * 1024 * 1024; // 50MB
}
```

### Cache Key Optimization

```csharp
// Generate efficient cache keys
public string GenerateCacheKey(LibraryProfile profile, DiscoveryMode mode)
{
    // Use hash for long library lists
    var libraryHash = ComputeHash(profile.TopArtists.Take(20));

    return $"{mode}:{libraryHash}:{profile.GenreDistribution.Count}";
}

private string ComputeHash(IEnumerable<string> items)
{
    using (var sha256 = SHA256.Create())
    {
        var input = string.Join(",", items.OrderBy(x => x));
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes).Substring(0, 8);
    }
}
```

### Cache Warming

```csharp
// Pre-populate cache during off-peak hours
public async Task WarmCache()
{
    var commonProfiles = new[]
    {
        new { Genres = new[] { "Rock", "Metal" }, Mode = DiscoveryMode.Similar },
        new { Genres = new[] { "Jazz", "Blues" }, Mode = DiscoveryMode.Adjacent },
        new { Genres = new[] { "Electronic" }, Mode = DiscoveryMode.Exploratory }
    };

    foreach (var profile in commonProfiles)
    {
        await GetRecommendationsAsync(BuildPrompt(profile));
    }
}
```

## Rate Limiting Optimization

### Adaptive Rate Limiting

```csharp
public class AdaptiveRateLimiter
{
    private readonly Dictionary<string, RateLimit> _limits = new()
    {
        ["openai"] = new RateLimit { RequestsPerMinute = 60, TokensPerMinute = 90000 },
        ["anthropic"] = new RateLimit { RequestsPerMinute = 50, TokensPerMinute = 100000 },
        ["gemini"] = new RateLimit { RequestsPerMinute = 60, TokensPerMinute = 1000000 },
        ["groq"] = new RateLimit { RequestsPerMinute = 30, TokensPerMinute = 20000 }
    };

    public async Task<T> ExecuteWithBackoff<T>(string provider, Func<Task<T>> action)
    {
        var limit = _limits[provider];
        var delay = CalculateDelay(provider);

        if (delay > 0)
        {
            await Task.Delay(delay);
        }

        return await action();
    }

    private int CalculateDelay(string provider)
    {
        // Exponential backoff based on recent rate limit hits
        var recentHits = GetRecentRateLimitHits(provider);
        return recentHits switch
        {
            0 => 0,
            1 => 1000,
            2 => 2000,
            3 => 4000,
            _ => 8000
        };
    }
}
```

## Database Optimization

### Query Optimization

```sql
-- Efficient artist retrieval
CREATE INDEX idx_artists_monitored ON Artists(Monitored) WHERE Monitored = 1;
CREATE INDEX idx_albums_artistid ON Albums(ArtistId);
CREATE INDEX idx_tracks_albumid ON Tracks(AlbumId);

-- Optimized query for library analysis
SELECT
    a.Name as ArtistName,
    COUNT(DISTINCT al.Id) as AlbumCount,
    GROUP_CONCAT(DISTINCT al.Genres) as Genres
FROM Artists a
JOIN Albums al ON a.Id = al.ArtistId
WHERE a.Monitored = 1
GROUP BY a.Id
LIMIT 100;
```

### Connection Pooling

```csharp
public class DatabaseConfiguration
{
    public string ConnectionString =>
        "Data Source=lidarr.db;" +
        "Version=3;" +
        "Pooling=True;" +
        "Max Pool Size=10;" +
        "Connection Timeout=5";
}
```

## Memory Optimization

### Recommendation Buffer Management

```csharp
public class RecommendationBuffer
{
    private readonly int _maxBufferSize = 1000;
    private readonly TimeSpan _bufferExpiry = TimeSpan.FromHours(1);
    private Queue<Recommendation> _buffer = new();

    public void Add(IEnumerable<Recommendation> recommendations)
    {
        foreach (var rec in recommendations.Take(_maxBufferSize - _buffer.Count))
        {
            _buffer.Enqueue(rec);
        }

        // Trim old entries
        while (_buffer.Count > _maxBufferSize)
        {
            _buffer.Dequeue();
        }
    }
}
```

### String Interning

```csharp
public class StringOptimization
{
    private readonly HashSet<string> _internedStrings = new();

    public string InternString(string value)
    {
        if (_internedStrings.TryGetValue(value, out var interned))
        {
            return interned;
        }

        _internedStrings.Add(value);
        return value;
    }
}
```

## Network Optimization

### Connection Reuse

```csharp
public class HttpClientConfiguration
{
    public static HttpClient CreateOptimizedClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 10,
            EnableMultipleHttp2Connections = true
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
}
```

### Request Compression

```csharp
public class CompressionMiddleware
{
    public HttpRequestMessage CompressRequest(HttpRequestMessage request)
    {
        if (request.Content != null)
        {
            var compressed = CompressContent(request.Content);
            request.Content = compressed;
            request.Content.Headers.ContentEncoding.Add("gzip");
        }

        request.Headers.AcceptEncoding.Add(
            new StringWithQualityHeaderValue("gzip"));

        return request;
    }
}
```

## Monitoring and Profiling

### Performance Metrics Collection

```csharp
public class PerformanceMonitor
{
    private readonly Dictionary<string, PerformanceMetric> _metrics = new();

    public async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var memoryBefore = GC.GetTotalMemory(false);

        try
        {
            var result = await action();

            stopwatch.Stop();
            var memoryAfter = GC.GetTotalMemory(false);

            RecordMetric(operation, new PerformanceMetric
            {
                Duration = stopwatch.ElapsedMilliseconds,
                MemoryDelta = memoryAfter - memoryBefore,
                Success = true
            });

            return result;
        }
        catch (Exception ex)
        {
            RecordMetric(operation, new PerformanceMetric
            {
                Duration = stopwatch.ElapsedMilliseconds,
                Success = false,
                Error = ex.Message
            });
            throw;
        }
    }
}
```

### Logging Optimization

```xml
<!-- NLog configuration for performance -->
<nlog>
  <targets>
    <target name="file"
            xsi:type="AsyncWrapper"
            queueLimit="5000"
            overflowAction="Discard">
      <target xsi:type="File"
              fileName="lidarr.log"
              archiveEvery="Day"
              maxArchiveFiles="7"
              bufferSize="32768" />
    </target>
  </targets>

  <rules>
    <!-- Only log warnings and above in production -->
    <logger name="Brainarr.*" minlevel="Warn" writeTo="file" />
  </rules>
</nlog>
```

## Production Optimization Checklist

### Before Deployment

- [ ] Select appropriate AI models for performance requirements
- [ ] Configure caching with proper duration
- [ ] Set rate limits for all providers
- [ ] Enable connection pooling
- [ ] Configure appropriate timeouts
- [ ] Disable debug logging
- [ ] Enable response compression

### Runtime Optimization

- [ ] Monitor cache hit rates
- [ ] Track provider response times
- [ ] Review token usage patterns
- [ ] Analyze memory consumption
- [ ] Check for memory leaks
- [ ] Monitor network latency
- [ ] Review error rates

### Cost Optimization

- [ ] Use free tier providers where possible
- [ ] Enable aggressive caching
- [ ] Optimize prompt length
- [ ] Use smaller models for simple tasks
- [ ] Batch recommendations
- [ ] Schedule updates during off-peak
- [ ] Monitor API usage closely

## Troubleshooting Performance Issues

### High Response Times

```bash
# Check provider latency
curl -w "@curl-format.txt" -o /dev/null -s "http://localhost:11434/api/generate"

# Monitor Lidarr process
htop -p $(pidof Lidarr)

# Check disk I/O
iotop -p $(pidof Lidarr)
```

### High Memory Usage

```bash
# Analyze memory usage
pmap -x $(pidof Lidarr)

# Check for memory leaks
valgrind --leak-check=full --show-leak-kinds=all Lidarr

# Force garbage collection (in code)
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
```

### Poor Cache Performance

```csharp
// Add cache statistics logging
_logger.Info($"Cache Stats: Hits={hits}, Misses={misses}, " +
             $"Hit Rate={hitRate:P}, Size={cacheSize}MB");
```

## Best Practices Summary

1. **Start with local providers** for development and testing
2. **Use caching aggressively** to reduce API calls
3. **Choose models based on task complexity** - don't use GPT-4 for simple tasks
4. **Monitor and log performance metrics** continuously
5. **Implement circuit breakers** for provider failures
6. **Use connection pooling** for HTTP clients
7. **Optimize prompts** for token efficiency
8. **Schedule heavy operations** during off-peak hours
9. **Implement graceful degradation** when providers are slow
10. **Regular performance audits** to identify bottlenecks

## Additional Resources

- [Ollama Performance Guide](https://github.com/ollama/ollama/blob/main/docs/performance.md)
- [OpenAI Rate Limits](https://platform.openai.com/docs/guides/rate-limits)
- [.NET Performance Best Practices](https://docs.microsoft.com/en-us/dotnet/framework/performance/)
- [Lidarr Performance Tuning](https://wiki.servarr.com/lidarr/troubleshooting#performance-issues)
