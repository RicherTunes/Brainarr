# ⚙️ Advanced Settings - Fine-Tuning Brainarr

Complete guide to Brainarr's advanced configuration options for power users and specific use cases.

## 🎛️ **Core Settings Deep Dive**

### **Recommendation Modes**

#### **Specific Albums Mode (Default)**
- **What it does**: Recommends individual albums to import
- **Best for**: Curated discovery, storage management
- **Lidarr behavior**: Only imports the specific recommended albums
- **Use case**: When you want control over what gets added

#### **Artists Mode** 
- **What it does**: Recommends artists (Lidarr imports ALL their albums)
- **Best for**: Deep artist discovery, filling discographies  
- **⚠️ Warning**: Can import large amounts of music automatically
- **Use case**: When you want complete artist collections

### **Discovery Mode Settings**

Based on code analysis of LibraryAnalyzer.cs:

#### **Similar (Conservative)**
```csharp
// Analyzes existing library heavily
var genreWeight = 0.8;        // Heavily weight existing genres
var artistWeight = 0.9;       // Stay close to known artists
var eras = ExistingEras;      // Only decades you already own
var explorationFactor = 0.1;  // 10% exploration
```

#### **Adjacent (Balanced - Default)**
```csharp  
var genreWeight = 0.6;        // Balance known and related genres
var artistWeight = 0.7;       // Include artist collaborations
var eras = ExistingEras + Related; // Include adjacent decades
var explorationFactor = 0.3;  // 30% exploration
```

#### **Exploratory (Adventurous)**
```csharp
var genreWeight = 0.4;        // Open to genre exploration  
var artistWeight = 0.5;       // Discover new artists
var eras = AllEras;           // All time periods
var explorationFactor = 0.5;  // 50% exploration
```

### **Sampling Strategy**

#### **Minimal**
- **Library Analysis**: Top 20 artists, 5 genres
- **Processing Speed**: Fastest
- **Best for**: Local providers, quick tests
- **Quality Trade-off**: Less personalized

#### **Balanced (Default)**
- **Library Analysis**: Top 50 artists, 10 genres  
- **Processing Speed**: Medium
- **Best for**: Most use cases
- **Quality**: Good personalization

#### **Comprehensive**
- **Library Analysis**: Top 100 artists, all genres
- **Processing Speed**: Slower
- **Best for**: Premium providers, large libraries
- **Quality**: Maximum personalization

---

## 🔧 **Performance Tuning**

### **Timeout Configuration**

**Default Values (from Constants.cs):**
```csharp
DefaultAITimeout = 30;          // API request timeout
MaxAITimeout = 120;             // Maximum allowed  
ModelDetectionTimeout = 10;     // Model discovery
TestConnectionTimeout = 10;     // Connection test
HealthCheckTimeoutMs = 5000;    // Health monitoring
```

**Optimization Guidelines:**

**Fast Networks + Local Providers:**
```
AI Timeout: 15-30 seconds
Connection Test: 5 seconds  
Health Check: 3 seconds
```

**Slow Networks + Cloud Providers:**
```
AI Timeout: 60-120 seconds
Connection Test: 15 seconds
Health Check: 10 seconds  
```

### **Rate Limiting Fine-Tuning**

**Built-in Rate Limits (from code):**
```csharp
// Default rate limiting
RequestsPerMinute = 10;         // Conservative default
BurstSize = 5;                  // Initial burst allowed
```

**Provider-Specific Optimization:**

**Local Providers (Ollama/LM Studio):**
```
Requests Per Minute: 30-60    // No external API limits
Burst Size: 10               // Higher burst for local
```

**Budget Cloud Providers:**
```  
Requests Per Minute: 15      // Respect free tier limits
Burst Size: 3               // Conservative burst
```

**Premium Cloud Providers:**
```
Requests Per Minute: 20-30   // Higher paid tier limits  
Burst Size: 5-10            // Moderate burst
```

### **Cache Optimization**

**Cache Configuration (from RecommendationCache.cs):**
```csharp
CacheDurationMinutes = 60;      // Default cache lifetime
MaxCacheEntries = 100;          // Maximum cached items
```

## Iterative Top-Up {#iterative-top-up}

- Purpose: When the model returns duplicates or library-matches and the final unique count is below your target, Brainarr can automatically “top up” by requesting more items with feedback to the model.
- Behavior:
  - Local providers (Ollama, LM Studio): Top‑up is enabled by default for best fill rates.
  - Cloud providers: Controlled via the Advanced setting `Iterative Top-Up` (EnableIterativeRefinement).
