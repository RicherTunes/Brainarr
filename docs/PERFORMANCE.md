# Brainarr Performance Tuning Guide

## Overview

This guide provides optimization strategies to improve Brainarr's performance, reduce resource usage, and minimize API costs.

## Table of Contents
- [Quick Wins](#quick-wins)
- [Cache Optimization](#cache-optimization)
- [Rate Limiting Configuration](#rate-limiting-configuration)
- [Memory Optimization](#memory-optimization)
- [Database Optimization](#database-optimization)
- [Provider-Specific Optimizations](#provider-specific-optimizations)
- [System Tuning](#system-tuning)
- [Monitoring Performance](#monitoring-performance)

## Quick Wins

### Immediate Performance Improvements

1. **Enable Caching** (60-80% API reduction)
   ```json
   {
     "CacheDuration": 120,  // Minutes
     "MaxCacheEntries": 1000
   }
   ```

2. **Use Local Providers** (Zero latency)
   ```bash
   # Install Ollama for fastest local inference
   curl -fsSL https://ollama.com/install.sh | sh
   ollama pull qwen2.5:7b  # Smaller, faster model
   ```

3. **Optimize Sampling Strategy**
   - Use "Minimal" for local models
   - Use "Balanced" for cloud providers
   - Only use "Comprehensive" for premium providers

4. **Reduce Recommendation Count**
   - Start with 5-10 recommendations
   - Increase only if needed

## Cache Optimization

### Cache Configuration

```csharp
// Default cache settings in BrainarrConstants.cs
public const int CacheDurationMinutes = 60;  // Adjust based on needs
public const int MaxCacheSize = 100;         // Number of entries
```

### Cache Strategy by Use Case

| Use Case | Cache Duration | Strategy |
|----------|---------------|----------|
| Stable Library | 4-6 hours | Aggressive caching |
| Growing Library | 1-2 hours | Moderate caching |
| Rapid Changes | 30 minutes | Light caching |
| Testing | 5 minutes | Minimal caching |

### Custom Cache Implementation

```csharp
// For advanced users: Custom cache with Redis
public class RedisRecommendationCache : IRecommendationCache
{
    private readonly IConnectionMultiplexer _redis;
    
    public RedisRecommendationCache(string connectionString)
    {
        _redis = ConnectionMultiplexer.Connect(connectionString);
    }
    
    public bool TryGet(string key, out List<ImportListItemInfo> recommendations)
    {
        var db = _redis.GetDatabase();
        var cached = db.StringGet(key);
        
        if (cached.HasValue)
        {
            recommendations = JsonSerializer.Deserialize<List<ImportListItemInfo>>(cached);
            return true;
        }
        
        recommendations = null;
        return false;
    }
    
    public void Set(string key, List<ImportListItemInfo> recommendations, TimeSpan duration)
    {
        var db = _redis.GetDatabase();
        var json = JsonSerializer.Serialize(recommendations);
        db.StringSet(key, json, duration);
    }
}
```

### Cache Warming

```bash
#!/bin/bash
# warm-cache.sh - Pre-populate cache during off-peak hours

# Run at 3 AM via cron
# 0 3 * * * /usr/local/bin/warm-cache.sh

curl -X POST http://localhost:8686/api/v1/command \
  -H "X-Api-Key: YOUR_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name": "RefreshImportList", "importListId": BRAINARR_ID}'
```

## Rate Limiting Configuration

### Default Rate Limits

```csharp
// RateLimiterConfiguration.cs
public static void ConfigureDefaults(IRateLimiter limiter)
{
    // Local providers - unlimited
    limiter.ConfigureLimit("ollama", int.MaxValue, int.MaxValue);
    limiter.ConfigureLimit("lmstudio", int.MaxValue, int.MaxValue);
    
    // Cloud providers - respect API limits
    limiter.ConfigureLimit("openai", 500, 200000);      // 500 RPM, 200K TPM
    limiter.ConfigureLimit("anthropic", 50, 100000);    // 50 RPM, 100K TPM
    limiter.ConfigureLimit("gemini", 15, 1000000);      // 15 RPM (free tier)
    limiter.ConfigureLimit("groq", 30, 100000);         // 30 RPM
    limiter.ConfigureLimit("deepseek", 60, 500000);     // 60 RPM
    limiter.ConfigureLimit("perplexity", 50, 100000);   // 50 RPM
}
```

### Custom Rate Limiting

```csharp
// Implement token bucket algorithm for smoother rate limiting
public class TokenBucketRateLimiter : IRateLimiter
{
    private readonly Dictionary<string, TokenBucket> _buckets = new();
    
    public async Task<T> ExecuteAsync<T>(string key, Func<Task<T>> action)
    {
        var bucket = GetOrCreateBucket(key);
        
        await bucket.WaitForTokenAsync();
        
        try
        {
            return await action();
        }
        finally
        {
            bucket.ReturnToken();
        }
    }
}
```

### Adaptive Rate Limiting

```csharp
// Automatically adjust rates based on response codes
public class AdaptiveRateLimiter
{
    public void AdjustLimits(string provider, int statusCode)
    {
        if (statusCode == 429) // Rate limit hit
        {
            // Reduce rate by 20%
            var currentRate = GetCurrentRate(provider);
            SetRate(provider, (int)(currentRate * 0.8));
        }
        else if (statusCode == 200) // Success
        {
            // Gradually increase rate
            var currentRate = GetCurrentRate(provider);
            SetRate(provider, Math.Min(currentRate + 1, GetMaxRate(provider)));
        }
    }
}
```

## Memory Optimization

### Memory Configuration

```xml
<!-- Lidarr config.xml -->
<Config>
  <!-- Limit Lidarr memory usage -->
  <MaxMemoryMB>2048</MaxMemoryMB>
  
  <!-- Reduce cache sizes -->
  <CacheSize>100</CacheSize>
  
  <!-- Disable unnecessary features -->
  <EnableDetailedLogging>false</EnableDetailedLogging>
</Config>
```

### .NET Memory Settings

```bash
# Environment variables for .NET runtime
export DOTNET_GCHeapCount=0x4        # 4 heaps for GC
export DOTNET_GCLatencyMode=1        # Low latency mode
export DOTNET_GCConserveMemory=1     # Conservative memory usage
export DOTNET_TieredCompilation=1    # Enable tiered compilation
```

### Memory-Efficient Models

| Provider | Model | Memory Usage | Speed | Quality |
|----------|-------|--------------|-------|---------|
| Ollama | qwen2.5:3b | 2 GB | Fast | Good |
| Ollama | qwen2.5:7b | 4 GB | Medium | Better |
| Ollama | llama3.2:3b | 2 GB | Fast | Good |
| Ollama | phi3:mini | 2 GB | Very Fast | Decent |
| LM Studio | Any 7B GGUF Q4 | 4 GB | Medium | Good |

### Garbage Collection Tuning

```csharp
// Force garbage collection after heavy operations
public void OptimizeMemory()
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    
    // Compact large object heap
    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
    GC.Collect();
}
```

## Database Optimization

### SQLite Optimization

```bash
# Optimize Lidarr database
sqlite3 /var/lib/lidarr/lidarr.db <<EOF
PRAGMA optimize;
PRAGMA analysis_limit=1000;
ANALYZE;
VACUUM;
PRAGMA wal_checkpoint(TRUNCATE);
EOF
```

### Database Maintenance Script

```bash
#!/bin/bash
# optimize-db.sh - Run weekly

DB_PATH="/var/lib/lidarr/lidarr.db"
BACKUP_PATH="/backup/lidarr-$(date +%Y%m%d).db"

# Backup first
cp "$DB_PATH" "$BACKUP_PATH"

# Optimize
sqlite3 "$DB_PATH" <<EOF
-- Clean old history (keep 30 days)
DELETE FROM History WHERE Date < datetime('now', '-30 days');

-- Clean old logs
DELETE FROM Logs WHERE Time < datetime('now', '-7 days');

-- Optimize tables
PRAGMA optimize;
ANALYZE;
VACUUM;

-- Checkpoint WAL
PRAGMA wal_checkpoint(TRUNCATE);
EOF

echo "Database optimized. Size before: $(du -h $BACKUP_PATH | cut -f1)"
echo "Size after: $(du -h $DB_PATH | cut -f1)"
```

### Index Optimization

```sql
-- Add indexes for Brainarr queries
CREATE INDEX IF NOT EXISTS idx_importlist_provider 
ON ImportLists(Implementation);

CREATE INDEX IF NOT EXISTS idx_importliststatus_lastsync 
ON ImportListStatus(LastInfoSync);

CREATE INDEX IF NOT EXISTS idx_history_date 
ON History(Date);
```

## Provider-Specific Optimizations

### Ollama Optimization

```bash
# Use GPU acceleration
OLLAMA_CUDA_VISIBLE_DEVICES=0 ollama serve

# Increase parallel requests
OLLAMA_NUM_PARALLEL=4 ollama serve

# Keep models in memory
OLLAMA_KEEP_ALIVE=24h ollama serve

# Optimize for your hardware
OLLAMA_MAX_LOADED_MODELS=2 \
OLLAMA_NUM_GPU=1 \
OLLAMA_GPU_MEMORY_FRACTION=0.8 \
ollama serve
```

### OpenAI Optimization

```csharp
// Use streaming for faster perceived response
public async IAsyncEnumerable<string> StreamRecommendationsAsync(string prompt)
{
    var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
    request.Content = JsonContent.Create(new
    {
        model = "gpt-4o-mini",  // Faster, cheaper model
        messages = new[] { new { role = "user", content = prompt } },
        stream = true,
        max_tokens = 500,  // Limit response size
        temperature = 0.7   // Balance creativity/consistency
    });
    
    // Process streaming response
    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    using var stream = await response.Content.ReadAsStreamAsync();
    // ... process stream
}
```

### Cloud Provider Optimization

```json
// Optimal settings per provider
{
  "OpenAI": {
    "Model": "gpt-4o-mini",
    "MaxTokens": 500,
    "Temperature": 0.7
  },
  "Anthropic": {
    "Model": "claude-3-5-haiku",
    "MaxTokens": 1000,
    "Temperature": 0.5
  },
  "Gemini": {
    "Model": "gemini-1.5-flash",
    "MaxTokens": 800,
    "Temperature": 0.6
  },
  "DeepSeek": {
    "Model": "deepseek-chat",
    "MaxTokens": 1000,
    "Temperature": 0.7
  }
}
```

## System Tuning

### Linux Kernel Parameters

```bash
# /etc/sysctl.d/99-brainarr.conf

# Network optimizations
net.core.rmem_max = 134217728
net.core.wmem_max = 134217728
net.ipv4.tcp_rmem = 4096 87380 134217728
net.ipv4.tcp_wmem = 4096 65536 134217728
net.core.netdev_max_backlog = 5000

# File system
fs.file-max = 2097152
fs.inotify.max_user_watches = 524288

# Memory management
vm.swappiness = 10
vm.dirty_ratio = 15
vm.dirty_background_ratio = 5
```

### Docker Optimization

```yaml
# docker-compose.yml optimizations
services:
  lidarr:
    image: linuxserver/lidarr
    deploy:
      resources:
        limits:
          cpus: '2.0'
          memory: 2G
        reservations:
          cpus: '0.5'
          memory: 512M
    sysctls:
      - net.core.somaxconn=1024
      - net.ipv4.tcp_fin_timeout=30
    ulimits:
      nofile:
        soft: 65536
        hard: 65536
```

### Storage Optimization

```bash
# Use SSD for database
ln -s /ssd/lidarr.db /var/lib/lidarr/lidarr.db

# Use memory for cache
mount -t tmpfs -o size=1G tmpfs /var/lib/lidarr/.cache

# Enable compression for logs
logrotate /var/log/lidarr/*.txt {
    compress
    delaycompress
    size 10M
    rotate 5
}
```

## Monitoring Performance

### Key Metrics to Track

```bash
# Response time monitoring
grep "responseTime" /var/log/lidarr/lidarr.txt | \
  awk '{print $NF}' | \
  awk '{sum+=$1; count++} END {print "Avg:", sum/count, "ms"}'

# Cache hit rate
grep -c "Cache hit" /var/log/lidarr/lidarr.txt
grep -c "Cache miss" /var/log/lidarr/lidarr.txt

# Provider success rate
grep "Brainarr.*success" /var/log/lidarr/lidarr.txt | wc -l
grep "Brainarr.*fail" /var/log/lidarr/lidarr.txt | wc -l
```

### Performance Dashboard

```sql
-- SQLite queries for performance metrics

-- Average response time by provider
SELECT 
    Provider,
    AVG(ResponseTime) as AvgResponseTime,
    MIN(ResponseTime) as MinResponseTime,
    MAX(ResponseTime) as MaxResponseTime,
    COUNT(*) as RequestCount
FROM ProviderMetrics
WHERE Timestamp > datetime('now', '-24 hours')
GROUP BY Provider;

-- Cache effectiveness
SELECT 
    DATE(Timestamp) as Date,
    SUM(CASE WHEN CacheHit = 1 THEN 1 ELSE 0 END) as Hits,
    SUM(CASE WHEN CacheHit = 0 THEN 1 ELSE 0 END) as Misses,
    CAST(SUM(CASE WHEN CacheHit = 1 THEN 1 ELSE 0 END) AS FLOAT) / COUNT(*) * 100 as HitRate
FROM CacheMetrics
GROUP BY DATE(Timestamp)
ORDER BY Date DESC
LIMIT 30;
```

### Prometheus Metrics

```yaml
# prometheus.yml
scrape_configs:
  - job_name: 'brainarr'
    static_configs:
      - targets: ['localhost:8686']
    metrics_path: '/metrics'
    params:
      module: ['brainarr']
```

## Performance Benchmarks

### Expected Performance

| Operation | Target Time | Acceptable | Slow |
|-----------|-------------|------------|------|
| Provider Test | <500ms | <2s | >2s |
| Get 10 Recommendations (Local) | <5s | <10s | >10s |
| Get 10 Recommendations (Cloud) | <8s | <15s | >15s |
| Cache Hit | <10ms | <50ms | >50ms |
| Library Analysis (1000 artists) | <1s | <3s | >3s |

### Optimization Results

| Optimization | Performance Gain | Implementation Effort |
|--------------|-----------------|----------------------|
| Enable Caching | 60-80% API reduction | Low |
| Use Local Provider | 100% cost reduction | Medium |
| Optimize Sampling | 30-50% token reduction | Low |
| Database Indexes | 20-40% query speedup | Low |
| Memory Tuning | 15-25% reduction | Medium |
| Rate Limiting | Prevents 429 errors | Low |

## Troubleshooting Performance

### High CPU Usage

```bash
# Identify CPU-intensive processes
top -H -p $(pgrep -f lidarr)

# Check for runaway queries
sqlite3 /var/lib/lidarr/lidarr.db "SELECT * FROM sqlite_stat1;"

# Limit concurrent operations
echo 1 > /proc/sys/kernel/sched_rt_runtime_us
```

### High Memory Usage

```bash
# Check memory usage
ps aux | grep lidarr
pmap -x $(pgrep -f lidarr)

# Clear caches
sync && echo 3 > /proc/sys/vm/drop_caches

# Restart with memory limit
systemd-run --scope -p MemoryLimit=2G lidarr
```

### Slow API Responses

1. Switch to faster provider (Groq, Gemini Flash)
2. Reduce prompt size (Minimal sampling)
3. Lower recommendation count
4. Enable response streaming
5. Use smaller models

## Best Practices

1. **Start Small**: Begin with 5 recommendations, increase gradually
2. **Cache Aggressively**: Longer cache = fewer API calls
3. **Monitor Metrics**: Track performance regularly
4. **Optimize Iteratively**: Make one change at a time
5. **Document Changes**: Keep notes on what improves performance
6. **Test Thoroughly**: Verify optimizations don't break functionality
7. **Plan Capacity**: Scale resources before hitting limits

## Resources

- [.NET Performance Tips](https://docs.microsoft.com/en-us/dotnet/core/performance/)
- [SQLite Optimization](https://www.sqlite.org/optoverview.html)
- [Docker Performance Tuning](https://docs.docker.com/config/containers/resource_constraints/)
- [Linux Performance Tools](https://www.brendangregg.com/linuxperf.html)