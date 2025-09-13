using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.RateLimiting
{
    /// <summary>
    /// Enhanced rate limiter with per-user, per-IP, and distributed rate limiting support.
    /// </summary>
    public interface IEnhancedRateLimiter
    {
        Task<RateLimitResult> CheckRateLimitAsync(RateLimitRequest request);
        Task<T> ExecuteAsync<T>(RateLimitRequest request, Func<Task<T>> action);
        void ConfigureLimit(string resource, RateLimitPolicy policy);
        RateLimitStatistics GetStatistics(string resource = null);
        void Reset(string resource = null, string identifier = null);
    }

    public class EnhancedRateLimiter : IEnhancedRateLimiter, IDisposable
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, ResourceRateLimiter> _resourceLimiters;
        private readonly ConcurrentDictionary<string, UserRateLimiter> _userLimiters;
        private readonly ConcurrentDictionary<string, IpRateLimiter> _ipLimiters;
        private readonly ConcurrentDictionary<string, RateLimitPolicy> _policies;
        private readonly Timer _cleanupTimer;
        private readonly RateLimitMetrics _metrics;

        public EnhancedRateLimiter(Logger logger)
        {
            _logger = logger;
            _resourceLimiters = new ConcurrentDictionary<string, ResourceRateLimiter>();
            _userLimiters = new ConcurrentDictionary<string, UserRateLimiter>();
            _ipLimiters = new ConcurrentDictionary<string, IpRateLimiter>();
            _policies = new ConcurrentDictionary<string, RateLimitPolicy>();
            _metrics = new RateLimitMetrics();
            
            // Start cleanup timer for expired entries
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, 
                TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            
            // Configure default policies
            ConfigureDefaultPolicies();
        }

        /// <summary>
        /// Checks if a request would be rate limited without consuming a token.
        /// </summary>
        public async Task<RateLimitResult> CheckRateLimitAsync(RateLimitRequest request)
        {
            ValidateRequest(request);
            
            var policy = GetPolicy(request.Resource);
            var results = new List<RateLimitResult>();
            
            // Check resource-level limits
            if (policy.EnableResourceLimit)
            {
                var resourceLimiter = GetOrCreateResourceLimiter(request.Resource, policy);
                results.Add(await resourceLimiter.CheckAsync(request));
            }
            
            // Check user-level limits
            if (policy.EnableUserLimit && !string.IsNullOrEmpty(request.UserId))
            {
                var userLimiter = GetOrCreateUserLimiter(request.UserId, policy);
                results.Add(await userLimiter.CheckAsync(request));
            }
            
            // Check IP-level limits
            if (policy.EnableIpLimit && request.IpAddress != null)
            {
                var ipLimiter = GetOrCreateIpLimiter(request.IpAddress, policy);
                results.Add(await ipLimiter.CheckAsync(request));
            }
            
            // Return the most restrictive result
            return CombineResults(results);
        }

        /// <summary>
        /// Executes an action with rate limiting applied.
        /// </summary>
        public async Task<T> ExecuteAsync<T>(RateLimitRequest request, Func<Task<T>> action)
        {
            ValidateRequest(request);
            
            var startTime = DateTime.UtcNow;
            var result = await CheckRateLimitAsync(request);
            
            if (!result.IsAllowed)
            {
                _metrics.RecordRejection(request.Resource, request.UserId, request.IpAddress?.ToString());
                
                if (result.RetryAfter.HasValue)
                {
                    _logger.Warn($"Rate limit exceeded for {request.Resource}. " +
                               $"Retry after {result.RetryAfter.Value.TotalSeconds:F1} seconds. " +
                               $"Reason: {result.Reason}");
                    
                    if (request.WaitForAvailability && result.RetryAfter.Value < TimeSpan.FromMinutes(1))
                    {
                        _logger.Debug($"Waiting {result.RetryAfter.Value.TotalMilliseconds:F0}ms for rate limit");
                        await Task.Delay(result.RetryAfter.Value);
                        
                        // Retry after waiting
                        return await ExecuteAsync(request, action);
                    }
                }
                
                throw new RateLimitExceededException(result);
            }
            
            // Consume tokens from all applicable limiters
            await ConsumeTokensAsync(request);
            
            try
            {
                var response = await action();
                var duration = DateTime.UtcNow - startTime;
                _metrics.RecordSuccess(request.Resource, duration);
                return response;
            }
            catch (Exception ex)
            {
                _metrics.RecordFailure(request.Resource);
                throw;
            }
        }

        /// <summary>
        /// Configures a rate limit policy for a resource.
        /// </summary>
        public void ConfigureLimit(string resource, RateLimitPolicy policy)
        {
            if (string.IsNullOrWhiteSpace(resource))
                throw new ArgumentException("Resource name is required", nameof(resource));
            
            if (policy == null)
                throw new ArgumentNullException(nameof(policy));
            
            _policies[resource] = policy;
            _logger.Info($"Configured rate limit for {resource}: " +
                        $"{policy.MaxRequests} requests per {policy.Period.TotalSeconds}s");
        }

        /// <summary>
        /// Gets statistics for rate limiting.
        /// </summary>
        public RateLimitStatistics GetStatistics(string resource = null)
        {
            var stats = new RateLimitStatistics
            {
                TotalRequests = _metrics.TotalRequests,
                RejectedRequests = _metrics.RejectedRequests,
                AverageResponseTime = _metrics.GetAverageResponseTime()
            };
            
            if (resource != null)
            {
                stats.ResourceStatistics = GetResourceStatistics(resource);
            }
            else
            {
                stats.AllResourceStatistics = _resourceLimiters
                    .ToDictionary(kvp => kvp.Key, kvp => GetResourceStatistics(kvp.Key));
            }
            
            stats.TopRejectedUsers = _metrics.GetTopRejectedUsers(10);
            stats.TopRejectedIps = _metrics.GetTopRejectedIps(10);
            
            return stats;
        }

        /// <summary>
        /// Resets rate limit counters.
        /// </summary>
        public void Reset(string resource = null, string identifier = null)
        {
            if (resource != null)
            {
                if (_resourceLimiters.TryGetValue(resource, out var limiter))
                {
                    limiter.Reset(identifier);
                    _logger.Info($"Reset rate limiter for resource: {resource}");
                }
            }
            else
            {
                // Reset all limiters
                foreach (var limiter in _resourceLimiters.Values)
                {
                    limiter.Reset();
                }
                
                _userLimiters.Clear();
                _ipLimiters.Clear();
                _metrics.Reset();
                
                _logger.Info("Reset all rate limiters");
            }
        }

        private void ValidateRequest(RateLimitRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            
            if (string.IsNullOrWhiteSpace(request.Resource))
                throw new ArgumentException("Resource is required", nameof(request));
        }

        private RateLimitPolicy GetPolicy(string resource)
        {
            if (_policies.TryGetValue(resource, out var policy))
                return policy;
            
            // Return default policy if not configured
            return RateLimitPolicy.Default;
        }

        private ResourceRateLimiter GetOrCreateResourceLimiter(string resource, RateLimitPolicy policy)
        {
            return _resourceLimiters.GetOrAdd(resource, 
                r => new ResourceRateLimiter(r, policy, _logger));
        }

        private UserRateLimiter GetOrCreateUserLimiter(string userId, RateLimitPolicy policy)
        {
            return _userLimiters.GetOrAdd(userId, 
                u => new UserRateLimiter(u, policy, _logger));
        }

        private IpRateLimiter GetOrCreateIpLimiter(IPAddress ipAddress, RateLimitPolicy policy)
        {
            var key = ipAddress.ToString();
            return _ipLimiters.GetOrAdd(key, 
                ip => new IpRateLimiter(ipAddress, policy, _logger));
        }

        private async Task ConsumeTokensAsync(RateLimitRequest request)
        {
            var policy = GetPolicy(request.Resource);
            var tasks = new List<Task>();
            
            if (policy.EnableResourceLimit)
            {
                var limiter = GetOrCreateResourceLimiter(request.Resource, policy);
                tasks.Add(limiter.ConsumeAsync(request));
            }
            
            if (policy.EnableUserLimit && !string.IsNullOrEmpty(request.UserId))
            {
                var limiter = GetOrCreateUserLimiter(request.UserId, policy);
                tasks.Add(limiter.ConsumeAsync(request));
            }
            
            if (policy.EnableIpLimit && request.IpAddress != null)
            {
                var limiter = GetOrCreateIpLimiter(request.IpAddress, policy);
                tasks.Add(limiter.ConsumeAsync(request));
            }
            
            await Task.WhenAll(tasks);
        }

        private RateLimitResult CombineResults(List<RateLimitResult> results)
        {
            if (!results.Any())
                return RateLimitResult.Allowed();
            
            // Find the most restrictive result
            var deniedResult = results.FirstOrDefault(r => !r.IsAllowed);
            if (deniedResult != null)
                return deniedResult;
            
            // All are allowed, return with minimum remaining tokens
            var minRemaining = results.Min(r => r.RemainingTokens);
            var maxRetryAfter = results.Max(r => r.RetryAfter);
            
            return new RateLimitResult
            {
                IsAllowed = true,
                RemainingTokens = minRemaining,
                RetryAfter = maxRetryAfter,
                ResetsAt = results.Max(r => r.ResetsAt)
            };
        }

        private ResourceStatistics GetResourceStatistics(string resource)
        {
            if (!_resourceLimiters.TryGetValue(resource, out var limiter))
                return new ResourceStatistics { Resource = resource };
            
            return limiter.GetStatistics();
        }

        private void ConfigureDefaultPolicies()
        {
            // Local AI providers - more lenient
            ConfigureLimit("ollama", RateLimitPolicy.LocalAI);
            ConfigureLimit("lmstudio", RateLimitPolicy.LocalAI);
            
            // Cloud AI providers - standard limits
            ConfigureLimit("openai", RateLimitPolicy.CloudAI);
            ConfigureLimit("anthropic", RateLimitPolicy.CloudAI);
            ConfigureLimit("gemini", RateLimitPolicy.CloudAI);
            ConfigureLimit("groq", RateLimitPolicy.CloudAI);
            
            // Music APIs - strict limits
            ConfigureLimit("musicbrainz", RateLimitPolicy.MusicAPI);
            ConfigureLimit("spotify", RateLimitPolicy.MusicAPI);
            
            // Admin operations - very lenient
            ConfigureLimit("admin", RateLimitPolicy.Admin);
        }

        private void CleanupExpiredEntries(object state)
        {
            try
            {
                var cutoff = DateTime.UtcNow - TimeSpan.FromHours(1);
                
                // Cleanup user limiters
                var expiredUsers = _userLimiters
                    .Where(kvp => kvp.Value.LastActivity < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var user in expiredUsers)
                {
                    _userLimiters.TryRemove(user, out _);
                }
                
                // Cleanup IP limiters
                var expiredIps = _ipLimiters
                    .Where(kvp => kvp.Value.LastActivity < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();
                
                foreach (var ip in expiredIps)
                {
                    _ipLimiters.TryRemove(ip, out _);
                }
                
                if (expiredUsers.Any() || expiredIps.Any())
                {
                    _logger.Debug($"Cleaned up {expiredUsers.Count} user limiters and {expiredIps.Count} IP limiters");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during rate limiter cleanup");
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }

        // Base limiter class
        private abstract class BaseLimiter
        {
            protected readonly string Identifier;
            protected readonly RateLimitPolicy Policy;
            protected readonly Logger Logger;
            protected readonly TokenBucket Bucket;
            protected DateTime _lastActivity;
            
            protected BaseLimiter(string identifier, RateLimitPolicy policy, Logger logger)
            {
                Identifier = identifier;
                Policy = policy;
                Logger = logger;
                Bucket = new TokenBucket(policy.MaxRequests, policy.Period);
                _lastActivity = DateTime.UtcNow;
            }
            
            public DateTime LastActivity => _lastActivity;
            
            public Task<RateLimitResult> CheckAsync(RateLimitRequest request)
            {
                _lastActivity = DateTime.UtcNow;
                var available = Bucket.GetAvailableTokens();
                var tokensNeeded = request.Weight ?? 1;
                
                if (available >= tokensNeeded)
                {
                    return Task.FromResult(new RateLimitResult
                    {
                        IsAllowed = true,
                        RemainingTokens = available - tokensNeeded,
                        ResetsAt = Bucket.GetNextResetTime()
                    });
                }
                
                return Task.FromResult(new RateLimitResult
                {
                    IsAllowed = false,
                    RemainingTokens = 0,
                    RetryAfter = Bucket.GetWaitTime(tokensNeeded),
                    ResetsAt = Bucket.GetNextResetTime(),
                    Reason = $"Rate limit exceeded for {GetLimiterType()}: {Identifier}"
                });
            }
            
            public Task ConsumeAsync(RateLimitRequest request)
            {
                _lastActivity = DateTime.UtcNow;
                var tokensNeeded = request.Weight ?? 1;
                Bucket.TryConsume(tokensNeeded);
                return Task.CompletedTask;
            }
            
            public void Reset(string identifier = null)
            {
                if (identifier == null || identifier == Identifier)
                {
                    Bucket.Reset();
                }
            }
            
            public ResourceStatistics GetStatistics()
            {
                return new ResourceStatistics
                {
                    Resource = Identifier,
                    AvailableTokens = Bucket.GetAvailableTokens(),
                    MaxTokens = Policy.MaxRequests,
                    ResetsAt = Bucket.GetNextResetTime(),
                    LastActivity = _lastActivity
                };
            }
            
            protected abstract string GetLimiterType();
        }
        
        private class ResourceRateLimiter : BaseLimiter
        {
            public ResourceRateLimiter(string resource, RateLimitPolicy policy, Logger logger) 
                : base(resource, policy, logger) { }
            
            protected override string GetLimiterType() => "resource";
        }
        
        private class UserRateLimiter : BaseLimiter
        {
            public UserRateLimiter(string userId, RateLimitPolicy policy, Logger logger) 
                : base(userId, policy, logger) { }
            
            protected override string GetLimiterType() => "user";
        }
        
        private class IpRateLimiter : BaseLimiter
        {
            private readonly IPAddress _ipAddress;
            
            public IpRateLimiter(IPAddress ipAddress, RateLimitPolicy policy, Logger logger) 
                : base(ipAddress.ToString(), policy, logger) 
            {
                _ipAddress = ipAddress;
            }
            
            protected override string GetLimiterType() => "IP";
        }
    }

    /// <summary>
    /// Token bucket algorithm implementation for rate limiting.
    /// </summary>
    public class TokenBucket
    {
        private readonly object _lock = new();
        private readonly int _capacity;
        private readonly TimeSpan _refillPeriod;
        private double _tokens;
        private DateTime _lastRefill;
        
        public TokenBucket(int capacity, TimeSpan refillPeriod)
        {
            _capacity = capacity;
            _refillPeriod = refillPeriod;
            _tokens = capacity;
            _lastRefill = DateTime.UtcNow;
        }
        
        public int GetAvailableTokens()
        {
            lock (_lock)
            {
                RefillTokens();
                return (int)_tokens;
            }
        }
        
        public bool TryConsume(int count = 1)
        {
            lock (_lock)
            {
                RefillTokens();
                
                if (_tokens >= count)
                {
                    _tokens -= count;
                    return true;
                }
                
                return false;
            }
        }
        
        public TimeSpan GetWaitTime(int tokensNeeded)
        {
            lock (_lock)
            {
                RefillTokens();
                
                if (_tokens >= tokensNeeded)
                    return TimeSpan.Zero;
                
                var tokensShort = tokensNeeded - _tokens;
                var refillRate = (double)_capacity / _refillPeriod.TotalMilliseconds;
                var waitMs = tokensShort / refillRate;
                
                return TimeSpan.FromMilliseconds(Math.Max(0, waitMs));
            }
        }
        
        public DateTime GetNextResetTime()
        {
            lock (_lock)
            {
                return _lastRefill.Add(_refillPeriod);
            }
        }
        
        public void Reset()
        {
            lock (_lock)
            {
                _tokens = _capacity;
                _lastRefill = DateTime.UtcNow;
            }
        }
        
        private void RefillTokens()
        {
            var now = DateTime.UtcNow;
            var timePassed = now - _lastRefill;
            
            if (timePassed >= _refillPeriod)
            {
                // Full refill
                _tokens = _capacity;
                _lastRefill = now;
            }
            else
            {
                // Partial refill
                var refillRate = (double)_capacity / _refillPeriod.TotalMilliseconds;
                var tokensToAdd = refillRate * timePassed.TotalMilliseconds;
                _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
                _lastRefill = now;
            }
        }
    }

    public class RateLimitRequest
    {
        public string Resource { get; set; }
        public string UserId { get; set; }
        public IPAddress IpAddress { get; set; }
        public int? Weight { get; set; } = 1; // Cost of the request in tokens
        public bool WaitForAvailability { get; set; } = false;
        public Dictionary<string, object> Metadata { get; set; }
    }

    public class RateLimitResult
    {
        public bool IsAllowed { get; set; }
        public int RemainingTokens { get; set; }
        public TimeSpan? RetryAfter { get; set; }
        public DateTime? ResetsAt { get; set; }
        public string Reason { get; set; }
        
        public static RateLimitResult Allowed(int remaining = int.MaxValue)
        {
            return new RateLimitResult 
            { 
                IsAllowed = true, 
                RemainingTokens = remaining 
            };
        }
        
        public static RateLimitResult Denied(string reason, TimeSpan? retryAfter = null)
        {
            return new RateLimitResult 
            { 
                IsAllowed = false, 
                RemainingTokens = 0,
                Reason = reason,
                RetryAfter = retryAfter
            };
        }
    }

    public class RateLimitPolicy
    {
        public int MaxRequests { get; set; }
        public TimeSpan Period { get; set; }
        public bool EnableResourceLimit { get; set; } = true;
        public bool EnableUserLimit { get; set; } = true;
        public bool EnableIpLimit { get; set; } = true;
        public int? BurstSize { get; set; }
        
        public static RateLimitPolicy Default => new()
        {
            MaxRequests = 60,
            Period = TimeSpan.FromMinutes(1)
        };
        
        public static RateLimitPolicy LocalAI => new()
        {
            MaxRequests = 100,
            Period = TimeSpan.FromMinutes(1),
            EnableIpLimit = false // Local, no need for IP limiting
        };
        
        public static RateLimitPolicy CloudAI => new()
        {
            MaxRequests = 20,
            Period = TimeSpan.FromMinutes(1),
            BurstSize = 5
        };
        
        public static RateLimitPolicy MusicAPI => new()
        {
            MaxRequests = 60,
            Period = TimeSpan.FromSeconds(60),
            BurstSize = 2
        };
        
        public static RateLimitPolicy Admin => new()
        {
            MaxRequests = 1000,
            Period = TimeSpan.FromMinutes(1),
            EnableIpLimit = false
        };
    }

    public class RateLimitStatistics
    {
        public long TotalRequests { get; set; }
        public long RejectedRequests { get; set; }
        public double AverageResponseTime { get; set; }
        public ResourceStatistics ResourceStatistics { get; set; }
        public Dictionary<string, ResourceStatistics> AllResourceStatistics { get; set; }
        public List<KeyValuePair<string, int>> TopRejectedUsers { get; set; }
        public List<KeyValuePair<string, int>> TopRejectedIps { get; set; }
    }

    public class ResourceStatistics
    {
        public string Resource { get; set; }
        public int AvailableTokens { get; set; }
        public int MaxTokens { get; set; }
        public DateTime? ResetsAt { get; set; }
        public DateTime LastActivity { get; set; }
    }

    public class RateLimitMetrics
    {
        private long _totalRequests;
        private long _rejectedRequests;
        private readonly ConcurrentBag<double> _responseTimes = new();
        private readonly ConcurrentDictionary<string, int> _rejectedUsers = new();
        private readonly ConcurrentDictionary<string, int> _rejectedIps = new();
        
        public long TotalRequests => _totalRequests;
        public long RejectedRequests => _rejectedRequests;
        
        public void RecordSuccess(string resource, TimeSpan duration)
        {
            Interlocked.Increment(ref _totalRequests);
            _responseTimes.Add(duration.TotalMilliseconds);
            
            // Keep only last 1000 response times
            while (_responseTimes.Count > 1000)
            {
                _responseTimes.TryTake(out _);
            }
        }
        
        public void RecordFailure(string resource)
        {
            Interlocked.Increment(ref _totalRequests);
        }
        
        public void RecordRejection(string resource, string userId, string ipAddress)
        {
            Interlocked.Increment(ref _rejectedRequests);
            
            if (!string.IsNullOrEmpty(userId))
            {
                _rejectedUsers.AddOrUpdate(userId, 1, (_, count) => count + 1);
            }
            
            if (!string.IsNullOrEmpty(ipAddress))
            {
                _rejectedIps.AddOrUpdate(ipAddress, 1, (_, count) => count + 1);
            }
        }
        
        public double GetAverageResponseTime()
        {
            if (_responseTimes.IsEmpty) return 0;
            return _responseTimes.Average();
        }
        
        public List<KeyValuePair<string, int>> GetTopRejectedUsers(int count)
        {
            return _rejectedUsers
                .OrderByDescending(kvp => kvp.Value)
                .Take(count)
                .ToList();
        }
        
        public List<KeyValuePair<string, int>> GetTopRejectedIps(int count)
        {
            return _rejectedIps
                .OrderByDescending(kvp => kvp.Value)
                .Take(count)
                .ToList();
        }
        
        public void Reset()
        {
            _totalRequests = 0;
            _rejectedRequests = 0;
            _responseTimes.Clear();
            _rejectedUsers.Clear();
            _rejectedIps.Clear();
        }
    }

    public class RateLimitExceededException : Exception
    {
        public RateLimitResult Result { get; }
        
        public RateLimitExceededException(RateLimitResult result) 
            : base($"Rate limit exceeded. {result.Reason}")
        {
            Result = result;
        }
    }
}