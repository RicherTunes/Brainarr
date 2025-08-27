using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;

namespace Brainarr.Plugin.Services.Security
{
    public interface ISecureHttpClient
    {
        Task<HttpResponse> ExecuteAsync(HttpRequest request);
        HttpRequestBuilder CreateSecureRequest(string url);
    }

    public class SecureHttpClient : ISecureHttpClient
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly SecurityConfiguration _securityConfig;
        
        // Security constants
        private const int MaxRequestSizeBytes = 10 * 1024 * 1024; // 10MB
        private const int MaxResponseSizeBytes = 50 * 1024 * 1024; // 50MB
        private const int DefaultTimeoutSeconds = 30;
        private const int MaxTimeoutSeconds = 120;
        
        // Required security headers
        private static readonly Dictionary<string, string> RequiredSecurityHeaders = new()
        {
            ["X-Content-Type-Options"] = "nosniff",
            ["X-Frame-Options"] = "DENY",
            ["X-XSS-Protection"] = "1; mode=block",
            ["Cache-Control"] = "no-store, no-cache, must-revalidate, private",
            ["Pragma"] = "no-cache"
        };

        public SecureHttpClient(IHttpClient httpClient, Logger logger, SecurityConfiguration? securityConfig = null)
        {
            _httpClient = httpClient;
            _logger = logger;
            _securityConfig = securityConfig ?? new SecurityConfiguration();
        }

        public async Task<HttpResponse> ExecuteAsync(HttpRequest request)
        {
            // Validate request before sending
            ValidateRequest(request);
            
            // Apply security headers
            ApplySecurityHeaders(request);
            
            // Set secure timeout
            request.RequestTimeout = GetSecureTimeout(request.RequestTimeout);
            
            // Log sanitized request info
            _logger.Debug($"Executing secure HTTP request to {SanitizeUrl(request.Url.ToString())}");
            
            try
            {
                // Execute request
                var response = await _httpClient.ExecuteAsync(request);
                
                // Validate response
                ValidateResponse(response);
                
                return response;
            }
            catch (HttpException ex)
            {
                // Sanitize error message to prevent information disclosure
                _logger.Error($"HTTP request failed: {SanitizeErrorMessage(ex.Message)}");
                throw new SecureHttpException("Request failed. Check logs for details.", ex);
            }
            catch (Exception ex)
            {
                _logger.Error($"Unexpected error during HTTP request: {ex.GetType().Name}");
                throw new SecureHttpException("An unexpected error occurred during the request.", ex);
            }
        }

        public HttpRequestBuilder CreateSecureRequest(string url)
        {
            // Validate URL
            if (!IsValidUrl(url))
            {
                throw new ArgumentException("Invalid URL format");
            }
            
            // Force HTTPS for external requests
            url = EnforceHttps(url);
            
            var builder = new HttpRequestBuilder(url);
            
            // Apply default security headers
            foreach (var header in RequiredSecurityHeaders)
            {
                builder.SetHeader(header.Key, header.Value);
            }
            
            // Set secure user agent
            builder.SetHeader("User-Agent", "Brainarr/1.0 (Secure)");
            
            return builder;
        }

        private void ValidateRequest(HttpRequest request)
        {
            // Validate URL
            if (request.Url == null || !IsValidUrl(request.Url.ToString()))
            {
                throw new ArgumentException("Invalid request URL");
            }
            
            // Validate request size
            if (request.ContentData != null && request.ContentData.Length > MaxRequestSizeBytes)
            {
                throw new ArgumentException($"Request body exceeds maximum size of {MaxRequestSizeBytes} bytes");
            }
            
            // Validate timeout
            if (request.RequestTimeout.TotalSeconds > MaxTimeoutSeconds)
            {
                throw new ArgumentException($"Request timeout exceeds maximum of {MaxTimeoutSeconds} seconds");
            }
            
            // Check for dangerous headers
            var dangerousHeaders = new[] { "X-Forwarded-For", "X-Real-IP", "X-Originating-IP" };
            foreach (var header in dangerousHeaders)
            {
                if (request.Headers.ContainsKey(header))
                {
                    _logger.Warn($"Removing potentially dangerous header: {header}");
                    request.Headers.Remove(header);
                }
            }
        }