- Safety Gates: The same confidence/MBID rules apply to top‑up items.
- Artist Mode MBIDs: In `Artists` mode, only the artist MBID is required when `Require MBIDs` is enabled (album MBIDs are not required for artist‑only recommendations).

Tips:
- If you want to minimize extra requests, turn off `Iterative Top-Up` for cloud providers, or reduce your target count.
- Check logs for messages like “Under target by N; starting iterative top‑up” and “Top‑up added X items; total now Y/Target.”

### Hysteresis Controls {#hysteresis-controls}

- Purpose: Avoid wasted iterations when the model repeatedly returns off‑spec or low‑quality items.
- Fields (Advanced):
  - Top‑Up Max Iterations: Hard cap (default 3)
  - Top‑Up Zero‑Success Stop: Stop after this many zero‑unique iterations (default 1)
  - Top‑Up Low‑Success Stop: Stop after this many <70% unique iterations (default 2)
  - Top‑Up Cooldown (ms): Delay before stopping (local providers), to reduce model churn (default 1000ms)

Notes:
- Hysteresis applies to both Artists and Albums modes.
- Validation feedback (reasons and examples) are fed into the next iteration to steer the model away from previous mistakes.

## Library Sampling {#library-sampling}

- Minimal: Fast with enough signal to reduce hallucinations
  - Local providers: ~2400 tokens
  - Use when you want quick scans or frequent refreshes
- Balanced: Better trade‑off between quality and speed
  - Local providers: ~3500 tokens; Cloud: ~3000 tokens
  - Recommended default
- Comprehensive: Fullest context for best match quality
  - Local providers: ~5000 tokens; Cloud: ~4000 tokens
  - Best for premium/cloud models; works with powerful local models (Qwen3, Llama) when you want deeper discovery

## Discovery Mode {#discovery-mode}

- Similar: Stick close to your library’s core profile
- Adjacent: Explore related genres/styles (default)
- Exploratory: Venture into new genres with lower prior representation

## Recommendation Type {#recommendation-type}

- Specific Albums: Returns concrete album recommendations (Artist + Album + Year)
- Artists: Returns artist‑only recommendations (Lidarr will import all albums)
  - In Artists mode, validation allows artist‑only responses and MBID gating requires only Artist MBID (if enabled)

## Safety Gates {#safety-gates}

- Minimum Confidence: Drop/queue items below this threshold (0.0–1.0)
- Require MusicBrainz IDs: Enforce MBID presence
  - Artists mode: requires only Artist MBID
  - Albums mode: requires both Artist and Album MBIDs
- Queue Borderline Items: Send low‑confidence or missing‑MBID items to Review Queue instead of dropping

**Tuning by Use Case:**

**Active Discovery (frequently changing taste):**
```
Cache Duration: 30 minutes   // Fresher recommendations
Max Entries: 50             // Less memory usage
```

**Stable Library (established taste):**
```
Cache Duration: 120 minutes  // Longer caching
Max Entries: 200            // More cached variety
```

**Testing/Development:**
```
Cache Duration: 5 minutes    // Rapid iteration
Max Entries: 10             // Minimal caching
```

---

## 🧠 **AI Model Configuration**

### **Temperature Settings**

**Understanding Temperature:**
- **0.0**: Deterministic, same results every time
- **0.3-0.5**: Slight variation, conservative
- **0.7**: Balanced creativity (default for most providers)
- **0.9-1.0**: High creativity, unpredictable results

**Optimal by Provider (from code):**
```
Ollama:       0.7 (balanced)
LM Studio:    0.7 (balanced)  
OpenAI:       0.8 (slightly creative)
Anthropic:    0.8 (slightly creative)
Gemini:       0.8 (slightly creative)
Groq:         0.7 (balanced)
Others:       0.7 (safe default)
```

### **Token Limits**

**Default Limits (from provider implementations):**
```
Max Tokens: 2000            // Response length limit
Context Window: Varies      // Input + output combined
```

**Optimization by Model:**
```
Small Models (7B): 1000-1500 tokens    // Prevent quality degradation
Large Models (70B+): 2000-4000 tokens // Can handle longer responses
```

---

## 🛡️ **Security Hardening**

### **API Key Management**

