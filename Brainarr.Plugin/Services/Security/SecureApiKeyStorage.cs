using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Security
{
    public interface ISecureApiKeyStorage
    {
        void StoreApiKey(string provider, string apiKey);
        SecureString GetApiKey(string provider);
        string GetApiKeyForRequest(string provider);
        void ClearApiKey(string provider);
        void ClearAllApiKeys();
    }

    public class SecureApiKeyStorage : ISecureApiKeyStorage, IDisposable
    {
        private readonly Logger _logger;
        private readonly Dictionary<string, SecureString> _secureKeys;
        private readonly Dictionary<string, byte[]> _encryptedKeys;
        private readonly byte[] _entropy;
        private readonly object _lock = new object();
        private bool _disposed;

        public SecureApiKeyStorage(Logger logger)
        {
            _logger = logger;
            _secureKeys = new Dictionary<string, SecureString>();
            _encryptedKeys = new Dictionary<string, byte[]>();
            
            // Generate entropy for additional encryption
            _entropy = GenerateEntropy();
        }

        public void StoreApiKey(string provider, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                throw new ArgumentException("Provider name cannot be empty", nameof(provider));
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _logger.Warn($"Attempted to store empty API key for provider {provider}");
                return;
            }

            lock (_lock)
            {
                // Clear existing key if present
                ClearApiKey(provider);

                try
                {
                    // Create SecureString
                    var secureKey = new SecureString();
                    foreach (char c in apiKey)
                    {
                        secureKey.AppendChar(c);
                    }
                    secureKey.MakeReadOnly();
                    
                    _secureKeys[provider] = secureKey;

                    // Also store encrypted version for persistence
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _encryptedKeys[provider] = ProtectData(apiKey);
                    }
                    else
                    {
                        // For non-Windows, use AES encryption
                        _encryptedKeys[provider] = AesEncrypt(apiKey);
                    }

                    _logger.Debug($"API key stored securely for provider {provider}");
                }
                finally
                {
                    // Clear the original string from memory
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        ClearString(apiKey);
                    }
                }
            }
        }

        public SecureString GetApiKey(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                return null;
            }

            lock (_lock)
            {
                if (_secureKeys.TryGetValue(provider, out var secureKey))
                {
                    return secureKey;
                }

                // Try to restore from encrypted storage
                if (_encryptedKeys.TryGetValue(provider, out var encryptedKey))
                {
                    try
                    {
                        string decrypted;
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            decrypted = UnprotectData(encryptedKey);
                        }
                        else
                        {
                            decrypted = AesDecrypt(encryptedKey);
                        }

                        var restoredKey = new SecureString();
                        foreach (char c in decrypted)
                        {
                            restoredKey.AppendChar(c);
                        }
                        restoredKey.MakeReadOnly();
                        
                        _secureKeys[provider] = restoredKey;
                        ClearString(decrypted);
                        
                        return restoredKey;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Failed to restore API key for provider {provider}: {ex.GetType().Name}");
                        return null;
                    }
                }

                return null;
            }
        }

        public string GetApiKeyForRequest(string provider)
        {
            var secureKey = GetApiKey(provider);
            if (secureKey == null)
            {
                return null;
            }

            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.SecureStringToGlobalAllocUnicode(secureKey);
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.ZeroFreeGlobalAllocUnicode(ptr);
                }
            }
        }

        public void ClearApiKey(string provider)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                return;
            }

            lock (_lock)
            {
                if (_secureKeys.TryGetValue(provider, out var secureKey))
                {
                    secureKey.Dispose();
                    _secureKeys.Remove(provider);
                }

                if (_encryptedKeys.ContainsKey(provider))
                {
                    // Overwrite encrypted data
                    var encryptedData = _encryptedKeys[provider];
                    if (encryptedData != null)
                    {
                        Array.Clear(encryptedData, 0, encryptedData.Length);
                    }
                    _encryptedKeys.Remove(provider);
                }

                _logger.Debug($"API key cleared for provider {provider}");
            }
        }

        public void ClearAllApiKeys()
        {
            lock (_lock)
            {
                foreach (var provider in _secureKeys.Keys.ToList())
                {
                    ClearApiKey(provider);
                }

                _logger.Debug("All API keys cleared from secure storage");
            }
        }

        private byte[] GenerateEntropy()
        {
            var entropy = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(entropy);
            }
            return entropy;
        }

        private byte[] ProtectData(string data)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var dataBytes = Encoding.UTF8.GetBytes(data);
                try
                {
                    // Use Windows DPAPI for encryption
                    return System.Security.Cryptography.ProtectedData.Protect(
                        dataBytes, 
                        _entropy, 
                        DataProtectionScope.CurrentUser);
                }
                finally
                {
                    Array.Clear(dataBytes, 0, dataBytes.Length);
                }
            }
            
            // Fallback to AES for non-Windows
            return AesEncrypt(data);
        }

        private string UnprotectData(byte[] encryptedData)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var decryptedBytes = System.Security.Cryptography.ProtectedData.Unprotect(
                    encryptedData, 
                    _entropy, 
                    DataProtectionScope.CurrentUser);
                
                try
                {
                    return Encoding.UTF8.GetString(decryptedBytes);
                }
                finally
                {
                    Array.Clear(decryptedBytes, 0, decryptedBytes.Length);
                }
            }
            
            return AesDecrypt(encryptedData);
        }

        private byte[] AesEncrypt(string plainText)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = DeriveKey(_entropy);
                aes.GenerateIV();
                
                using (var encryptor = aes.CreateEncryptor())
                {
                    var plainBytes = Encoding.UTF8.GetBytes(plainText);
                    try
                    {
                        var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
                        
                        // Combine IV and encrypted data
                        var result = new byte[aes.IV.Length + encrypted.Length];
                        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);
                        
                        return result;
                    }
                    finally
                    {
                        Array.Clear(plainBytes, 0, plainBytes.Length);
                    }
                }
            }
        }

        private string AesDecrypt(byte[] cipherText)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = DeriveKey(_entropy);
                
                // Extract IV from the beginning of the cipher text
                var iv = new byte[aes.BlockSize / 8];
                var encrypted = new byte[cipherText.Length - iv.Length];
                
                Buffer.BlockCopy(cipherText, 0, iv, 0, iv.Length);
                Buffer.BlockCopy(cipherText, iv.Length, encrypted, 0, encrypted.Length);
                
                aes.IV = iv;
                
                using (var decryptor = aes.CreateDecryptor())
                {
                    var decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);
                    try
                    {
                        return Encoding.UTF8.GetString(decrypted);
                    }
                    finally
                    {
                        Array.Clear(decrypted, 0, decrypted.Length);
                    }
                }
            }
        }

        private byte[] DeriveKey(byte[] entropy)
        {
            // SECURITY IMPROVEMENT: Use PBKDF2 with 100,000 iterations instead of simple SHA256
            // This provides significantly stronger protection against rainbow table and brute force attacks
            using (var pbkdf2 = new Rfc2898DeriveBytes(
                entropy, 
                entropy, 
                iterations: 100000, 
                HashAlgorithmName.SHA256))
            {
                return pbkdf2.GetBytes(32);
            }
        }

        private void ClearString(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return;
            }

            // This is a best-effort approach to clear string from memory
            // .NET strings are immutable, so we can't guarantee complete removal
            unsafe
            {
                fixed (char* ptr = str)
                {
                    for (int i = 0; i < str.Length; i++)
                    {
                        ptr[i] = '\0';
                    }
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    ClearAllApiKeys();
                    
                    // Clear entropy
                    if (_entropy != null)
                    {
                        Array.Clear(_entropy, 0, _entropy.Length);
                    }
                }

                _disposed = true;
            }
        }

        ~SecureApiKeyStorage()
        {
            Dispose(false);
        }
    }

    /// <summary>
    /// Extension methods for secure API key handling
    /// </summary>
    public static class SecureApiKeyExtensions
    {
        /// <summary>
        /// Safely uses an API key for a single operation and ensures cleanup
        /// </summary>
        public static TResult UseApiKey<TResult>(
            this ISecureApiKeyStorage storage,
            string provider,
            Func<string, TResult> operation)
        {
            var apiKey = storage.GetApiKeyForRequest(provider);
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException($"No API key found for provider {provider}");
            }

            try
            {
                return operation(apiKey);
            }
            finally
            {
                // Clear the temporary string
                if (!string.IsNullOrEmpty(apiKey))
                {
                    unsafe
                    {
                        fixed (char* ptr = apiKey)
                        {
                            for (int i = 0; i < apiKey.Length; i++)
                            {
                                ptr[i] = '\0';
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Async version of UseApiKey
        /// </summary>
        public static async Task<TResult> UseApiKeyAsync<TResult>(
            this ISecureApiKeyStorage storage,
            string provider,
            Func<string, Task<TResult>> operation)
        {
            var apiKey = storage.GetApiKeyForRequest(provider);
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException($"No API key found for provider {provider}");
            }

            try
            {
                return await operation(apiKey).ConfigureAwait(false);
            }
            finally
            {
                // Clear the temporary string
                if (!string.IsNullOrEmpty(apiKey))
                {
                    unsafe
                    {
                        fixed (char* ptr = apiKey)
                        {
                            for (int i = 0; i < apiKey.Length; i++)
                            {
                                ptr[i] = '\0';
                            }
                        }
                    }
                }
            }
        }
    }
}