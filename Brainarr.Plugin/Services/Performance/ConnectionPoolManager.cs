using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Performance
{
    /// <summary>
    /// Manages HTTP connection pools for optimal performance and resource utilization
    /// </summary>
    public class ConnectionPoolManager : IDisposable
    {
        private readonly ConcurrentDictionary<string, HttpClient> _httpClients;
        private readonly ConcurrentDictionary<string, ConnectionPoolMetrics> _metrics;
        private readonly ILogger _logger;
        private readonly Timer _cleanupTimer;
        private readonly object _disposeLock = new object();
        private bool _disposed;

        public ConnectionPoolManager(ILogger logger)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
            _httpClients = new ConcurrentDictionary<string, HttpClient>();
            _metrics = new ConcurrentDictionary<string, ConnectionPoolMetrics>();
            
            // Cleanup idle connections every 5 minutes
            _cleanupTimer = new Timer(
                _ => CleanupIdleConnections(),
                null,
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// Get or create an optimized HTTP client for a specific endpoint
        /// </summary>
        public HttpClient GetClient(string endpoint, TimeSpan? timeout = null)
        {
            var key = GetPoolKey(endpoint);
            
            return _httpClients.GetOrAdd(key, k =>
            {
                var handler = CreateOptimizedHandler();
                var client = new HttpClient(handler, disposeHandler: false)
                {
                    Timeout = timeout ?? TimeSpan.FromSeconds(30)
                };
                
                ConfigureClient(client, endpoint);
                _logger.Debug($"Created new HTTP client for pool: {k}");
                
                return client;
            });
        }

        /// <summary>
        /// Execute HTTP request with automatic retry and circuit breaker
        /// </summary>
        public async Task<HttpResponseMessage> ExecuteWithRetryAsync(
            HttpClient client,
            Func<HttpClient, Task<HttpResponseMessage>> operation,
            int maxRetries = 3,
            CancellationToken cancellationToken = default)
        {
            var endpoint = client.BaseAddress?.Host ?? "unknown";
            var metrics = _metrics.GetOrAdd(endpoint, _ => new ConnectionPoolMetrics());

            // Check circuit breaker
            if (metrics.IsCircuitOpen)
            {
                if (DateTime.UtcNow - metrics.CircuitOpenTime > TimeSpan.FromMinutes(1))
                {
                    metrics.ResetCircuit();
                    _logger.Info($"Circuit breaker reset for {endpoint}");
                }
                else
                {
                    throw new InvalidOperationException($"Circuit breaker is open for {endpoint}");
                }
            }

            Exception lastException = null;
            
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    metrics.RecordAttempt();
                    
                    var response = await operation(client).ConfigureAwait(false);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        metrics.RecordSuccess();
                        return response;
                    }

                    // Don't retry client errors (4xx)
                    if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
                    {
                        metrics.RecordFailure();
                        return response;
                    }

                    lastException = new HttpRequestException($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
                catch (TaskCanceledException ex)
                {
                    metrics.RecordTimeout();
                    lastException = ex;
                    
                    if (cancellationToken.IsCancellationRequested)
                        throw;
                }
                catch (HttpRequestException ex)
                {
                    metrics.RecordFailure();
                    lastException = ex;
                }
                catch (Exception ex)
                {
                    metrics.RecordFailure();
                    _logger.Error(ex, $"Unexpected error in HTTP request to {endpoint}");
                    throw;
                }

                // Calculate backoff delay
                if (attempt < maxRetries - 1)
                {
                    var delay = CalculateBackoffDelay(attempt, metrics);
                    _logger.Debug($"Retrying request to {endpoint} after {delay.TotalSeconds}s (attempt {attempt + 1}/{maxRetries})");
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }

            // Open circuit breaker if too many failures
            if (metrics.ShouldOpenCircuit())
            {
                metrics.OpenCircuit();
                _logger.Warn($"Circuit breaker opened for {endpoint} due to repeated failures");
            }

            throw new HttpRequestException($"Request failed after {maxRetries} attempts", lastException);
        }

        private HttpMessageHandler CreateOptimizedHandler()
        {
            var handler = new SocketsHttpHandler
            {
                // Connection pooling settings
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                MaxConnectionsPerServer = 10,
                
                // Performance optimizations
                EnableMultipleHttp2Connections = true,
                MaxResponseHeadersLength = 64 * 1024, // 64KB
                MaxResponseDrainSize = 1024 * 1024, // 1MB
                ResponseDrainTimeout = TimeSpan.FromSeconds(5),
                
                // Security settings
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                },
                
                // Automatic decompression
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                
                // Cookie handling
                UseCookies = false,
                
                // Proxy settings
                UseProxy = true,
                Proxy = WebRequest.GetSystemWebProxy()
            };

            // Configure keep-alive
            handler.ConnectTimeout = TimeSpan.FromSeconds(10);
            handler.Expect100ContinueTimeout = TimeSpan.FromSeconds(1);

            return handler;
        }

        private void ConfigureClient(HttpClient client, string endpoint)
        {
            // Set default headers for performance
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("User-Agent", "Brainarr/1.0.0");
            
            // Set base address if endpoint is provided
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                client.BaseAddress = new Uri($"{uri.Scheme}://{uri.Host}:{uri.Port}");
            }
        }

        private string GetPoolKey(string endpoint)
        {
            if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
            }
            
            return endpoint;
        }

        private TimeSpan CalculateBackoffDelay(int attempt, ConnectionPoolMetrics metrics)
        {
            // Exponential backoff with jitter
            var baseDelay = Math.Pow(2, attempt);
            var jitter = new Random().NextDouble() * 0.3; // 0-30% jitter
            
            // Adjust based on metrics
            var adjustmentFactor = 1.0;
            if (metrics.TimeoutRate > 0.5)
                adjustmentFactor = 1.5; // Increase delay for high timeout rate
            
            var delaySeconds = baseDelay * (1 + jitter) * adjustmentFactor;
            return TimeSpan.FromSeconds(Math.Min(delaySeconds, 30)); // Max 30 seconds
        }

        private void CleanupIdleConnections()
        {
            try
            {
                foreach (var kvp in _metrics)
                {
                    var metrics = kvp.Value;
                    
                    // Remove metrics for endpoints that haven't been used recently
                    if (DateTime.UtcNow - metrics.LastUsed > TimeSpan.FromHours(1))
                    {
                        if (_metrics.TryRemove(kvp.Key, out _))
                        {
                            _logger.Debug($"Removed idle metrics for {kvp.Key}");
                        }
                    }
                }

                // Force garbage collection of idle connections
                GC.Collect(1, GCCollectionMode.Optimized);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during connection pool cleanup");
            }
        }

        public ConnectionPoolStatistics GetStatistics()
        {
            var stats = new ConnectionPoolStatistics();
            
            foreach (var kvp in _metrics)
            {
                var metrics = kvp.Value;
                stats.TotalRequests += metrics.TotalAttempts;
                stats.SuccessfulRequests += metrics.SuccessCount;
                stats.FailedRequests += metrics.FailureCount;
                stats.TimeoutRequests += metrics.TimeoutCount;
                
                if (metrics.IsCircuitOpen)
                    stats.OpenCircuits++;
            }
            
            stats.ActivePools = _httpClients.Count;
            return stats;
        }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed)
                    return;

                _cleanupTimer?.Dispose();
                
                foreach (var client in _httpClients.Values)
                {
                    client?.Dispose();
                }
                
                _httpClients.Clear();
                _metrics.Clear();
                
                _disposed = true;
            }
        }

        private class ConnectionPoolMetrics
        {
            private long _totalAttempts;
            private long _successCount;
            private long _failureCount;
            private long _timeoutCount;
            
            public DateTime LastUsed { get; private set; }
            public DateTime CircuitOpenTime { get; private set; }
            public bool IsCircuitOpen { get; private set; }
            
            public long TotalAttempts => Interlocked.Read(ref _totalAttempts);
            public long SuccessCount => Interlocked.Read(ref _successCount);
            public long FailureCount => Interlocked.Read(ref _failureCount);
            public long TimeoutCount => Interlocked.Read(ref _timeoutCount);
            
            public double SuccessRate => TotalAttempts > 0 ? (double)SuccessCount / TotalAttempts : 0;
            public double TimeoutRate => TotalAttempts > 0 ? (double)TimeoutCount / TotalAttempts : 0;
            public double FailureRate => TotalAttempts > 0 ? (double)FailureCount / TotalAttempts : 0;

            public void RecordAttempt()
            {
                Interlocked.Increment(ref _totalAttempts);
                LastUsed = DateTime.UtcNow;
            }

            public void RecordSuccess()
            {
                Interlocked.Increment(ref _successCount);
            }

            public void RecordFailure()
            {
                Interlocked.Increment(ref _failureCount);
            }

            public void RecordTimeout()
            {
                Interlocked.Increment(ref _timeoutCount);
            }

            public bool ShouldOpenCircuit()
            {
                // Open circuit if failure rate > 50% in last 10 attempts
                var recentAttempts = Math.Min(TotalAttempts, 10);
                if (recentAttempts < 5) return false; // Need minimum attempts
                
                var recentFailures = Math.Min(FailureCount, recentAttempts);
                return (double)recentFailures / recentAttempts > 0.5;
            }

            public void OpenCircuit()
            {
                IsCircuitOpen = true;
                CircuitOpenTime = DateTime.UtcNow;
            }

            public void ResetCircuit()
            {
                IsCircuitOpen = false;
                // Reset counters for fresh start
                Interlocked.Exchange(ref _failureCount, 0);
                Interlocked.Exchange(ref _timeoutCount, 0);
            }
        }

        public class ConnectionPoolStatistics
        {
            public int ActivePools { get; set; }
            public long TotalRequests { get; set; }
            public long SuccessfulRequests { get; set; }
            public long FailedRequests { get; set; }
            public long TimeoutRequests { get; set; }
            public int OpenCircuits { get; set; }
            
            public double OverallSuccessRate => TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 0;
        }
    }
}