**Production Security:**
```bash
# Use environment variables instead of direct config
export BRAINARR_OPENAI_KEY="sk-..."
export BRAINARR_ANTHROPIC_KEY="sk-ant-..."

# Limit network access (Linux firewall)
sudo ufw allow out 443         # HTTPS only  
sudo ufw deny out 80           # Block HTTP
sudo ufw deny in 8686          # Restrict Lidarr access if needed
```

### **Input Sanitization**

**Brainarr's Built-in Protection:**
```csharp
// Automatic URL validation
BeValidUrl(url);               // Blocks javascript:, file:, etc.

// JSON security  
SecureJsonSerializer.Create(); // Prevents prototype pollution

// API key validation
ValidateApiKeyFormat(key);     // Format validation per provider
```

### **Custom Security Filters**

**Add Custom Hallucination Patterns:**
```
Settings → Import Lists → Brainarr → Advanced Settings
Custom Filter Patterns:
- "AI Version"           // Blocks AI-generated version labels
- "Extended Universe"    // Blocks fictional universe albums  
- "Director's Cut"       // Blocks movie-style album names
- "Redux"               // Blocks remake-style naming
```

---

## 📊 **Monitoring & Analytics**

### **Built-in Metrics**

**Health Monitoring (from ProviderHealthMonitor.cs):**
```csharp
// Automatic tracking per provider:
SuccessRate = successfulRequests / totalRequests;
ConsecutiveFailures = count;
AverageResponseTime = totalTime / requestCount;
LastHealthCheck = DateTime.UtcNow;
```

**Performance Metrics:**
- **Cache Hit Rate**: Percentage of requests served from cache
- **Provider Response Times**: Average API response times
- **Recommendation Success Rate**: Percentage of valid recommendations
- **Error Patterns**: Common failure modes for optimization

### **Custom Monitoring Setup**

**Log Analysis:**
```bash
# Monitor success patterns
grep "Generated.*recommendations" /var/lib/lidarr/.config/Lidarr/logs/lidarr.txt

# Track provider health  
grep "Provider.*health" /var/lib/lidarr/.config/Lidarr/logs/lidarr.txt

# Monitor performance
grep "Response time" /var/lib/lidarr/.config/Lidarr/logs/lidarr.txt
```

**Performance Dashboard (Custom):**
```bash
#!/bin/bash
# Simple performance monitoring
echo "=== Brainarr Performance Report ==="
echo "Cache Hit Rate: $(grep 'Cache hit' lidarr.txt | wc -l)/$(grep 'Cache' lidarr.txt | wc -l)"
echo "Average Response Time: $(grep 'Response time:' lidarr.txt | awk '{sum+=$5; count++} END {print sum/count "ms"}')"
echo "Success Rate: $(grep 'Generated.*recommendations' lidarr.txt | wc -l)/$(grep 'recommendation generation' lidarr.txt | wc -l)"
```

---

## 🔬 **Expert-Level Customization**

### **Custom Prompt Engineering**

**Understanding Brainarr's Prompts (from LibraryAwarePromptBuilder.cs):**

**Base Prompt Structure:**
```
1. Library Analysis Summary (genres, artists, eras)
2. Recommendation Guidelines (format, requirements)  
3. Context Information (discovery mode, sampling)
4. Output Format Specification (JSON schema)
5. Quality Constraints (no duplicates, real albums only)
```

**Customization Points:**
- **Discovery Mode**: Affects exploration vs. similarity weighting
- **Sampling Strategy**: Controls library analysis depth
- **Custom Filters**: Add business logic for recommendation filtering

### **Multi-Provider Load Balancing**

**Advanced Failover Configuration:**
```csharp
// Provider priority configuration
Primary.Priority = 1;          // Lowest number = highest priority
Secondary.Priority = 2;        // Backup provider
Emergency.Priority = 3;        // Local/offline provider

// Health-based routing
HealthThreshold = 0.7;         // 70% success rate minimum
ConsecutiveFailureLimit = 5;   // Switch after 5 failures
```

**Geographic Distribution:**
- **Primary**: Local region provider (lowest latency)
- **Secondary**: Different region provider (redundancy)
- **Tertiary**: Local provider (always available)

### **Custom Validation Rules**

