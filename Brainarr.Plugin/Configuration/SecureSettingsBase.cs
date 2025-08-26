using System;
using NzbDrone.Core.ImportLists.Brainarr.Services.Security;
using NLog;

namespace NzbDrone.Core.ImportLists.Brainarr.Configuration
{
    /// <summary>
    /// Base class for secure settings management with API key protection.
    /// Integrates SecureApiKeyStorage to prevent plain-text API key storage.
    /// </summary>
    public abstract class SecureSettingsBase
    {
        private static readonly Lazy<ISecureApiKeyStorage> _keyStorage = 
            new Lazy<ISecureApiKeyStorage>(() => new SecureApiKeyStorage(LogManager.GetCurrentClassLogger()));
        
        protected ISecureApiKeyStorage KeyStorage => _keyStorage.Value;
        
        /// <summary>
        /// Securely stores an API key for the given provider
        /// </summary>
        protected void StoreApiKeySecurely(string provider, string apiKey)
        {
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                KeyStorage.StoreApiKey(provider, apiKey);
            }
        }
        
        /// <summary>
        /// Retrieves an API key securely for the given provider
        /// </summary>
        protected string GetApiKeySecurely(string provider)
        {
            return KeyStorage.GetApiKeyForRequest(provider);
        }
        
        /// <summary>
        /// Clears the stored API key for the given provider
        /// </summary>
        protected void ClearApiKeySecurely(string provider)
        {
            KeyStorage.ClearApiKey(provider);
        }
        
        /// <summary>
        /// Validates that an API key meets security requirements
        /// </summary>
        protected bool ValidateApiKeySecurity(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return false;
            
            // Enforce minimum API key length for security
            if (apiKey.Length < 20)
                return false;
            
            // Check for common test/invalid keys
            var invalidPatterns = new[] { "test", "demo", "xxx", "123", "key", "sample" };
            var lowerKey = apiKey.ToLowerInvariant();
            
            foreach (var pattern in invalidPatterns)
            {
                if (lowerKey.Contains(pattern))
                    return false;
            }
            
            // Ensure key has sufficient entropy (mix of characters)
            bool hasUpper = false, hasLower = false, hasDigit = false, hasSpecial = false;
            
            foreach (char c in apiKey)
            {
                if (char.IsUpper(c)) hasUpper = true;
                else if (char.IsLower(c)) hasLower = true;
                else if (char.IsDigit(c)) hasDigit = true;
                else if (!char.IsLetterOrDigit(c)) hasSpecial = true;
            }
            
            // Require at least 3 out of 4 character types
            int complexity = (hasUpper ? 1 : 0) + (hasLower ? 1 : 0) + 
                            (hasDigit ? 1 : 0) + (hasSpecial ? 1 : 0);
            
            return complexity >= 3 || apiKey.Length >= 32; // Long keys are acceptable even with lower complexity
        }
    }
}