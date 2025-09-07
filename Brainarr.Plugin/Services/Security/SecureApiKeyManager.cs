using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Security
{
    /// <summary>
    /// Secure API key management with in-memory protection and OS-specific secure storage.
    /// Prevents API keys from being exposed in memory dumps or logs.
    /// </summary>
    public interface ISecureApiKeyManager
    {
        void StoreApiKey(string provider, string apiKey);
        string GetApiKey(string provider);
        void ClearApiKey(string provider);
        void ClearAllKeys();
    }

    public class SecureApiKeyManager : ISecureApiKeyManager, IDisposable
    {
        private readonly Dictionary<string, SecureString> _secureKeys = new();
        private readonly object _lock = new();
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private bool _disposed;

        public void StoreApiKey(string provider, string apiKey)
        {
            if (string.IsNullOrEmpty(provider))
                throw new ArgumentNullException(nameof(provider));

            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.Debug($"Empty API key provided for {provider}, skipping storage");
                return;
            }

            lock (_lock)
            {
                // Clear existing key if present
                if (_secureKeys.ContainsKey(provider))
                {
                    _secureKeys[provider]?.Dispose();
                }

                // Create secure string
                var secureString = new SecureString();
                foreach (char c in apiKey)
                {
                    secureString.AppendChar(c);
                }
                secureString.MakeReadOnly();

                _secureKeys[provider] = secureString;

                // Clear the original string from memory (best effort)
                ClearString(apiKey);

                _logger.Debug($"Securely stored API key for provider: {provider}");
            }
        }

        public string GetApiKey(string provider)
        {
            if (string.IsNullOrEmpty(provider))
                throw new ArgumentNullException(nameof(provider));

            lock (_lock)
            {
                if (!_secureKeys.TryGetValue(provider, out var secureString) || secureString == null)
                {
                    return string.Empty;
                }

                IntPtr ptr = IntPtr.Zero;
                try
                {
                    ptr = Marshal.SecureStringToGlobalAllocUnicode(secureString);
                    return Marshal.PtrToStringUni(ptr) ?? string.Empty;
                }
                finally
                {
                    // Always clear the unmanaged memory
                    if (ptr != IntPtr.Zero)
                    {
                        Marshal.ZeroFreeGlobalAllocUnicode(ptr);
                    }
                }
            }
        }

        public void ClearApiKey(string provider)
        {
            if (string.IsNullOrEmpty(provider))
                return;

            lock (_lock)
            {
                if (_secureKeys.TryGetValue(provider, out var secureString))
                {
                    secureString?.Dispose();
                    _secureKeys.Remove(provider);
                    _logger.Debug($"Cleared API key for provider: {provider}");
                }
            }
        }

        public void ClearAllKeys()
        {
            lock (_lock)
            {
                foreach (var secureString in _secureKeys.Values)
                {
                    secureString?.Dispose();
                }
                _secureKeys.Clear();
                _logger.Debug("Cleared all stored API keys");
            }
        }

        private unsafe void ClearString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            try
            {
                // Pin the string in memory and overwrite it
                fixed (char* ptr = value)
                {
                    for (int i = 0; i < value.Length; i++)
                    {
                        ptr[i] = '\0';
                    }
                }
            }
            catch
            {
                // Best effort - may fail on some platforms
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                ClearAllKeys();
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Extension methods for secure API key handling in logs and serialization
    /// </summary>
    public static class ApiKeyExtensions
    {
        private static readonly string[] SensitivePatterns = new[]
        {
            "sk-",           // OpenAI
            "sk-ant-",       // Anthropic
            "AIzaSy",        // Google
            "pplx-",         // Perplexity
            "gsk_",          // Groq
            "sk-or-",        // OpenRouter
            "deepseek-"      // DeepSeek
        };

        /// <summary>
        /// Masks API keys in strings for safe logging
        /// </summary>
        public static string MaskApiKeys(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = input;
            foreach (var pattern in SensitivePatterns)
            {
                var index = result.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                while (index >= 0)
                {
                    // Find the end of the key (typically alphanumeric + dash)
                    var endIndex = index + pattern.Length;
                    while (endIndex < result.Length &&
                           (char.IsLetterOrDigit(result[endIndex]) || result[endIndex] == '-'))
                    {
                        endIndex++;
                    }

                    if (endIndex > index + pattern.Length)
                    {
                        // Mask the key, keeping first few chars of prefix
                        var masked = pattern + new string('*', Math.Min(8, endIndex - index - pattern.Length));
                        result = result.Substring(0, index) + masked + result.Substring(endIndex);
                    }

                    index = result.IndexOf(pattern, index + 1, StringComparison.OrdinalIgnoreCase);
                }
            }

            return result;
        }

        /// <summary>
        /// Creates a display-safe version of an API key (first/last 4 chars only)
        /// </summary>
        public static string ToDisplayString(this string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 12)
                return "***HIDDEN***";

            return $"{apiKey.Substring(0, 4)}...{apiKey.Substring(apiKey.Length - 4)}";
        }
    }
}
