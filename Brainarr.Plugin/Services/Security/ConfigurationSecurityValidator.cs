using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Security
{
    /// <summary>
    /// Enhanced configuration security validator with defense-in-depth validation
    /// </summary>
    public class ConfigurationSecurityValidator
    {
        private readonly ILogger _logger;
        
        // Dangerous URL schemes that could lead to security issues
        private static readonly HashSet<string> DangerousSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "javascript", "data", "vbscript", "file", "about", "blob",
            "chrome", "chrome-extension", "ms-appx", "ms-appx-web",
            "ms-local-stream", "res", "resource", "moz-extension",
            "wyciwyg", "view-source", "jar", "attachment", "cid"
        };

        // Potentially dangerous file extensions
        private static readonly HashSet<string> DangerousExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".bat", ".cmd", ".com", ".scr", ".vbs", ".vbe",
            ".js", ".jse", ".wsf", ".wsh", ".ps1", ".psm1", ".msi", ".jar",
            ".app", ".deb", ".rpm", ".dmg", ".pkg", ".run", ".sh", ".bash"
        };

        // Known malicious patterns in configuration values
        private static readonly List<Regex> MaliciousPatterns = new List<Regex>
        {
            new Regex(@"<script[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline),
            new Regex(@"javascript\s*:", RegexOptions.IgnoreCase),
            new Regex(@"on\w+\s*=", RegexOptions.IgnoreCase), // Event handlers
            new Regex(@"eval\s*\(", RegexOptions.IgnoreCase),
            new Regex(@"expression\s*\(", RegexOptions.IgnoreCase),
            new Regex(@"import\s+(?:os|subprocess|sys|socket)", RegexOptions.IgnoreCase),
            new Regex(@"exec\s*\(", RegexOptions.IgnoreCase),
            new Regex(@"__import__", RegexOptions.IgnoreCase),
            new Regex(@"process\.env", RegexOptions.IgnoreCase),
            new Regex(@"require\s*\(['""]child_process", RegexOptions.IgnoreCase)
        };

        // IP address ranges that should be blocked
        private static readonly List<IPNetwork> BlockedNetworks = new List<IPNetwork>
        {
            IPNetwork.Parse("0.0.0.0/8"),        // Current network
            IPNetwork.Parse("10.0.0.0/8"),       // Private network
            IPNetwork.Parse("100.64.0.0/10"),    // Shared address space
            IPNetwork.Parse("127.0.0.0/8"),      // Loopback
            IPNetwork.Parse("169.254.0.0/16"),   // Link local
            IPNetwork.Parse("172.16.0.0/12"),    // Private network
            IPNetwork.Parse("192.0.0.0/24"),     // IETF Protocol Assignments
            IPNetwork.Parse("192.168.0.0/16"),   // Private network
            IPNetwork.Parse("224.0.0.0/4"),      // Multicast
            IPNetwork.Parse("255.255.255.255/32") // Broadcast
        };

        public ConfigurationSecurityValidator(ILogger logger)
        {
            _logger = logger ?? LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Comprehensive validation of a URL configuration value
        /// </summary>
        public ValidationResult ValidateUrl(string url, string fieldName, bool allowLocalhost = false)
        {
            if (string.IsNullOrWhiteSpace(url))
                return ValidationResult.Success();

            try
            {
                var uri = new Uri(url);

                // Check for dangerous schemes
                if (DangerousSchemes.Contains(uri.Scheme))
                {
                    _logger.Warn($"Blocked dangerous URL scheme in {fieldName}: {uri.Scheme}");
                    return ValidationResult.Failure($"URL scheme '{uri.Scheme}' is not allowed for security reasons");
                }

                // Validate host
                var hostValidation = ValidateHost(uri.Host, allowLocalhost);
                if (!hostValidation.IsValid)
                {
                    _logger.Warn($"Invalid host in {fieldName}: {hostValidation.ErrorMessage}");
                    return hostValidation;
                }

                // Check for suspicious patterns in path
                if (ContainsMaliciousPattern(uri.PathAndQuery))
                {
                    _logger.Warn($"Malicious pattern detected in {fieldName} URL path");
                    return ValidationResult.Failure("URL contains potentially malicious patterns");
                }

                // Validate port
                if (uri.Port > 0 && IsBlockedPort(uri.Port))
                {
                    _logger.Warn($"Blocked port {uri.Port} in {fieldName}");
                    return ValidationResult.Failure($"Port {uri.Port} is blocked for security reasons");
                }

                // Check for credential exposure
                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    _logger.Warn($"URL in {fieldName} contains credentials - this is insecure");
                    return ValidationResult.Failure("URLs should not contain embedded credentials");
                }

                return ValidationResult.Success();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Failed to validate URL in {fieldName}");
                return ValidationResult.Failure("Invalid URL format");
            }
        }

        /// <summary>
        /// Validate API keys for common security issues
        /// </summary>
        public ValidationResult ValidateApiKey(string apiKey, string provider)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return ValidationResult.Failure("API key is required");

            // Check for placeholder values
            if (apiKey.Equals("your-api-key-here", StringComparison.OrdinalIgnoreCase) ||
                apiKey.Equals("xxxxxxxx", StringComparison.OrdinalIgnoreCase) ||
                apiKey.StartsWith("sk-proj-", StringComparison.OrdinalIgnoreCase) && apiKey.Length < 20)
            {
                return ValidationResult.Failure("Please enter a valid API key");
            }

            // Check for exposed test keys
            if (IsTestApiKey(apiKey))
            {
                _logger.Warn($"Test API key detected for {provider}");
                return ValidationResult.Failure("Test API keys should not be used in production");
            }

            // Check minimum entropy (randomness)
            if (CalculateEntropy(apiKey) < 3.0)
            {
                return ValidationResult.Failure("API key appears to be weak or invalid");
            }

            // Provider-specific validation
            return ValidateProviderSpecificApiKey(apiKey, provider);
        }

        /// <summary>
        /// Validate file paths for security issues
        /// </summary>
        public ValidationResult ValidateFilePath(string path, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ValidationResult.Success();

            // Check for path traversal attempts
            if (path.Contains("..") || path.Contains("~"))
            {
                _logger.Warn($"Path traversal attempt in {fieldName}: {path}");
                return ValidationResult.Failure("Path contains invalid characters");
            }

            // Check for dangerous extensions
            foreach (var ext in DangerousExtensions)
            {
                if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warn($"Dangerous file extension in {fieldName}: {ext}");
                    return ValidationResult.Failure($"File type '{ext}' is not allowed");
                }
            }

            // Check for command injection patterns
            if (ContainsCommandInjection(path))
            {
                _logger.Warn($"Command injection pattern in {fieldName}");
                return ValidationResult.Failure("Path contains invalid characters");
            }

            return ValidationResult.Success();
        }

        private ValidationResult ValidateHost(string host, bool allowLocalhost)
        {
            // Check for IP address
            if (IPAddress.TryParse(host, out var ipAddress))
            {
                // Check against blocked networks
                foreach (var network in BlockedNetworks)
                {
                    if (network.Contains(ipAddress))
                    {
                        if (!allowLocalhost || !IsLocalhost(ipAddress))
                        {
                            return ValidationResult.Failure($"IP address {host} is in a blocked network range");
                        }
                    }
                }
            }

            // Check for suspicious hostnames
            if (host.Contains("internal", StringComparison.OrdinalIgnoreCase) ||
                host.Contains("local", StringComparison.OrdinalIgnoreCase) && !allowLocalhost ||
                host.Contains("metadata", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && !allowLocalhost)
            {
                return ValidationResult.Failure($"Host '{host}' appears to reference internal resources");
            }

            // DNS rebinding protection
            if (IsDnsRebindingRisk(host))
            {
                return ValidationResult.Failure("Host may be vulnerable to DNS rebinding attacks");
            }

            return ValidationResult.Success();
        }

        private bool IsLocalhost(IPAddress address)
        {
            return IPAddress.IsLoopback(address) || 
                   address.ToString() == "127.0.0.1" || 
                   address.ToString() == "::1";
        }

        private bool IsDnsRebindingRisk(string host)
        {
            // Check for domains known to be used in DNS rebinding attacks
            var suspiciousDomains = new[] { "xip.io", "nip.io", "sslip.io", "localtest.me" };
            return suspiciousDomains.Any(domain => host.EndsWith(domain, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsBlockedPort(int port)
        {
            // Common internal service ports that should be blocked
            var blockedPorts = new[] { 22, 23, 25, 110, 135, 139, 445, 3389, 5432, 3306, 1433 };
            return blockedPorts.Contains(port);
        }

        private bool ContainsMaliciousPattern(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return MaliciousPatterns.Any(pattern => pattern.IsMatch(value));
        }

        private bool ContainsCommandInjection(string value)
        {
            var dangerousChars = new[] { ';', '|', '&', '$', '`', '\n', '\r', '>', '<' };
            return dangerousChars.Any(value.Contains);
        }

        private bool IsTestApiKey(string apiKey)
        {
            var testKeyPatterns = new[]
            {
                "test", "demo", "sample", "example", "sandbox",
                "00000000", "11111111", "12345678", "abcdefgh"
            };

            var lowerKey = apiKey.ToLowerInvariant();
            return testKeyPatterns.Any(pattern => lowerKey.Contains(pattern));
        }

        private double CalculateEntropy(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            var charCounts = new Dictionary<char, int>();
            foreach (var c in value)
            {
                if (charCounts.ContainsKey(c))
                    charCounts[c]++;
                else
                    charCounts[c] = 1;
            }

            double entropy = 0;
            var len = (double)value.Length;

            foreach (var count in charCounts.Values)
            {
                var probability = count / len;
                entropy -= probability * Math.Log(probability, 2);
            }

            return entropy;
        }

        private ValidationResult ValidateProviderSpecificApiKey(string apiKey, string provider)
        {
            switch (provider?.ToLowerInvariant())
            {
                case "openai":
                    if (!apiKey.StartsWith("sk-") || apiKey.Length < 40)
                        return ValidationResult.Failure("Invalid OpenAI API key format");
                    break;

                case "anthropic":
                    if (!apiKey.StartsWith("sk-ant-") || apiKey.Length < 40)
                        return ValidationResult.Failure("Invalid Anthropic API key format");
                    break;

                case "groq":
                    if (!apiKey.StartsWith("gsk_") || apiKey.Length < 50)
                        return ValidationResult.Failure("Invalid Groq API key format");
                    break;
            }

            return ValidationResult.Success();
        }

        public class ValidationResult
        {
            public bool IsValid { get; private set; }
            public string ErrorMessage { get; private set; }

            private ValidationResult(bool isValid, string errorMessage = null)
            {
                IsValid = isValid;
                ErrorMessage = errorMessage;
            }

            public static ValidationResult Success() => new ValidationResult(true);
            public static ValidationResult Failure(string message) => new ValidationResult(false, message);
        }

        private class IPNetwork
        {
            private readonly IPAddress _network;
            private readonly int _prefixLength;

            private IPNetwork(IPAddress network, int prefixLength)
            {
                _network = network;
                _prefixLength = prefixLength;
            }

            public static IPNetwork Parse(string cidr)
            {
                var parts = cidr.Split('/');
                var network = IPAddress.Parse(parts[0]);
                var prefixLength = int.Parse(parts[1]);
                return new IPNetwork(network, prefixLength);
            }

            public bool Contains(IPAddress address)
            {
                var networkBytes = _network.GetAddressBytes();
                var addressBytes = address.GetAddressBytes();

                if (networkBytes.Length != addressBytes.Length)
                    return false;

                var bytesToCheck = _prefixLength / 8;
                var bitsToCheck = _prefixLength % 8;

                for (int i = 0; i < bytesToCheck; i++)
                {
                    if (networkBytes[i] != addressBytes[i])
                        return false;
                }

                if (bitsToCheck > 0 && bytesToCheck < networkBytes.Length)
                {
                    var mask = (byte)(0xFF << (8 - bitsToCheck));
                    if ((networkBytes[bytesToCheck] & mask) != (addressBytes[bytesToCheck] & mask))
                        return false;
                }

                return true;
            }
        }
    }
}