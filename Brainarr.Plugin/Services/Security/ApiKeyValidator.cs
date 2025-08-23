using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Brainarr.Plugin.Services.Security
{
    /// <summary>
    /// Comprehensive API key validation service with format checking and security validation
    /// </summary>
    public static class ApiKeyValidator
    {
        // Maximum allowed key length to prevent buffer attacks
        private const int MaxKeyLength = 500;
        
        // Minimum key length for security
        private const int MinKeyLength = 10;
        
        // Provider-specific key patterns for format validation
        private static readonly Dictionary<string, Regex> ProviderKeyPatterns = new Dictionary<string, Regex>
        {
            // OpenAI keys start with "sk-" and contain alphanumeric chars
            ["OpenAI"] = new Regex(@"^sk-[a-zA-Z0-9]{48,}$", RegexOptions.Compiled),
            
            // Anthropic keys start with "sk-ant-" 
            ["Anthropic"] = new Regex(@"^sk-ant-[a-zA-Z0-9\-]{40,}$", RegexOptions.Compiled),
            
            // Perplexity keys start with "pplx-"
            ["Perplexity"] = new Regex(@"^pplx-[a-zA-Z0-9]{40,}$", RegexOptions.Compiled),
            
            // OpenRouter keys are typically UUIDs or alphanumeric
            ["OpenRouter"] = new Regex(@"^[a-zA-Z0-9\-]{32,}$", RegexOptions.Compiled),
            
            // DeepSeek keys are alphanumeric
            ["DeepSeek"] = new Regex(@"^[a-zA-Z0-9]{32,}$", RegexOptions.Compiled),
            
            // Google Gemini keys are complex alphanumeric
            ["Gemini"] = new Regex(@"^[a-zA-Z0-9\-_]{39,}$", RegexOptions.Compiled),
            
            // Groq keys are alphanumeric
            ["Groq"] = new Regex(@"^gsk_[a-zA-Z0-9]{50,}$", RegexOptions.Compiled)
        };
        
        // Suspicious patterns that might indicate injection attempts
        private static readonly string[] SuspiciousPatterns = new[]
        {
            // SQL injection patterns
            "SELECT", "INSERT", "UPDATE", "DELETE", "DROP", "CREATE", "ALTER",
            "--", "/*", "*/", ";",
            
            // Command injection patterns
            "|", "&", ">", "<", "`", "$", "\\", "\n", "\r", "\0",
            
            // Script injection patterns
            "<script", "</script", "javascript:", "onerror", "onclick",
            
            // Path traversal patterns
            "../", "..\\", "%2e%2e", "0x2e0x2e",
            
            // Unicode/encoding attacks
            "%00", "\u0000", "\\x00"
        };
        
        /// <summary>
        /// Validates an API key with comprehensive security checks
        /// </summary>
        public static ValidationResult ValidateApiKey(string apiKey, string provider)
        {
            // Null/empty check
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Error = "API key cannot be empty"
                };
            }
            
            // Trim whitespace
            apiKey = apiKey.Trim();
            
            // Length validation
            if (apiKey.Length < MinKeyLength)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Error = $"API key is too short (minimum {MinKeyLength} characters)"
                };
            }
            
            if (apiKey.Length > MaxKeyLength)
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Error = $"API key exceeds maximum length of {MaxKeyLength} characters"
                };
            }
            
            // Check for suspicious patterns
            var upperKey = apiKey.ToUpperInvariant();
            foreach (var pattern in SuspiciousPatterns)
            {
                if (upperKey.Contains(pattern.ToUpperInvariant()))
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Error = $"API key contains suspicious pattern: {pattern}",
                        IsSuspicious = true
                    };
                }
            }
            
            // Check for non-printable characters
            foreach (char c in apiKey)
            {
                if (char.IsControl(c) && c != '\t')
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Error = "API key contains invalid control characters",
                        IsSuspicious = true
                    };
                }
            }
            
            // Provider-specific format validation
            if (!string.IsNullOrEmpty(provider) && ProviderKeyPatterns.ContainsKey(provider))
            {
                var pattern = ProviderKeyPatterns[provider];
                if (!pattern.IsMatch(apiKey))
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Error = $"API key format is invalid for {provider} provider",
                        IsFormatError = true
                    };
                }
            }
            
            // Check for common test/demo keys
            if (IsTestKey(apiKey))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    Error = "Test or demo API keys are not allowed",
                    IsTestKey = true
                };
            }
            
            return new ValidationResult
            {
                IsValid = true,
                SanitizedKey = apiKey
            };
        }
        
        /// <summary>
        /// Checks if a key is a known test/demo key
        /// </summary>
        private static bool IsTestKey(string apiKey)
        {
            var lowerKey = apiKey.ToLowerInvariant();
            
            // Common test key patterns
            string[] testPatterns = new[]
            {
                "test", "demo", "sample", "example", 
                "1234", "0000", "aaaa", "xxxx",
                "your-api-key", "your_api_key",
                "placeholder", "dummy"
            };
            
            foreach (var pattern in testPatterns)
            {
                if (lowerKey.Contains(pattern))
                {
                    return true;
                }
            }
            
            // Check for repeated characters (like "aaaaaa...")
            if (apiKey.Length > 10)
            {
                char firstChar = apiKey[0];
                bool allSame = true;
                for (int i = 1; i < Math.Min(apiKey.Length, 20); i++)
                {
                    if (apiKey[i] != firstChar)
                    {
                        allSame = false;
                        break;
                    }
                }
                if (allSame)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        /// <summary>
        /// Sanitizes an API key for safe storage/transmission
        /// </summary>
        public static string SanitizeApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return string.Empty;
            }
            
            // Trim whitespace
            apiKey = apiKey.Trim();
            
            // Remove any control characters
            apiKey = Regex.Replace(apiKey, @"[\x00-\x1F\x7F]", string.Empty);
            
            // Truncate if too long
            if (apiKey.Length > MaxKeyLength)
            {
                apiKey = apiKey.Substring(0, MaxKeyLength);
            }
            
            return apiKey;
        }
        
        /// <summary>
        /// Masks an API key for logging (shows first 4 and last 4 chars)
        /// </summary>
        public static string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return "[empty]";
            }
            
            if (apiKey.Length <= 10)
            {
                return "***";
            }
            
            return $"{apiKey.Substring(0, 4)}...{apiKey.Substring(apiKey.Length - 4)}";
        }
        
        /// <summary>
        /// Result of API key validation
        /// </summary>
        public class ValidationResult
        {
            public bool IsValid { get; set; }
            public string Error { get; set; }
            public string SanitizedKey { get; set; }
            public bool IsSuspicious { get; set; }
            public bool IsFormatError { get; set; }
            public bool IsTestKey { get; set; }
        }
    }
}