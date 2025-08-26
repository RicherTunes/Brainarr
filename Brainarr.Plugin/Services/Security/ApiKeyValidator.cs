using System;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Security
{
    /// <summary>
    /// SECURITY ENHANCEMENT: Validates API keys before storage to prevent accidental exposure
    /// </summary>
    public static class ApiKeyValidator
    {
        // Common patterns that indicate test/example keys
        private static readonly Regex TestKeyPattern = new Regex(
            @"(test|demo|example|sample|dummy|fake|mock|placeholder|xxx|abc|123|000|111)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Patterns for detecting exposed keys in logs/output
        private static readonly Regex[] ApiKeyPatterns = new[]
        {
            new Regex(@"sk-[a-zA-Z0-9]{48}", RegexOptions.Compiled), // OpenAI
            new Regex(@"sk-ant-[a-zA-Z0-9]{90,}", RegexOptions.Compiled), // Anthropic
            new Regex(@"gsk_[a-zA-Z0-9]{50,}", RegexOptions.Compiled), // Groq
            new Regex(@"pplx-[a-zA-Z0-9]{48}", RegexOptions.Compiled), // Perplexity
            new Regex(@"AIza[a-zA-Z0-9]{35}", RegexOptions.Compiled), // Google
            new Regex(@"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}", RegexOptions.Compiled) // Generic UUID
        };

        /// <summary>
        /// Validates an API key for security issues
        /// </summary>
        public static ValidationResult ValidateApiKey(string provider, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ValidationResult(false, "API key cannot be empty");
            }

            // Check for test/dummy keys
            if (TestKeyPattern.IsMatch(apiKey))
            {
                return new ValidationResult(false, "API key appears to be a test/example key");
            }

            // Check key length based on provider
            var (minLength, maxLength) = GetKeyLengthRequirements(provider);
            if (apiKey.Length < minLength || apiKey.Length > maxLength)
            {
                return new ValidationResult(false, 
                    $"API key length must be between {minLength} and {maxLength} characters");
            }

            // Check for common mistakes
            if (apiKey.Contains(" ") || apiKey.Contains("\n") || apiKey.Contains("\t"))
            {
                return new ValidationResult(false, "API key contains whitespace characters");
            }

            // Check for URL encoding issues
            if (apiKey.Contains("%"))
            {
                return new ValidationResult(false, "API key appears to be URL encoded");
            }

            // Provider-specific validation
            return ValidateProviderSpecific(provider, apiKey);
        }

        /// <summary>
        /// Redacts API keys from text for safe logging
        /// </summary>
        public static string RedactApiKeys(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            foreach (var pattern in ApiKeyPatterns)
            {
                text = pattern.Replace(text, match =>
                {
                    var key = match.Value;
                    if (key.Length <= 8)
                        return "[REDACTED]";
                    
                    // Show first 4 and last 4 chars only
                    return $"{key.Substring(0, 4)}...{key.Substring(key.Length - 4)}";
                });
            }

            // Generic key patterns
            text = Regex.Replace(text, @"(api[_-]?key|apikey|api_secret|secret[_-]?key)[\"'\s]*[:=][\"'\s]*([^\"'\s]+)", 
                "$1=[REDACTED]", RegexOptions.IgnoreCase);

            return text;
        }

        private static (int min, int max) GetKeyLengthRequirements(string provider)
        {
            return provider?.ToLower() switch
            {
                "openai" => (40, 60),
                "anthropic" => (80, 120),
                "groq" => (50, 70),
                "perplexity" => (40, 60),
                "gemini" or "google" => (35, 45),
                "openrouter" => (20, 100),
                "deepseek" => (30, 60),
                _ => (10, 200) // Generic range
            };
        }

        private static ValidationResult ValidateProviderSpecific(string provider, string apiKey)
        {
            switch (provider?.ToLower())
            {
                case "openai":
                    if (!apiKey.StartsWith("sk-"))
                        return new ValidationResult(false, "OpenAI keys should start with 'sk-'");
                    break;
                    
                case "anthropic":
                    if (!apiKey.StartsWith("sk-ant-"))
                        return new ValidationResult(false, "Anthropic keys should start with 'sk-ant-'");
                    break;
                    
                case "groq":
                    if (!apiKey.StartsWith("gsk_"))
                        return new ValidationResult(false, "Groq keys should start with 'gsk_'");
                    break;
                    
                case "perplexity":
                    if (!apiKey.StartsWith("pplx-"))
                        return new ValidationResult(false, "Perplexity keys should start with 'pplx-'");
                    break;
            }

            return new ValidationResult(true, "Valid");
        }

        public class ValidationResult
        {
            public bool IsValid { get; }
            public string Message { get; }

            public ValidationResult(bool isValid, string message)
            {
                IsValid = isValid;
                Message = message;
            }
        }
    }
}