using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Network
{
    /// <summary>
    /// Manages HTTP client connections with pooling for improved performance and resource management.
    /// </summary>
    public interface IHttpConnectionPool : IDisposable
    {
        HttpClient GetClient(string baseUrl, TimeSpan? timeout = null);
        Task<T> ExecuteWithClientAsync<T>(string baseUrl, Func<HttpClient, Task<T>> operation, CancellationToken cancellationToken = default);
        ConnectionPoolStatistics GetStatistics();
        void ClearPool();
    }

    public class HttpConnectionPool : IHttpConnectionPool
    {
        private readonly Logger _logger;
        private readonly ConcurrentDictionary<string, PooledHttpClient> _clientPool;
        private readonly HttpConnectionPoolOptions _options;
        private readonly Timer _cleanupTimer;
        private readonly SemaphoreSlim _poolSemaphore;
        private readonly ConnectionPoolMetrics _metrics;
        private volatile bool _disposed;

        public HttpConnectionPool(Logger logger, HttpConnectionPoolOptions options = null)
        {
            _logger = logger;
            _options = options ?? HttpConnectionPoolOptions.Default;
            _clientPool = new ConcurrentDictionary<string, PooledHttpClient>();
            _poolSemaphore = new SemaphoreSlim(_options.MaxConnectionsPerHost);
            _metrics = new ConnectionPoolMetrics();
            
            // Start cleanup timer
            _cleanupTimer = new Timer(CleanupStaleConnections, null, 
                _options.ConnectionIdleTimeout, _options.ConnectionIdleTimeout);
        }

        /// <summary>
        /// Gets or creates a pooled HTTP client for the specified base URL.
        /// </summary>
        public HttpClient GetClient(string baseUrl, TimeSpan? timeout = null)
        {
            ThrowIfDisposed();
            
            var normalizedUrl = NormalizeUrl(baseUrl);
            var poolKey = GeneratePoolKey(normalizedUrl, timeout);
            
            return _clientPool.GetOrAdd(poolKey, key =>
            {
                _logger.Debug($"Creating new HTTP client for pool key: {key}");
                return CreatePooledClient(normalizedUrl, timeout);
            }).Client;
        }

        /// <summary>
        /// Executes an operation with a pooled client, ensuring proper resource management.
        /// </summary>
        public async Task<T> ExecuteWithClientAsync<T>(
            string baseUrl, 
            Func<HttpClient, Task<T>> operation, 
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            await _poolSemaphore.WaitAsync(cancellationToken);
            try
            {
                var client = GetClient(baseUrl);
                var startTime = DateTime.UtcNow;
                
                try
                {
                    var result = await operation(client).ConfigureAwait(false);
                    RecordSuccess(baseUrl, DateTime.UtcNow - startTime);
                    return result;
                }
                catch (Exception ex)
                {
                    RecordFailure(baseUrl, DateTime.UtcNow - startTime, ex);
                    throw;
                }
            }
            finally
            {
                _poolSemaphore.Release();
            }
        }

        /// <summary>
        /// Creates a new pooled HTTP client with optimized settings.
        /// </summary>
        private PooledHttpClient CreatePooledClient(string baseUrl, TimeSpan? timeout)
        {
            var handler = new SocketsHttpHandler
            {
                // Connection pooling settings
                PooledConnectionLifetime = _options.ConnectionLifetime,
                PooledConnectionIdleTimeout = _options.ConnectionIdleTimeout,
                MaxConnectionsPerServer = _options.MaxConnectionsPerHost,
                
                // Performance optimizations
                EnableMultipleHttp2Connections = true,
                MaxResponseHeadersLength = 64 * 1024, // 64KB
                ResponseDrainTimeout = TimeSpan.FromSeconds(10),
                
                // Security settings
                AllowAutoRedirect = false, // Handle redirects explicitly
                UseCookies = false, // Stateless connections
                UseProxy = _options.UseProxy,
                
                // Connection settings
                ConnectTimeout = _options.ConnectTimeout,
                Expect100ContinueTimeout = TimeSpan.Zero, // Disable Expect: 100-continue
            };

            // Certificate validation (production should validate properly)
            if (_options.ValidateCertificates)
            {
                handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                {
                    if (errors == System.Net.Security.SslPolicyErrors.None)
                        return true;
                    
                    _logger.Warn($"Certificate validation failed for {baseUrl}: {errors}");
                    return _options.AllowInvalidCertificates;
                };
            }

            var client = new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = timeout ?? _options.DefaultTimeout
            };

            // Set default headers
            client.DefaultRequestHeaders.Add("User-Agent", $"Brainarr/{GetVersion()}");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            client.DefaultRequestHeaders.ConnectionClose = false; // Keep-alive
            
            // HTTP/2 support
            client.DefaultRequestVersion = new Version(2, 0);
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;

            return new PooledHttpClient
            {
                Client = client,
                Handler = handler,
                BaseUrl = baseUrl,
                CreatedAt = DateTime.UtcNow,
                LastUsed = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Cleans up stale connections that haven't been used recently.
        /// </summary>
        private void CleanupStaleConnections(object state)
        {
            if (_disposed) return;
            
            var cutoffTime = DateTime.UtcNow - _options.ConnectionIdleTimeout;
            var keysToRemove = new List<string>();
            
            foreach (var kvp in _clientPool)
            {
                if (kvp.Value.LastUsed < cutoffTime)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }
            
            foreach (var key in keysToRemove)
            {
                if (_clientPool.TryRemove(key, out var pooledClient))
                {
                    _logger.Debug($"Removing stale HTTP client from pool: {key}");
                    pooledClient.Dispose();
                    Interlocked.Increment(ref _metrics.ConnectionsRemoved);
                }
            }
            
            if (keysToRemove.Count > 0)
            {
                _logger.Info($"Cleaned up {keysToRemove.Count} stale HTTP connections");
            }
        }

        /// <summary>
        /// Gets statistics about the connection pool.
        /// </summary>
        public ConnectionPoolStatistics GetStatistics()
        {
            return new ConnectionPoolStatistics
            {
                ActiveConnections = _clientPool.Count,
                TotalConnectionsCreated = _metrics.ConnectionsCreated,
                TotalConnectionsRemoved = _metrics.ConnectionsRemoved,
                SuccessfulRequests = _metrics.SuccessfulRequests,
                FailedRequests = _metrics.FailedRequests,
                AverageResponseTime = _metrics.GetAverageResponseTime(),
                ConnectionsByHost = _clientPool.GroupBy(kvp => kvp.Value.BaseUrl)
                    .ToDictionary(g => g.Key, g => g.Count())
            };
        }

        /// <summary>
        /// Clears all connections from the pool.
        /// </summary>
        public void ClearPool()
        {
            _logger.Info("Clearing HTTP connection pool");
            
            foreach (var kvp in _clientPool)
            {
                if (_clientPool.TryRemove(kvp.Key, out var pooledClient))
                {
                    pooledClient.Dispose();
                }
            }
            
            _metrics.Reset();
        }

        private void RecordSuccess(string baseUrl, TimeSpan duration)
        {
            Interlocked.Increment(ref _metrics.SuccessfulRequests);
            _metrics.RecordResponseTime(duration.TotalMilliseconds);
            
            // Update last used time
            var poolKey = GeneratePoolKey(NormalizeUrl(baseUrl), null);
            if (_clientPool.TryGetValue(poolKey, out var pooledClient))
            {
                pooledClient.LastUsed = DateTime.UtcNow;
            }
        }

        private void RecordFailure(string baseUrl, TimeSpan duration, Exception ex)
        {
            Interlocked.Increment(ref _metrics.FailedRequests);
            _logger.Warn($"HTTP request failed for {baseUrl}: {ex.Message}");
        }

        private string NormalizeUrl(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
            }
            return url;
        }

        private string GeneratePoolKey(string baseUrl, TimeSpan? timeout)
        {
            var timeoutKey = timeout?.TotalSeconds.ToString() ?? "default";
            return $"{baseUrl}_{timeoutKey}";
        }

        private string GetVersion()
        {
            return System.Reflection.Assembly.GetExecutingAssembly()
                .GetName().Version?.ToString() ?? "1.0.0";
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(HttpConnectionPool));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            _cleanupTimer?.Dispose();
            _poolSemaphore?.Dispose();
            ClearPool();
            
            _logger.Info("HTTP connection pool disposed");
        }

        private class PooledHttpClient : IDisposable
        {
            public HttpClient Client { get; set; }
            public SocketsHttpHandler Handler { get; set; }
            public string BaseUrl { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime LastUsed { get; set; }

            public void Dispose()
            {
                Client?.Dispose();
                Handler?.Dispose();
            }
        }

        private class ConnectionPoolMetrics
        {
            public int ConnectionsCreated;
            public int ConnectionsRemoved;
            public int SuccessfulRequests;
            public int FailedRequests;
            private readonly ConcurrentBag<double> _responseTimes = new();

            public void RecordResponseTime(double milliseconds)
            {
                _responseTimes.Add(milliseconds);
                
                // Keep only last 1000 response times to avoid memory issues
                while (_responseTimes.Count > 1000)
                {
                    _responseTimes.TryTake(out _);
                }
            }

            public double GetAverageResponseTime()
            {
                if (_responseTimes.IsEmpty) return 0;
                return _responseTimes.Average();
            }

            public void Reset()
            {
                ConnectionsCreated = 0;
                ConnectionsRemoved = 0;
                SuccessfulRequests = 0;
                FailedRequests = 0;
                _responseTimes.Clear();
            }
        }
    }

    public class HttpConnectionPoolOptions
    {
        public int MaxConnectionsPerHost { get; set; } = 10;
        public TimeSpan ConnectionLifetime { get; set; } = TimeSpan.FromMinutes(5);
        public TimeSpan ConnectionIdleTimeout { get; set; } = TimeSpan.FromMinutes(2);
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(60);
        public bool UseProxy { get; set; } = true;
        public bool ValidateCertificates { get; set; } = true;
        public bool AllowInvalidCertificates { get; set; } = false;

        public static HttpConnectionPoolOptions Default => new();
        
        public static HttpConnectionPoolOptions HighPerformance => new()
        {
            MaxConnectionsPerHost = 20,
            ConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectionIdleTimeout = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15)
        };

        public static HttpConnectionPoolOptions Conservative => new()
        {
            MaxConnectionsPerHost = 5,
            ConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(60)
        };
    }

    public class ConnectionPoolStatistics
    {
        public int ActiveConnections { get; set; }
        public int TotalConnectionsCreated { get; set; }
        public int TotalConnectionsRemoved { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public double AverageResponseTime { get; set; }
        public Dictionary<string, int> ConnectionsByHost { get; set; }
    }
}