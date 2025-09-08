# Brainarr Plugin Lifecycle Documentation

This document explains how Lidarr loads, initializes, and manages the Brainarr plugin throughout its lifecycle.

## Plugin Architecture Overview

Brainarr is implemented as an Import List plugin for Lidarr, inheriting from `ImportListBase<BrainarrSettings>` and implementing the `IImportList` interface.

```text
Lidarr Core
    ↓
ImportListBase<T>
    ↓
BrainarrImportList
    ↓
AI Providers & Services
```

## Lifecycle Phases

### 1. Plugin Discovery (Startup)

When Lidarr starts, it scans the plugins directory for assemblies:

```text
/var/lib/lidarr/plugins/RicherTunes/Brainarr/
├── Brainarr.Plugin.dll          # Main plugin assembly
├── plugin.json                   # Plugin manifest
└── Dependencies/                 # Additional dependencies
```

**Process:**

1. Lidarr scans `/plugins/` directory
2. Loads assemblies matching pattern `*.Plugin.dll`
3. Reads `plugin.json` for metadata validation
4. Registers plugin types with dependency injection container

### 2. Plugin Registration

Lidarr uses reflection to discover plugin classes:

```csharp
[ImportListDefinition(
    "Brainarr",
    typeof(BrainarrSettings),
    ImportListType.Music)]
public class BrainarrImportList : ImportListBase<BrainarrSettings>
```

**Registration Steps:**

1. Finds classes inheriting from `ImportListBase<T>`
2. Reads `ImportListDefinition` attribute
3. Registers in import list factory
4. Creates singleton service instances

### 3. Dependency Injection

Brainarr's constructor receives Lidarr services via DI:

```csharp
public BrainarrImportList(
    IHttpClient httpClient,           // HTTP operations
    IImportListStatusService statusService,  // Status tracking
    IConfigService configService,     // Configuration
    IParsingService parsingService,   // Metadata parsing
    IArtistService artistService,     // Artist database
    IAlbumService albumService,       // Album database
    Logger logger)                    // Logging
```

**Service Lifecycle:**

- Services are singleton instances managed by Lidarr
- Plugin cannot create its own service instances
- Must use provided interfaces for all operations

### 4. Configuration Loading

When user accesses Settings > Import Lists > Brainarr:

```text
User Opens Settings
    ↓
Lidarr calls GetDefaultSettings()
    ↓
BrainarrSettings instance created
    ↓
Field definitions rendered in UI
    ↓
User modifies settings
    ↓
Validation via IValidationResult
    ↓
Settings persisted to database
```

**Settings Management:**

```csharp
// Settings are loaded from database
var settings = _configService.GetSettings<BrainarrSettings>(Id);

// Settings are validated
var validation = settings.Validate();

// Settings are saved
_configService.SaveSettings(Id, settings);
```

### 5. Execution Cycle

Brainarr executes on Lidarr's import list schedule:

```text
Scheduled Timer (configurable interval)
    ↓
ImportListSyncService triggers
    ↓
Calls Fetch() on each import list
    ↓
BrainarrImportList.Fetch() executes
    ↓
Returns List<ImportListItemInfo>
    ↓
Lidarr processes recommendations
    ↓
New albums added to wanted list
```

**Default Schedule:**

- Runs every 6 hours (configurable)
- Can be triggered manually via UI
- Respects rate limiting per provider

### 6. Core Method Execution Flow

#### Fetch() Method Lifecycle

```csharp
public override async Task<List<ImportListItemInfo>> Fetch()
{
    // 1. Initialize services
    InitializeServices();

    // 2. Analyze library
    var profile = _libraryAnalyzer.AnalyzeLibrary();

    // 3. Build AI prompt
    var prompt = BuildPrompt(profile);

    // 4. Get recommendations (with failover)
    var recommendations = await _aiService.GetRecommendationsAsync(prompt);

    // 5. Convert to Lidarr format
    return ConvertToImportListItems(recommendations);
}
```

**Execution Context:**

- Runs in Lidarr's task scheduler
- Has configurable timeout (default: 120s)
- Errors are logged but don't crash Lidarr
- Failed fetches retry on next schedule

### 7. Provider Chain Execution

When getting recommendations:

```text
AIService.GetRecommendationsAsync()
    ↓
Iterate through provider chain by priority
    ↓
For each provider:
    1. Check health status
    2. Apply rate limiting
    3. Execute with retry policy
    4. Validate recommendations
    5. Return on success OR continue to next
    ↓
All providers failed → Return empty list
```

### 8. Error Handling & Recovery

**Error Boundaries:**

```csharp
try
{
    // Plugin execution
}
catch (Exception ex)
{
    _logger.Error(ex, "Import list error");
    _statusService.RecordFailure(Id);
    return new List<ImportListItemInfo>();
}
```

**Recovery Mechanisms:**