        private void ValidateResponse(HttpResponse response)
        {
            // Check response size
            if (response.ResponseData?.Length > MaxResponseSizeBytes)
            {
                throw new SecureHttpException($"Response exceeds maximum size of {MaxResponseSizeBytes} bytes");
            }
            
            // Validate content type if expecting JSON
            if (response.Headers.ContentType != null && 
                !response.Headers.ContentType.Contains("application/json") &&
                !response.Headers.ContentType.Contains("text/plain"))
            {
                _logger.Warn($"Unexpected content type received: {response.Headers.ContentType}");
            }
            
            // Check for security headers in response
            if (_securityConfig.ValidateResponseHeaders)
            {
                ValidateResponseSecurityHeaders(response);
            }
        }

        private void ValidateResponseSecurityHeaders(HttpResponse response)
        {
            var recommendedHeaders = new[] 
            { 
                "Strict-Transport-Security",
                "Content-Security-Policy",
                "X-Content-Type-Options"
            };
            
            foreach (var header in recommendedHeaders)
            {
                if (!response.Headers.AllKeys.Any(k => k.Equals(header, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.Debug($"Response missing recommended security header: {header}");
                }
            }
        }

        private void ApplySecurityHeaders(HttpRequest request)
        {
            // Apply required security headers if not already present
            foreach (var header in RequiredSecurityHeaders)
            {
                if (!request.Headers.ContainsKey(header.Key))
                {
                    request.Headers[header.Key] = header.Value;
                }
            }
            
            // Add request ID for tracing
            if (!request.Headers.ContainsKey("X-Request-ID"))
            {
                request.Headers["X-Request-ID"] = Guid.NewGuid().ToString("N");
            }
        }

        private TimeSpan GetSecureTimeout(TimeSpan? requestedTimeout)
        {
            if (requestedTimeout == null)
            {
                return TimeSpan.FromSeconds(DefaultTimeoutSeconds);
            }
            
            var totalSeconds = requestedTimeout.Value.TotalSeconds;
            if (totalSeconds <= 0 || totalSeconds > MaxTimeoutSeconds)
            {
                _logger.Warn($"Invalid timeout {totalSeconds}s, using default {DefaultTimeoutSeconds}s");
                return TimeSpan.FromSeconds(DefaultTimeoutSeconds);
            }
            
            return requestedTimeout.Value;
        }

        private bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }
            
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }
            
            // Only allow HTTP(S) schemes
            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        private string EnforceHttps(string url)
        {
            if (_securityConfig.EnforceHttps && url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(url);
                
                // Don't enforce HTTPS for localhost/local network
                if (!IsLocalUrl(uri))
                {
                    _logger.Debug("Upgrading HTTP to HTTPS for external request");
                    return "https" + url.Substring(4);
                }
            }
            
            return url;
        }

        private bool IsLocalUrl(Uri uri)
        {
            return uri.Host == "localhost" || 
                   uri.Host == "127.0.0.1" ||
                   uri.Host.StartsWith("192.168.") ||
                   uri.Host.StartsWith("10.") ||
                   uri.Host.StartsWith("172.");
        }

        private string SanitizeUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                // Return only scheme and host, no path or query params
                return $"{uri.Scheme}://{uri.Host}";
            }
            catch
            {
                return "[invalid-url]";
            }
        }

        private string SanitizeErrorMessage(string message)
        {
            // Remove potentially sensitive information from error messages
            if (string.IsNullOrWhiteSpace(message))
            {
                return "Unknown error";
            }
            
            // Remove file paths
            message = System.Text.RegularExpressions.Regex.Replace(message, @"[a-zA-Z]:[\\\/][\w\s\\\/.-]+", "[path]");
            message = System.Text.RegularExpressions.Regex.Replace(message, @"\/[\w\s\/.-]+", "[path]");
            
            // Remove IP addresses
            message = System.Text.RegularExpressions.Regex.Replace(message, @"\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b", "[ip]");
            
            // Remove URLs
            message = System.Text.RegularExpressions.Regex.Replace(message, @"https?:\/\/[^\s]+", "[url]");
            
            // Truncate if too long
            if (message.Length > 200)
            {
                message = message.Substring(0, 197) + "...";
            }
            
            return message;
        }
    }

    public class SecurityConfiguration
    {
        public bool EnforceHttps { get; set; } = true;
        public bool ValidateResponseHeaders { get; set; } = true;
        public bool ValidateCertificates { get; set; } = true;
        public List<string> AllowedHosts { get; set; } = new();
        public List<string> BlockedHosts { get; set; } = new();
    }

    public class SecureHttpException : Exception
    {
        public SecureHttpException(string message) : base(message) { }
        public SecureHttpException(string message, Exception innerException) : base(message, innerException) { }
    }
}