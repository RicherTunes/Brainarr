using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Service for auditing security-sensitive operations and enforcing security policies
    /// </summary>
    public class SecurityAuditService
    {
        private readonly Logger _logger;
        private readonly HashSet<string> _sensitivePatterns;
        private readonly HashSet<string> _allowedHosts;
        private readonly bool _enforceHttps;
        private readonly bool _validateCertificates;

        public SecurityAuditService(Logger logger, bool enforceHttps = false, bool validateCertificates = true)
        {
            _logger = logger;
            _enforceHttps = enforceHttps;
            _validateCertificates = validateCertificates;
            
            // Patterns that might indicate sensitive data
            _sensitivePatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "api_key", "apikey", "api-key",
                "secret", "password", "token",
                "bearer", "authorization",
                "x-api-key", "x-auth-token"
            };
            
            // Allowed hosts for external connections (security whitelist)
            _allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // OpenAI
                "api.openai.com",
                // Anthropic
                "api.anthropic.com",
                // Google
                "generativelanguage.googleapis.com",
                // Groq
                "api.groq.com",
                // DeepSeek
                "api.deepseek.com",
                // Perplexity
                "api.perplexity.ai",
                // OpenRouter
                "openrouter.ai",
                // Local hosts (for Ollama, LM Studio)
                "localhost",
                "127.0.0.1",
                "::1"
            };
        }

        /// <summary>
        /// Audits an HTTP request before sending
        /// </summary>
        public bool AuditRequest(HttpRequest request)
        {
            if (request == null)
                return false;

            var uri = new Uri(request.Url.ToString());
            
            // 1. Validate host is allowed
            if (!IsHostAllowed(uri.Host))
            {
                _logger.Warn($"Blocked request to unauthorized host: {uri.Host}");
                return false;
            }
            
            // 2. Check for HTTPS enforcement
            if (_enforceHttps && !IsLocalHost(uri.Host) && uri.Scheme != Uri.UriSchemeHttps)
            {
                _logger.Warn($"Blocked non-HTTPS request to external host: {uri.Host}");
                return false;
            }
            
            // 3. Audit for sensitive data in URL
            if (ContainsSensitiveDataInUrl(uri.ToString()))
            {
                _logger.Warn("Detected potential sensitive data in URL");
                // Log warning but don't block - some APIs require this
            }
            
            // 4. Validate headers don't leak sensitive info
            AuditHeaders(request);
            
            return true;
        }

        /// <summary>
        /// Validates SSL certificate for HTTPS connections
        /// </summary>
        public bool ValidateCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if (!_validateCertificates)
                return true;

            // Allow valid certificates
            if (sslPolicyErrors == SslPolicyErrors.None)
                return true;

            // Allow self-signed certificates for localhost
            if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
            {
                var request = sender as HttpWebRequest;
                if (request != null && IsLocalHost(request.Host))
                {
                    _logger.Debug("Allowing self-signed certificate for localhost");
                    return true;
                }
            }

            _logger.Error($"SSL certificate validation failed: {sslPolicyErrors}");
            return false;
        }

        /// <summary>
        /// Sanitizes log output to remove sensitive information
        /// </summary>
        public string SanitizeForLogging(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var sanitized = input;
            
            // Remove API keys and tokens
            sanitized = Regex.Replace(sanitized, 
                @"(api[_-]?key|token|secret|password|bearer)[\s:=]+[\w\-]+", 
                "$1=***REDACTED***", 
                RegexOptions.IgnoreCase);
            
            // Remove base64 encoded potential secrets
            sanitized = Regex.Replace(sanitized,
                @"[A-Za-z0-9+/]{40,}={0,2}",
                "***REDACTED_BASE64***");
            
            return sanitized;
        }

        private bool IsHostAllowed(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;
                
            // Always allow local hosts
            if (IsLocalHost(host))
                return true;
                
            // Check against whitelist
            return _allowedHosts.Contains(host);
        }

        private bool IsLocalHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
                return false;
                
            var lowerHost = host.ToLower();
            return lowerHost == "localhost" || 
                   lowerHost == "127.0.0.1" || 
                   lowerHost == "::1" ||
                   lowerHost.StartsWith("192.168.") ||
                   lowerHost.StartsWith("10.") ||
                   lowerHost.StartsWith("172.");
        }

        private bool ContainsSensitiveDataInUrl(string url)
        {
            var lowerUrl = url.ToLower();
            return _sensitivePatterns.Any(pattern => lowerUrl.Contains(pattern));
        }

        private void AuditHeaders(HttpRequest request)
        {
            foreach (var header in request.Headers)
            {
                var headerName = header.Key.ToLower();
                
                // Check for sensitive headers being logged
                if (_sensitivePatterns.Any(pattern => headerName.Contains(pattern)))
                {
                    _logger.Debug($"Request contains sensitive header: {header.Key} (value not logged)");
                }
            }
        }
    }

    /// <summary>
    /// Extension methods for security enhancements
    /// </summary>
    public static class SecurityExtensions
    {
        private static readonly SecurityAuditService _auditService = 
            new SecurityAuditService(LogManager.GetCurrentClassLogger());

        /// <summary>
        /// Adds security headers and validation to HTTP request
        /// </summary>
        public static HttpRequest WithSecurityValidation(this HttpRequest request)
        {
            // Audit the request
            _auditService.AuditRequest(request);
            
            // Add security headers
            request.Headers["User-Agent"] = "Brainarr/1.0";
            
            // Ensure no sensitive data in logs
            request.LogHttpError = false;
            
            return request;
        }

        /// <summary>
        /// Sanitizes a string for safe logging
        /// </summary>
        public static string SanitizeForLog(this string input)
        {
            return _auditService.SanitizeForLogging(input);
        }
    }
}