- Automatic retry on next schedule
- Provider failover for resilience
- Health monitoring prevents repeated failures
- Circuit breaker pattern for providers

### 9. Resource Management

**Memory Management:**

- Services are singleton (shared across requests)
- Recommendations cached with expiration
- Large responses streamed (not buffered)
- Proper disposal of HTTP clients

**Connection Management:**

```csharp
// HTTP client is reused (provided by Lidarr)
_httpClient.Execute(request);

// Connections pooled automatically
// No manual connection management needed
```

### 10. Plugin Shutdown

When Lidarr stops or plugin is disabled:

```text
Lidarr shutdown initiated
    ↓
Cancellation tokens signaled
    ↓
Active operations cancelled
    ↓
Plugin disposal (if IDisposable)
    ↓
Services cleaned up
    ↓
Assembly unloaded
```

**Cleanup Operations:**

- In-flight requests cancelled via CancellationToken
- Caches are not persisted (memory only)
- No explicit cleanup required (managed by Lidarr)

## State Management

### Persistent State

Stored in Lidarr's database:

- Plugin configuration (BrainarrSettings)
- Import list status (last run, errors)
- Added albums history

### Transient State

Stored in memory during execution:

- Provider health metrics
- Recommendation cache
- Rate limit windows
- Active request tracking

## Threading Model

**Execution Context:**

- Fetch() runs on thread pool thread
- Async/await for I/O operations
- No manual thread creation
- Thread-safe collections for shared state

**Concurrency Handling:**

```csharp
// Thread-safe cache operations
private readonly ConcurrentDictionary<string, CacheEntry> _cache;

// Thread-safe rate limiting
private readonly SemaphoreSlim _rateLimitSemaphore;
```

## Security Context

**Permissions:**

- Runs under Lidarr service account
- Network access for AI providers
- Read access to music library
- Write access to Lidarr database

**Isolation:**

- Cannot access file system directly
- Cannot modify Lidarr core behavior
- Cannot create system services
- Sandboxed within plugin boundary

## Performance Considerations

### Startup Impact

- Plugin load time: < 100ms
- Service initialization: < 50ms
- No blocking operations during init

### Runtime Performance

- Fetch execution: 5-30s typical
- Memory usage: < 50MB baseline
- CPU usage: < 5% during fetch

### Optimization Strategies

1. **Lazy Initialization**: Services created on-demand
2. **Connection Reuse**: HTTP client pooling
3. **Response Caching**: Reduces API calls
4. **Async Operations**: Non-blocking I/O

## Monitoring & Diagnostics

### Health Indicators

```csharp
// Plugin health check
public override ValidationResult Test()
{
    var health = _aiService.TestAllProvidersAsync().Result;
    return new ValidationResult(health);
}
```

### Logging Integration

```csharp
_logger.Debug("Starting fetch cycle");
_logger.Info($"Found {count} recommendations");
_logger.Warn("Provider {name} unavailable");
_logger.Error(ex, "Fetch failed");
```

### Metrics Collection

- Request count per provider
- Response times
- Success/failure rates
- Cache hit ratios

## Integration Points

### Database Access

Via Lidarr services only:

```csharp
_artistService.GetAllArtists();
_albumService.GetAlbumsByArtist(artistId);
```

### HTTP Operations

Via IHttpClient interface:

```csharp
var request = new HttpRequestBuilder(url).Build();
var response = _httpClient.Execute(request);
```

### Event System

Plugin can't subscribe to events directly but responds to:

- Configuration changes
- Manual triggers
- Scheduled executions

## Best Practices

### Do's

- Use provided Lidarr services
- Handle all exceptions gracefully
- Log important operations
- Validate all user input
- Implement timeout handling
- Cache expensive operations

### Don'ts

- Don't create threads manually
- Don't access file system directly
- Don't modify global state
- Don't hold locks across async calls
- Don't ignore cancellation tokens
- Don't leak sensitive data in logs

## Troubleshooting Lifecycle Issues

### Plugin Not Loading

1. Check assembly is in correct location
2. Verify plugin.json is valid
3. Check .NET version compatibility
4. Review Lidarr logs for load errors

### Configuration Not Saving

1. Verify validation passes
2. Check field definitions
3. Review database permissions
4. Check for serialization errors

### Fetch Not Running

1. Verify import list is enabled
2. Check schedule configuration
3. Review last error in status
4. Check provider availability

### Memory Leaks

1. Ensure proper disposal patterns
2. Clear caches periodically
3. Avoid capturing contexts
4. Profile memory usage

## Version Compatibility

| Lidarr Version | Plugin Version | Status |
|---------------|----------------|---------|
| 4.0.0+        | 1.0.0          | ✅ Supported |
| 3.x.x         | -              | ❌ Not supported |
| 5.0.0         | TBD            | 🔄 Future |

---

Last Updated: 2024-12-20
