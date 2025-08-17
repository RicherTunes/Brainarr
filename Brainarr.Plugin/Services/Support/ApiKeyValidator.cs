using System;
using System.Text.RegularExpressions;
using NzbDrone.Core.ImportLists.Brainarr.Configuration;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Support
{
    /// <summary>
    /// Validates API keys for different AI providers with provider-specific rules
    /// </summary>
    public static class ApiKeyValidator
    {
        // Provider-specific API key patterns
        private static readonly Regex OpenAIKeyPattern = new Regex(
            @"^sk-[A-Za-z0-9]{48,}$",
            RegexOptions.Compiled);

        private static readonly Regex AnthropicKeyPattern = new Regex(
            @"^sk-ant-api\d{2}-[A-Za-z0-9\-]{95,}$",
            RegexOptions.Compiled);

        private static readonly Regex GeminiKeyPattern = new Regex(
            @"^AIza[A-Za-z0-9\-_]{35}$",
            RegexOptions.Compiled);

        private static readonly Regex GroqKeyPattern = new Regex(
            @"^gsk_[A-Za-z0-9]{50,}$",
            RegexOptions.Compiled);

        private static readonly Regex DeepSeekKeyPattern = new Regex(
            @"^sk-[a-f0-9]{32,}$",
            RegexOptions.Compiled);

        private static readonly Regex PerplexityKeyPattern = new Regex(
            @"^pplx-[a-f0-9]{48,}$",
            RegexOptions.Compiled);

        private static readonly Regex OpenRouterKeyPattern = new Regex(
            @"^sk-or-[A-Za-z0-9\-]{40,}$",
            RegexOptions.Compiled);

        /// <summary>
        /// Validates an API key for a specific provider
        /// </summary>
        public static bool ValidateApiKey(string apiKey, AIProvider provider)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return false;

            // Remove any accidental whitespace
            apiKey = apiKey.Trim();

            // Check for common placeholder values
            if (IsPlaceholder(apiKey))
                return false;

            // Provider-specific validation
            return provider switch
            {
                AIProvider.OpenAI => ValidateOpenAIKey(apiKey),
                AIProvider.Anthropic => ValidateAnthropicKey(apiKey),
                AIProvider.Gemini => ValidateGeminiKey(apiKey),
                AIProvider.Groq => ValidateGroqKey(apiKey),
                AIProvider.DeepSeek => ValidateDeepSeekKey(apiKey),
                AIProvider.Perplexity => ValidatePerplexityKey(apiKey),
                AIProvider.OpenRouter => ValidateOpenRouterKey(apiKey),
                AIProvider.Ollama => ValidateLocalProviderUrl(apiKey), // URL validation for local
                AIProvider.LMStudio => ValidateLocalProviderUrl(apiKey), // URL validation for local
                _ => ValidateGenericKey(apiKey)
            };
        }

        /// <summary>
        /// Validates OpenAI API key format
        /// </summary>
        private static bool ValidateOpenAIKey(string apiKey)
        {
            // OpenAI keys start with "sk-" and are typically 51+ characters
            if (!apiKey.StartsWith("sk-") || apiKey.Length < 51)
                return false;

            // Check if it matches the expected pattern
            return OpenAIKeyPattern.IsMatch(apiKey);
        }

        /// <summary>
        /// Validates Anthropic API key format
        /// </summary>
        private static bool ValidateAnthropicKey(string apiKey)
        {
            // Anthropic keys start with "sk-ant-" and are typically 108+ characters
            if (!apiKey.StartsWith("sk-ant-") || apiKey.Length < 108)
                return false;

            return AnthropicKeyPattern.IsMatch(apiKey);
        }

        /// <summary>
        /// Validates Google Gemini API key format
        /// </summary>
        private static bool ValidateGeminiKey(string apiKey)
        {
            // Gemini keys start with "AIza" and are exactly 39 characters
            if (!apiKey.StartsWith("AIza") || apiKey.Length != 39)
                return false;

            return GeminiKeyPattern.IsMatch(apiKey);
        }

        /// <summary>
        /// Validates Groq API key format
        /// </summary>
        private static bool ValidateGroqKey(string apiKey)
        {
            // Groq keys start with "gsk_" and are typically 54+ characters
            if (!apiKey.StartsWith("gsk_") || apiKey.Length < 54)
                return false;

            return GroqKeyPattern.IsMatch(apiKey);
        }

        /// <summary>
        /// Validates DeepSeek API key format
        /// </summary>
        private static bool ValidateDeepSeekKey(string apiKey)
        {
            // DeepSeek keys start with "sk-" and contain hex characters
            if (!apiKey.StartsWith("sk-") || apiKey.Length < 35)
                return false;

            return DeepSeekKeyPattern.IsMatch(apiKey);
        }

        /// <summary>
        /// Validates Perplexity API key format
        /// </summary>
        private static bool ValidatePerplexityKey(string apiKey)
        {
            // Perplexity keys start with "pplx-" and are typically 53+ characters
            if (!apiKey.StartsWith("pplx-") || apiKey.Length < 53)
                return false;

            return PerplexityKeyPattern.IsMatch(apiKey);
        }

        /// <summary>
        /// Validates OpenRouter API key format
        /// </summary>
        private static bool ValidateOpenRouterKey(string apiKey)
        {
            // OpenRouter keys start with "sk-or-" and are typically 46+ characters
            if (!apiKey.StartsWith("sk-or-") || apiKey.Length < 46)
                return false;

            return OpenRouterKeyPattern.IsMatch(apiKey);
        }

        /// <summary>
        /// Validates URL format for local providers
        /// </summary>
        private static bool ValidateLocalProviderUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            // For local providers, we validate URLs instead of API keys
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                // Allow http for localhost/local network
                if (uri.Scheme == Uri.UriSchemeHttp)
                {
                    return IsLocalUrl(uri);
                }
                
                // Always allow https
                return uri.Scheme == Uri.UriSchemeHttps;
            }

            // If it's not a URL, it might be just a port number for localhost
            if (int.TryParse(url, out var port))
            {
                return port > 0 && port <= 65535;
            }

            return false;
        }

        /// <summary>
        /// Checks if a URI is local (localhost or private network)
        /// </summary>
        private static bool IsLocalUrl(Uri uri)
        {
            var host = uri.Host.ToLower();
            
            // Localhost variations
            if (host == "localhost" || host == "127.0.0.1" || host == "::1")
                return true;

            // Private network ranges
            if (uri.HostNameType == UriHostNameType.IPv4)
            {
                var parts = host.Split('.');
                if (parts.Length == 4)
                {
                    if (int.TryParse(parts[0], out var first))
                    {
                        // 10.0.0.0/8
                        if (first == 10)
                            return true;
                        
                        // 172.16.0.0/12
                        if (first == 172 && int.TryParse(parts[1], out var second))
                        {
                            if (second >= 16 && second <= 31)
                                return true;
                        }
                        
                        // 192.168.0.0/16
                        if (first == 192 && int.TryParse(parts[1], out second))
                        {
                            if (second == 168)
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Generic validation for unknown providers
        /// </summary>
        private static bool ValidateGenericKey(string apiKey)
        {
            // Basic validation: reasonable length and no obvious issues
            if (apiKey.Length < 20 || apiKey.Length > 200)
                return false;

            // Check for reasonable characters (alphanumeric, dash, underscore)
            return Regex.IsMatch(apiKey, @"^[A-Za-z0-9\-_]+$");
        }

        /// <summary>
        /// Checks if an API key is a common placeholder
        /// </summary>
        private static bool IsPlaceholder(string apiKey)
        {
            var lower = apiKey.ToLower();
            
            // Common placeholders
            string[] placeholders = {
                "your-api-key",
                "your_api_key",
                "api-key",
                "api_key",
                "placeholder",
                "example",
                "test",
                "demo",
                "xxxxxxxx",
                "your-key-here",
                "paste-your-key",
                "enter-your-key",
                "<api-key>",
                "[api-key]",
                "{api-key}",
                "sk-...",
                "changeme",
                "replace-me",
                "todo"
            };

            foreach (var placeholder in placeholders)
            {
                if (lower.Contains(placeholder))
                    return true;
            }

            // Check for repeated characters (e.g., "aaaaaaaa")
            if (apiKey.Length > 8)
            {
                var firstChar = apiKey[0];
                if (apiKey.All(c => c == firstChar))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Gets a helpful error message for invalid API keys
        /// </summary>
        public static string GetValidationErrorMessage(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.OpenAI => "OpenAI API key should start with 'sk-' and be at least 51 characters long",
                AIProvider.Anthropic => "Anthropic API key should start with 'sk-ant-' and be at least 108 characters long",
                AIProvider.Gemini => "Google Gemini API key should start with 'AIza' and be exactly 39 characters long",
                AIProvider.Groq => "Groq API key should start with 'gsk_' and be at least 54 characters long",
                AIProvider.DeepSeek => "DeepSeek API key should start with 'sk-' and contain hexadecimal characters",
                AIProvider.Perplexity => "Perplexity API key should start with 'pplx-' and be at least 53 characters long",
                AIProvider.OpenRouter => "OpenRouter API key should start with 'sk-or-' and be at least 46 characters long",
                AIProvider.Ollama => "Ollama requires a valid URL (e.g., http://localhost:11434)",
                AIProvider.LMStudio => "LM Studio requires a valid URL (e.g., http://localhost:1234)",
                _ => "API key must be between 20 and 200 characters and contain only alphanumeric characters, dashes, and underscores"
            };
        }

        /// <summary>
        /// Masks an API key for safe display
        /// </summary>
        public static string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return "[empty]";

            if (apiKey.Length <= 8)
                return "***";

            // Show first 4 and last 4 characters
            var prefix = apiKey.Substring(0, 4);
            var suffix = apiKey.Substring(apiKey.Length - 4);
            var masked = $"{prefix}...{suffix}";

            return masked;
        }
    }
}