**Extend RecommendationValidator.cs patterns:**
```csharp
// Add custom business rules
CustomPatterns = new[]
{
    @"your-custom-pattern",     // Your specific filters
    @"another-pattern",         // Additional validation
};

// Custom confidence scoring
MinConfidenceThreshold = 0.6;   // Minimum AI confidence
MaxConfidenceThreshold = 0.95;  // Maximum (detect overconfidence)
```

---

## 🎯 **Configuration Profiles**

### **Profile: Privacy Maximalist**
```
Provider: Ollama
URL: http://localhost:11434  
Model: qwen2.5:latest
Max Recommendations: 15
Discovery Mode: Exploratory  
Cache Duration: 120 minutes
Rate Limiting: Disabled (local)
Custom Filters: Aggressive
```

### **Profile: Cost Optimizer**  
```
Provider: DeepSeek
API Key: [your-key]
Model: deepseek-chat
Max Recommendations: 30
Discovery Mode: Adjacent
Cache Duration: 120 minutes  
Rate Limiting: 15/minute
Sampling: Comprehensive
```

### **Profile: Speed Demon**
```
Provider: Groq
API Key: [your-key]  
Model: llama-3.3-70b-versatile
Max Recommendations: 20
Discovery Mode: Similar
Cache Duration: 60 minutes
Rate Limiting: 25/minute
Sampling: Minimal
```

### **Profile: Quality Maximalist**
```
Provider: Anthropic
API Key: [your-key]
Model: claude-3-5-sonnet-latest  
Max Recommendations: 50
Discovery Mode: Exploratory
Sampling: Comprehensive
Cache Duration: 30 minutes
Custom Validation: Strict
```

---

## 🧪 **Testing Advanced Configurations**

### **A/B Testing Different Providers**
1. **Configure Provider A**: Run for 1 week, note results
2. **Switch to Provider B**: Same settings, different AI
3. **Compare Results**: Quality, speed, cost, reliability
4. **Choose Winner**: Based on your specific priorities

### **Performance Benchmarking**
```bash
# Time full recommendation cycle
time curl -X POST localhost:8686/api/v1/importlist/test

# Monitor resource usage
htop -p $(pgrep Lidarr)        # CPU/memory
nethogs                        # Network usage
iotop                          # Disk I/O
```

### **Quality Assessment**
```
Metrics to Track:
✅ Relevance: How well recommendations match your taste
✅ Discovery: Percentage of recommendations that are genuinely new
✅ Accuracy: No hallucinations or non-existent content  
✅ Variety: Good mix across subgenres and eras
✅ Availability: Percentage found by your indexers
```

---

## 🔄 **Configuration Management**

### **Backup Current Settings**
```bash
# Export Lidarr configuration (includes all Brainarr settings)
cp /var/lib/lidarr/.config/Lidarr/config.xml ~/brainarr-config-backup-$(date +%Y%m%d).xml

# Or just the import list settings
grep -A 20 -B 5 "Brainarr" /var/lib/lidarr/.config/Lidarr/config.xml > ~/brainarr-settings-backup.xml
```

### **Migration Between Environments** 
```bash
# Development → Production
# 1. Test configuration in dev environment
# 2. Export working settings
# 3. Import to production Lidarr
# 4. Verify with connection test
# 5. Run small batch test before full automation
```

### **Version-Specific Settings**

**Brainarr v1.0.3 Settings:**
- All 9 providers fully supported
- Enhanced JSON parsing for all cloud providers  
- Improved deduplication and caching
- Advanced health monitoring
- Security improvements

---

## 🚀 **Integration Patterns**

### **Large-Scale Deployment**

**For Libraries > 5000 Albums:**
```
Max Recommendations: 50         // Higher batch size
Discovery Mode: Adjacent        // Balanced exploration
Sampling Strategy: Comprehensive // Deep analysis
Provider: Claude 3.5 Sonnet    // Best reasoning for complex libraries
Cache Duration: 240 minutes    // Longer caching
Refresh Interval: 48-72 hours  // Less frequent updates
```

### **Multi-User Environments**

**Docker Compose for Multiple Users:**
```yaml
version: "3.8"
services:
  lidarr-user1:
    image: ghcr.io/hotio/lidarr:pr-plugins-2.13.3.4692
    environment:
      - BRAINARR_PROVIDER=DeepSeek
    volumes:
      - ./user1-config:/config
      
  lidarr-user2:  
    image: ghcr.io/hotio/lidarr:pr-plugins-2.13.3.4692
    environment:
      - BRAINARR_PROVIDER=Gemini  
    volumes:
      - ./user2-config:/config
```

### **Development Environment**

**Testing Configuration:**
```
Provider: Ollama (local testing)
Max Recommendations: 5          // Small batches for speed
Discovery Mode: Similar         // Predictable results  
Cache Duration: 1 minute       // No caching during development
Debug Logging: Enabled         // Full verbosity
Strict Validation: Disabled    // See all AI outputs
```

---

## 🔍 **Debugging Advanced Issues**

### **Enable Verbose Logging**

**Temporary Debug Mode:**
```csharp
// In Lidarr: Settings → General → Logging
LogLevel = Debug;              // Enable detailed logging

// Brainarr-specific debug info:
LogRequest = true;             // Log AI requests (sanitized)
LogResponse = true;            // Log AI responses  
LogCacheOperations = true;     // Cache hit/miss tracking
LogHealthChecks = true;        // Provider health details
```

### **Provider-Specific Debug**

**Local Provider Debugging:**
```bash
# Test Ollama directly
curl http://localhost:11434/api/generate -d '{
  "model": "qwen2.5:latest",
  "prompt": "Recommend 3 jazz albums as JSON array",
  "stream": false
}'

# Check LM Studio logs
# LM Studio → Settings → Logs (enable request logging)
```

**Cloud Provider Debugging:**
```bash
# Test with curl to isolate issues
curl -X POST https://api.openai.com/v1/chat/completions \
  -H "Authorization: Bearer $OPENAI_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gpt-4o-mini",
    "messages": [{"role": "user", "content": "Test message"}],
    "max_tokens": 100
  }'
```

### **Performance Profiling**

**Memory Profiling:**
```bash
# Monitor Lidarr memory usage during recommendation generation
while true; do
  echo "$(date): $(ps -o rss= -p $(pgrep Lidarr) | awk '{print $1/1024 "MB"}')"
  sleep 10
done
```

**Response Time Analysis:**
```bash
# Extract response times from logs
grep "Response time:" /var/lib/lidarr/.config/Lidarr/logs/lidarr.txt | \
awk '{print $5}' | sed 's/ms//' | \
awk '{sum+=$1; count++} END {print "Average: " sum/count "ms"}'
```

---

## 🎛️ **Expert Customization**

### **Custom Provider Configuration**

**Environment Variable Override:**
```bash
# Override default settings via environment
export BRAINARR_MAX_RECOMMENDATIONS=30
export BRAINARR_CACHE_DURATION_MINUTES=90
export BRAINARR_RATE_LIMIT_PER_MINUTE=15
```

### **Advanced Retry Configuration**

**Exponential Backoff Tuning (from RetryPolicy.cs):**
```csharp
MaxRetryAttempts = 3;          // Maximum retries
InitialRetryDelayMs = 1000;    // 1 second initial delay
MaxRetryDelayMs = 30000;       // 30 second maximum delay
BackoffMultiplier = 2.0;       // Double delay each retry
```

**Optimization:**
```
Fast Providers (Groq, Gemini):
- InitialDelay: 500ms
- MaxRetries: 2
- MaxDelay: 10s

Slow Providers (Local, Premium):  
- InitialDelay: 2000ms
- MaxRetries: 5
- MaxDelay: 60s
```

### **Custom Health Check Configuration**

**Health Monitoring Tuning:**
```csharp
UnhealthyThreshold = 0.5;       // 50% failure rate = unhealthy
HealthCheckWindowMinutes = 5;   // Analysis window
ConsecutiveFailureLimit = 5;    // Circuit breaker threshold
```

---

## 📈 **Scaling Recommendations**

### **High-Volume Optimization**

**For Enterprise/High-Usage Scenarios:**
```
Multiple Provider Setup:
- Primary: DeepSeek (cost-effective)  
- Secondary: Gemini (speed)
- Tertiary: Ollama (offline backup)

Performance Settings:
- Max Recommendations: 100
- Batch Processing: Multiple smaller requests  
- Cache Duration: 180 minutes
- Health Check: Every minute
```

### **Cost Optimization**

**Token Usage Optimization:**
```
Sampling Strategy: Minimal     // Reduce input tokens
Max Tokens: 1500              // Limit response length
Cache Duration: 120+ minutes   // Reduce API calls
Discovery Mode: Similar        // More predictable output
```

---

**Ready for Production?** Your advanced configuration is complete! Monitor performance and adjust based on actual usage patterns.
